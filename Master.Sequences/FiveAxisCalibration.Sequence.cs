using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Master.Sequences.FiveAxis;
using Nova.Abstractions.Services;

namespace Master.Sequences
{
    public sealed partial class FiveAxisCalibration
    {
        // ==================================================================
        //  THE 5 AXIS CALIBRATION SEQUENCE
        //  (SCUD3.1 Kinematic Calibration SOP, slides 4 to 12)
        // ==================================================================
        //  teach.Station       1 or 2, the station picked on the screen
        //  teach.Translation   point1 .. point9    X, Y        gantry positions of images 1 to 9
        //  teach.Rotation      point10 .. point34  X, Y, Z, A, C   poses of images 10 to 34
        //  s                   plate pitch, image size, vision address, feed ... (FiveAxisSettings.json)
        //  token               cancelled when the Stop button is pressed
        //  throw               ends the run; the message is shown on the screen
        //
        //  Image n is vision command "C5A,n". Moves are G-code blocks through the CNC channel (_motion),
        //  all five axes together. Axis(...) / Move(...) at the bottom are for single servo axes.
        // ==================================================================
        private async Task<FiveAxisResult> Sequence(FiveAxisTeach teach, FiveAxisSettings s, CancellationToken token)
        {
            int station = teach.Station;
            CheckTables(teach);

            // Step 1.1: the plate's dot positions, mm (P1 = (-3d, -3d) .. P49 = (+3d, +3d), Z = 0).
            double[][] plate = PlateDots(s.PlateSpacingMm);

            // The reference position the images 1 to 9 are taken at: A0 and C0 from the taught rotation table
            // (its middle A and its first C), Z0 where the axis is now, unless FiveAxisSettings.json sets them.
            double a0 = s.A0 ?? teach.Rotation.Average(p => p.A);
            double c0 = s.C0 ?? teach.Rotation[0].C;
            double z0 = s.Z0 ?? _motion.ReadPose(station, s).Z;
            Log("Station " + station + ": reference Z0 " + z0.ToString("0.###") + ", A0 " + a0.ToString("0.###") + ", C0 " + c0.ToString("0.###") + ".");

            var views = new List<double[][]>();          // the dots' pixels of every image
            var gantry9 = new double[9][];                // gantry X, Y, Z of images 1 to 9
            var gantry25 = new double[25][];              // gantry X, Y, Z of images 10 to 34
            var commandedA = new double[25];
            var commandedC = new double[25];

            FiveAxisResult result;
            using (IFiveAxisVision vision = VisionFactory(s))
            {
                await vision.ConnectAsync(token).ConfigureAwait(false);

                // Step 1.2: the camera moves in X and Y only, plate in view, A0 / C0 / Z0 fixed: images 1 to 9.
                for (int i = 0; i < 9; i++)
                {
                    FiveAxisTranslationPoint p = teach.Translation[i];
                    gantry9[i] = new[] { p.X, p.Y, z0 };
                    var pose = new FiveAxisPose { X = p.X, Y = p.Y, Z = z0, A = a0, C = c0 };
                    views.Add(await CaptureAt(station, i + 1, pose, vision, s, token).ConfigureAwait(false));
                }

                // Step 1.3: A (+/-20 deg) and C (0 to 288 deg), X / Y / Z compensating to keep the plate in view: images 10 to 34.
                for (int m = 0; m < 25; m++)
                {
                    FiveAxisRotationPoint p = teach.Rotation[m];
                    gantry25[m] = new[] { p.X, p.Y, p.Z };
                    commandedA[m] = p.A;
                    commandedC[m] = p.C;
                    var pose = new FiveAxisPose { X = p.X, Y = p.Y, Z = p.Z, A = p.A, C = p.C };
                    views.Add(await CaptureAt(station, 10 + m, pose, vision, s, token).ConfigureAwait(false));
                }

                // Steps 1.4 to 3.2: pure calculation from the 34 pictures.
                Report("Calculating the camera and the kinematics");
                token.ThrowIfCancellationRequested();
                CameraCalibrationResult camera = CameraCalibrator.Calibrate(plate, views, s.ImageWidth, s.ImageHeight);       // 1.4: K, D, rvecs, tvecs
                KinematicParameters kinematics = KinematicSolver.Solve(camera, gantry9, gantry25, commandedA, commandedC, a0, c0); // 2, 3.1, 3.2
                Log("Camera RMS " + camera.Rms.ToString("0.000") + " px, fx " + camera.Fx.ToString("0.0") + ", fy " + camera.Fy.ToString("0.0")
                    + ". Y_C0 " + kinematics.YC0.ToString("0.###") + ", rx " + kinematics.Rx.ToString("0.###") + ", ry " + kinematics.Ry.ToString("0.###")
                    + ", X_A " + kinematics.XA.ToString("0.###") + ", Z_A " + kinematics.ZA.ToString("0.###")
                    + ", Lx " + kinematics.Lx.ToString("0.###") + ", Lz " + kinematics.Lz.ToString("0.###") + ".");

                result = new FiveAxisResult
                {
                    Station = station,
                    Z0 = z0,
                    A0 = a0,
                    C0 = c0,
                    CameraRms = camera.Rms,
                    Fx = camera.Fx,
                    Fy = camera.Fy,
                    Cx = camera.Cx,
                    Cy = camera.Cy,
                    Distortion = camera.Distortion,
                    HandEye = ToRows(kinematics.HandEye),
                    YC0 = kinematics.YC0,
                    Rx = kinematics.Rx,
                    Ry = kinematics.Ry,
                    XA = kinematics.XA,
                    ZA = kinematics.ZA,
                    Lx = kinematics.Lx,
                    Lz = kinematics.Lz,
                };
                result.Warnings.AddRange(kinematics.Warnings);
                if (camera.Rms > 1.0)
                    result.Warnings.Add("The camera's re-projection error is " + camera.Rms.ToString("0.00") + " px, which is high: check the plate pitch and that the dots are found in every image.");

                // Step 4: quick validation. Step 1.3's A and C again, X / Y / Z from the model (4.2), P25 measured against image 5 (4.3).
                for (int m = 0; m < 25; m++)
                {
                    double[] predicted = KinematicSolver.Predict(kinematics, commandedA[m], commandedC[m]);
                    var pose = new FiveAxisPose { X = predicted[0], Y = predicted[1], Z = predicted[2], A = commandedA[m], C = commandedC[m] };
                    double[][] dots = await CaptureAt(station, 10 + m, pose, vision, s, token, "Validation " + (m + 1) + "/25").ConfigureAwait(false);

                    double[] rvec, tvec;
                    CameraCalibrator.SolvePose(camera, plate, dots, out rvec, out tvec);

                    result.Validation.Add(new FiveAxisValidationPoint
                    {
                        Name = teach.Rotation[m].Name,
                        A = commandedA[m],
                        C = commandedC[m],
                        XPredicted = predicted[0],
                        YPredicted = predicted[1],
                        ZPredicted = predicted[2],
                        ErrorMm = KinematicSolver.CentreError(kinematics, tvec, camera.Tvecs[4]),
                    });
                }
            }

            result.ErrorMax = result.Validation.Max(v => v.ErrorMm);
            result.ErrorAverage = result.Validation.Average(v => v.ErrorMm);
            result.FinishedUtc = DateTime.UtcNow;
            Log("Validation: E max " + result.ErrorMax.ToString("0.###") + " mm, E average " + result.ErrorAverage.ToString("0.###") + " mm.");

            // The SOP gives no limit for E, so the run is complete whatever it is; the screen and the log show it.
            return result;
        }

        // ------------------------------------------------------------------
        //  Steps the sequence is made of
        // ------------------------------------------------------------------

        /// <summary>Moves to the pose, waits for the picture to settle, asks the vision system for image <paramref name="image"/>.</summary>
        private async Task<double[][]> CaptureAt(
            int station, int image, FiveAxisPose pose, IFiveAxisVision vision, FiveAxisSettings s, CancellationToken token, string label = null)
        {
            string name = label ?? "Image " + image + "/34";

            Report(name + ": moving");
            await Go(station, pose, s, token).ConfigureAwait(false);

            Report(name + ": taking the picture");
            VisionCapture capture = await vision.CaptureAsync(image, token).ConfigureAwait(false);
            Log(name + " at " + pose + ": " + capture.Points.Length + " points.");
            return capture.Points;
        }

        /// <summary>All five axes to the pose as one G-code move, then the settle time.</summary>
        private async Task Go(int station, FiveAxisPose pose, FiveAxisSettings s, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await _motion.MoveAsync(station, pose, s, token).ConfigureAwait(false);

            if (s.SettleMs > 0)
                await Task.Delay(s.SettleMs, token).ConfigureAwait(false);
        }

        /// <summary>Step 1.1: P1 .. P49 of the 7 x 7 plate, X along the rows and Y along the columns (the slide's table).</summary>
        private static double[][] PlateDots(double d)
        {
            var dots = new double[C5AVisionClient.PointCount][];
            for (int k = 0; k < dots.Length; k++)
                dots[k] = new[] { (k / 7 - 3) * d, (k % 7 - 3) * d };
            return dots;
        }

        /// <summary>The tables must hold the 9 + 25 taught poses and look taught (not all zero).</summary>
        private static void CheckTables(FiveAxisTeach teach)
        {
            if (teach.Translation == null || teach.Translation.Count != 9)
                throw new InvalidOperationException("The translation table needs 9 points (point1 .. point9).");
            if (teach.Rotation == null || teach.Rotation.Count != 25)
                throw new InvalidOperationException("The rotation table needs 25 points (point10 .. point34).");

            if (Range(teach.Translation.Select(p => p.X)) < 0.01 || Range(teach.Translation.Select(p => p.Y)) < 0.01)
                throw new InvalidOperationException("The translation table is not taught: the X and Y of point1 .. point9 must differ.");
            if (Range(teach.Rotation.Select(p => p.A)) < 1 || Range(teach.Rotation.Select(p => p.C)) < 1)
                throw new InvalidOperationException("The rotation table is not taught: the A and C of point10 .. point34 must differ.");
        }

        private static double Range(IEnumerable<double> values)
        {
            double[] v = values.ToArray();
            return v.Max() - v.Min();
        }

        private static double[][] ToRows(double[,] m)
        {
            var rows = new double[m.GetLength(0)][];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new double[m.GetLength(1)];
                for (int j = 0; j < rows[i].Length; j++)
                    rows[i][j] = m[i, j];
            }
            return rows;
        }

        private static void Log(string text)
        {
            FiveAxisLog.Info("Sequence", text);
        }

        // ------------------------------------------------------------------
        //  A single servo axis (not the CNC channel's, which only G-code positions)
        // ------------------------------------------------------------------

        /// <summary>Moves one servo axis to a position and waits; stops the run if it does not get there or Stop was pressed.</summary>
        private async Task Move(string axisId, double position, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            const double speed = 10;
            if (!await Axis(axisId).MoveAbsAsync(position, speed).ConfigureAwait(false))
                throw new InvalidOperationException(axisId + " did not reach " + position + ".");
        }
    }
}

