using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Master.Sequences.FiveAxis
{
    /// <summary>The SOP's step 3.2 results (plus A0 / C0 they are relative to).</summary>
    public sealed class KinematicParameters
    {
        public double A0 { get; set; }

        public double C0 { get; set; }

        /// <summary>C axis centre Y, mm.</summary>
        public double YC0 { get; set; }

        /// <summary>Eccentricity of the target about the C axis at C = C0, mm.</summary>
        public double Rx { get; set; }

        public double Ry { get; set; }

        /// <summary>A axis pivot, mm.</summary>
        public double XA { get; set; }

        public double ZA { get; set; }

        /// <summary>A to C lever arm, mm.</summary>
        public double Lx { get; set; }

        public double Lz { get; set; }

        /// <summary>M_hand_eye, 3 x 4: gantry = R * camera + t, as fitted (step 2).</summary>
        public double[,] HandEye { get; set; }

        /// <summary>The rotation steps 3 and 4 use: the fitted R_hand_eye, made a proper rotation and, when needed, turned 180 deg about the camera axis.</summary>
        public double[,] Rotation { get; set; }

        public bool HandEyeFlipped { get; set; }

        /// <summary>True when A and C were read as R = Ry(A) * Rz(C) instead of the SOP's Y / Z Euler angles of RQDecomp3x3.</summary>
        public bool AOuterAngles { get; set; }

        /// <summary>RMS difference, degrees, between the A / C angles the camera saw and the ones the images were taken at.</summary>
        public double AngleMismatchDegrees { get; set; }

        /// <summary>The A and C angles seen in images 10 to 34 (step 3.1), degrees.</summary>
        public double[] AList { get; set; }

        public double[] CList { get; set; }

        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// SOP steps 2, 3 and 4.2: hand-eye matrix (cv2.estimateAffine3D), the A and C angles seen by the camera
    /// (cv2.Rodrigues, cv2.RQDecomp3x3), the C axis centre, the A axis pivot and the lever arm (cv2.solve),
    /// and the gantry position that keeps the target in view. All OpenCvSharp calls; the numbered comments follow the slides.
    /// </summary>
    public static class KinematicSolver
    {
        /// <summary>
        /// Steps 2 to 3.2.
        /// </summary>
        /// <param name="camera">The calibration of step 1.4 over all 34 images (image 1 is index 0).</param>
        /// <param name="gantry9">Gantry X, Y, Z of images 1 to 9.</param>
        /// <param name="gantry25">Gantry X, Y, Z of images 10 to 34.</param>
        /// <param name="commandedA">The A angle each of images 10 to 34 was taken at.</param>
        /// <param name="commandedC">The C angle each of images 10 to 34 was taken at.</param>
        public static KinematicParameters Solve(
            CameraCalibrationResult camera, double[][] gantry9, double[][] gantry25,
            double[] commandedA, double[] commandedC, double a0, double c0)
        {
            if (camera.Tvecs.Length != 34 || gantry9.Length != 9 || gantry25.Length != 25
                || commandedA.Length != 25 || commandedC.Length != 25)
                throw new ArgumentException("The solver needs 34 images: 9 translation and 25 rotation.");

            var result = new KinematicParameters { A0 = a0, C0 = c0 };

            // Step 2: P_cam_9pts = tvecs[0..8], P_gantry_9pts = the gantry positions of images 1..9.
            var cam9 = new double[9][];
            Array.Copy(camera.Tvecs, cam9, 9);
            bool planar;
            double[] normal;
            double[,] handEye = EstimateHandEye(cam9, gantry9, out planar, out normal);
            result.HandEye = handEye;
            if (planar)
            {
                result.Warnings.Add("The nine hand-eye points lie in one plane (the gantry only moved in X and Y), so cv2.estimateAffine3D cannot determine "
                    + "the out-of-plane column of R_hand_eye. It was completed as a rotation (right-handed, scale from the in-plane columns).");
            }

            // The matrix is a rotation (the SOP uses it as one). A fit with the camera on the gantry can come out
            // as -R (the target moves against the camera), and a planar fit cannot tell the two apart: they differ by
            // 180 degrees about the camera axis. Step 3.1 turns the camera's view of the plate into A and C angles,
            // and the images were taken at known A and C, so whichever of the two reproduces those angles is used.
            double[,] linear = LinearPart(handEye);
            if (Determinant(linear) < 0)
            {
                linear = Negate(linear);
                result.Warnings.Add("R_hand_eye (the camera axes against the gantry axes) came out mirrored (determinant < 0), so "
                    + "its negative is used. This is the camera moving on the gantry with both frames right-handed.");
            }

            // Step 3.1: A and C of each of images 10..34 from the plate's rotation:
            //   R_cam = cv2.Rodrigues(rvec);  R_board = R_hand_eye @ R_cam;  euler = cv2.RQDecomp3x3(R_board);
            //   A_m = A0 + euler[1];  C_m = C0 + euler[2].
            // The SOP's reading takes the Y and Z Euler angles of RQDecomp3x3, which factors the rotation as Rz * Ry * Rx:
            // exact when the plate is turned about Y first and then about Z. The step 4.2 formulas put the A axis outside the
            // C axis (plate turned by C, then tilted by A, R = Ry(A) * Rz(C)), which that reading only follows for a small tilt.
            // Both are tried against the commanded angles, the SOP's own reading preferred unless the other is clearly better.
            const double AOuterPenalty = 1.0;
            double[,] rHandEye = null;
            double bestScore = double.MaxValue, bestMismatch = 0, referenceMismatch = double.MaxValue;
            double sopBestMismatch = double.MaxValue;
            double[] aList = null, cList = null;
            foreach (bool aOuter in new[] { false, true })
            {
                foreach (bool flipped in new[] { false, true })
                {
                    double[,] candidate = flipped ? HalfTurnAbout(linear, normal) : linear;
                    var a = new double[25];
                    var c = new double[25];
                    double sum = 0;
                    for (int m = 0; m < 25; m++)
                    {
                        double[,] rCam;
                        double[,] jacobian;
                        Cv2.Rodrigues(camera.Rvecs[9 + m], out rCam, out jacobian);
                        double[,] rBoard = Mul(candidate, rCam);

                        if (aOuter)
                        {
                            a[m] = a0 + Math.Atan2(rBoard[0, 2], rBoard[2, 2]) * 180 / Math.PI;
                            c[m] = c0 + Math.Atan2(rBoard[1, 0], rBoard[1, 1]) * 180 / Math.PI;
                        }
                        else
                        {
                            double[,] r, q, qx, qy, qz;
                            Vec3d euler = Cv2.RQDecomp3x3(rBoard, out r, out q, out qx, out qy, out qz);
                            a[m] = a0 + euler.Item1;
                            c[m] = c0 + euler.Item2;
                        }
                        double da = Wrap(a[m] - commandedA[m]), dc = Wrap(c[m] - commandedC[m]);
                        sum += da * da + dc * dc;
                    }

                    double mismatch = Math.Sqrt(sum / 50);
                    double score = mismatch + (aOuter ? AOuterPenalty : 0);
                    if (!aOuter)
                        sopBestMismatch = Math.Min(sopBestMismatch, mismatch);

                    if (score < bestScore)
                    {
                        if (bestScore < double.MaxValue)
                            referenceMismatch = Math.Min(referenceMismatch, bestMismatch);
                        bestScore = score;
                        bestMismatch = mismatch;
                        rHandEye = candidate;
                        aList = a;
                        cList = c;
                        result.HandEyeFlipped = flipped;
                        result.AOuterAngles = aOuter;
                    }
                    else
                    {
                        referenceMismatch = Math.Min(referenceMismatch, mismatch);
                    }
                }
            }

            result.AngleMismatchDegrees = bestMismatch;
            if (bestMismatch > 5)
            {
                throw new InvalidOperationException("The A and C angles the camera sees in images 10 to 34 do not follow the angles they were taken at "
                    + "(RMS difference " + bestMismatch.ToString("0.0") + " deg; " + sopBestMismatch.ToString("0.0") + " deg read the way the SOP does): "
                    + "check the sign and direction of the A and C axes, the plate pitch and that the plate is in view in every image.");
            }
            if (bestMismatch > 1.5)
            {
                result.Warnings.Add("The camera's A and C angles differ from the commanded ones by " + bestMismatch.ToString("0.0")
                    + " deg RMS. A tilt or rotation error of that size is what the calibration is meant to find, but check the plate and the camera calibration if it is not expected.");
            }
            if (result.AOuterAngles)
            {
                result.Warnings.Add("SOP step 3.1 as written (the Y and Z Euler angles of cv2.RQDecomp3x3) reads the commanded A and C with "
                    + sopBestMismatch.ToString("0.0") + " deg RMS error here, so the angles were read for the A axis outside the C axis (R = Ry(A) * Rz(C), "
                    + "the stacking the step 4.2 formulas use) instead: " + bestMismatch.ToString("0.0") + " deg RMS. Please confirm this with the SOP's author.");
            }
            if (result.HandEyeFlipped)
                result.Warnings.Add("The hand-eye rotation was turned 180 deg about the plate normal to match the commanded A and C angles ("
                    + bestMismatch.ToString("0.0") + " deg RMS against " + referenceMismatch.ToString("0.0") + " deg without).");

            result.Rotation = rHandEye;
            result.AList = aList;
            result.CList = cList;

            // Step 3.2: true target centre in the gantry frame = gantry position + the target's offset in the field of view:
            //   P_true = P_gantry_25 + (R_hand_eye @ (tvecs_25 - tvecs[4]).T).T
            double[] tRef = camera.Tvecs[4];
            var xTrue = new double[25];
            var yTrue = new double[25];
            var zTrue = new double[25];
            var dA = new double[25];
            var dC = new double[25];
            for (int m = 0; m < 25; m++)
            {
                double[] offset = Mul(rHandEye, Sub(camera.Tvecs[9 + m], tRef));
                xTrue[m] = gantry25[m][0] + offset[0];
                yTrue[m] = gantry25[m][1] + offset[1];
                zTrue[m] = gantry25[m][2] + offset[2];
                dA[m] = (aList[m] - a0) * Math.PI / 180;
                dC[m] = (cList[m] - c0) * Math.PI / 180;
            }

            // 1. C axis centre Y_C0 and the target's eccentricity (rx, ry):  Y = Y_C0 + rx sin(dC) + ry cos(dC)   (cv2.solve, DECOMP_SVD)
            var my = new double[25, 3];
            for (int m = 0; m < 25; m++)
            {
                my[m, 0] = 1;
                my[m, 1] = Math.Sin(dC[m]);
                my[m, 2] = Math.Cos(dC[m]);
            }
            double[] py = Solve(my, yTrue);
            result.YC0 = py[0];
            result.Rx = py[1];
            result.Ry = py[2];

            // 2. A axis pivot (X_A, Z_A) and lever arm (Lx, Lz).
            var mxz = new double[50, 4];
            var bxz = new double[50];
            for (int m = 0; m < 25; m++)
            {
                double xc = result.Rx * Math.Cos(dC[m]) - result.Ry * Math.Sin(dC[m]);
                mxz[m, 0] = 1;
                mxz[m, 1] = 0;
                mxz[m, 2] = Math.Cos(dA[m]);
                mxz[m, 3] = Math.Sin(dA[m]);
                mxz[25 + m, 0] = 0;
                mxz[25 + m, 1] = 1;
                mxz[25 + m, 2] = -Math.Sin(dA[m]);
                mxz[25 + m, 3] = Math.Cos(dA[m]);
                bxz[m] = xTrue[m] - xc * Math.Cos(dA[m]);
                bxz[25 + m] = zTrue[m] + xc * Math.Sin(dA[m]);
            }
            double[] pxz = Solve(mxz, bxz);
            result.XA = pxz[0];
            result.ZA = pxz[1];
            result.Lx = pxz[2];
            result.Lz = pxz[3];

            return result;
        }

        /// <summary>Step 4.2: the gantry X, Y, Z that keeps the target in view at this A and C.</summary>
        public static double[] Predict(KinematicParameters k, double aDegrees, double cDegrees)
        {
            double dA = (aDegrees - k.A0) * Math.PI / 180;
            double dC = (cDegrees - k.C0) * Math.PI / 180;

            double xcm = k.Rx * Math.Cos(dC) - k.Ry * Math.Sin(dC);
            double ycm = k.Rx * Math.Sin(dC) + k.Ry * Math.Cos(dC);

            return new[]
            {
                k.XA + (k.Lx + xcm) * Math.Cos(dA) + k.Lz * Math.Sin(dA),
                k.YC0 + ycm,
                k.ZA - (k.Lx + xcm) * Math.Sin(dA) + k.Lz * Math.Cos(dA),
            };
        }

        /// <summary>
        /// Step 4.3: the centre distance error E, mm. The plate centre (P25) in this image is compared with
        /// where it was in the reference image 5, as the camera sees it, in the gantry frame: E = sqrt(dX^2 + dY^2).
        /// </summary>
        public static double CentreError(KinematicParameters k, double[] tvec, double[] tvecReference)
        {
            double[] d = Mul(k.Rotation, Sub(tvec, tvecReference));
            return Math.Sqrt(d[0] * d[0] + d[1] * d[1]);
        }

        // ==================================================================
        // Step 2: hand-eye
        // ==================================================================

        /// <summary>
        /// cv2.estimateAffine3D(P_cam, P_gantry): the 3 x 4 matrix M with gantry = M * [camera; 1].
        /// </summary>
        /// <param name="planar">
        /// True when the points lie in one plane. cv2.estimateAffine3D cannot determine the out-of-plane
        /// column then, so it is the cross product of the two fitted columns, scaled to their mean length
        /// (the in-plane part is a cv2.solve over the plane's two directions from cv2.SVDecomp).
        /// </param>
        /// <param name="normal">The plane's normal in the camera frame.</param>
        public static double[,] EstimateHandEye(double[][] src, double[][] dst, out bool planar, out double[] normal)
        {
            int n = src.Length;
            if (n < 4 || dst.Length != n)
                throw new ArgumentException("The hand-eye fit needs at least 4 point pairs.");

            double[] ps = Centroid(src), pd = Centroid(dst);

            // The plane of the camera-side points: right singular vectors of the centred points.
            var centred = new double[n, 3];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < 3; j++)
                    centred[i, j] = src[i][j] - ps[j];

            double[] sv;
            double[][] axes;
            Svd(centred, out sv, out axes);
            normal = axes[2];

            if (sv[1] < 1e-3 * sv[0])
                throw new InvalidOperationException("The hand-eye points lie on a line: the gantry must move in both X and Y between images 1 and 9.");

            planar = sv[2] < 0.03 * sv[0];
            var m = new double[3, 4];

            if (!planar)
            {
                using (Mat from = Mat.FromArray(src.Select(p => new Point3d(p[0], p[1], p[2])).ToArray()))
                using (Mat to = Mat.FromArray(dst.Select(p => new Point3d(p[0], p[1], p[2])).ToArray()))
                using (var affine = new Mat())
                using (var inliers = new Mat())
                {
                    int retval = Cv2.EstimateAffine3D(from, to, affine, inliers);
                    if (retval == 0 || affine.Rows != 3 || affine.Cols != 4)
                        throw new InvalidOperationException("cv2.estimateAffine3D found no hand-eye matrix for the nine points.");

                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 4; c++)
                            m[r, c] = affine.Get<double>(r, c);
                }
                return m;
            }

            double[] e1 = axes[0], e2 = axes[1], e3 = axes[2];

            var inPlane = new double[n, 2];
            var target = new double[n, 3];
            for (int i = 0; i < n; i++)
            {
                double[] s = Sub(src[i], ps);
                inPlane[i, 0] = Dot(s, e1);
                inPlane[i, 1] = Dot(s, e2);
                for (int j = 0; j < 3; j++)
                    target[i, j] = dst[i][j] - pd[j];
            }

            // inPlane * X = target, X is 2 x 3: its rows are the images of e1 and e2.
            double[,] x = SolveMany(inPlane, target);
            double[] l1 = { x[0, 0], x[0, 1], x[0, 2] };
            double[] l2 = { x[1, 0], x[1, 1], x[1, 2] };

            double[] l3 = Cross(l1, l2);
            double length = Norm(l3);
            if (length < 1e-12)
                throw new InvalidOperationException("The hand-eye fit is degenerate: the camera's X and Y axes map to the same gantry direction.");

            // R e3 = +/-(R e1 x R e2) = +/-(l1 x l2); the sign is that of e3 against e1 x e2, so that R is a proper rotation.
            double handed = Dot(e3, Cross(e1, e2)) < 0 ? -1 : 1;
            double scale = handed * (Norm(l1) + Norm(l2)) / 2 / length;
            for (int i = 0; i < 3; i++)
                l3[i] *= scale;

            // A = [l1 l2 l3] * [e1 e2 e3]^T
            var rot = new double[3, 3];
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    rot[row, col] = l1[row] * e1[col] + l2[row] * e2[col] + l3[row] * e3[col];

            double[] shift = Sub(pd, Mul(rot, ps));
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                    m[row, col] = rot[row, col];
                m[row, 3] = shift[row];
            }
            return m;
        }

        // ==================================================================
        // OpenCV helpers: cv2.solve(DECOMP_SVD) and cv2.SVDecomp on plain arrays
        // ==================================================================

        private static double[] Solve(double[,] a, double[] b)
        {
            var rhs = new double[b.Length, 1];
            for (int i = 0; i < b.Length; i++)
                rhs[i, 0] = b[i];

            double[,] x = SolveMany(a, rhs);
            var result = new double[x.GetLength(0)];
            for (int i = 0; i < result.Length; i++)
                result[i] = x[i, 0];
            return result;
        }

        private static double[,] SolveMany(double[,] a, double[,] b)
        {
            using (Mat am = Mat.FromArray(a))
            using (Mat bm = Mat.FromArray(b))
            using (var xm = new Mat())
            {
                if (!Cv2.Solve(am, bm, xm, DecompTypes.SVD))
                    throw new InvalidOperationException("cv2.solve could not solve the least-squares problem.");

                var x = new double[xm.Rows, xm.Cols];
                for (int i = 0; i < xm.Rows; i++)
                    for (int j = 0; j < xm.Cols; j++)
                        x[i, j] = xm.Get<double>(i, j);
                return x;
            }
        }

        /// <summary>Singular values (descending) and right singular vectors of an n x 3 matrix.</summary>
        private static void Svd(double[,] a, out double[] values, out double[][] rightVectors)
        {
            using (Mat am = Mat.FromArray(a))
            using (var w = new Mat())
            using (var u = new Mat())
            using (var vt = new Mat())
            {
                Cv2.SVDecomp(am, w, u, vt);
                values = new[] { w.Get<double>(0, 0), w.Get<double>(1, 0), w.Get<double>(2, 0) };
                rightVectors = new double[3][];
                for (int i = 0; i < 3; i++)
                    rightVectors[i] = new[] { vt.Get<double>(i, 0), vt.Get<double>(i, 1), vt.Get<double>(i, 2) };
            }
        }

        // ==================================================================
        // 3 x 3 arithmetic
        // ==================================================================

        private static double[,] LinearPart(double[,] m)
        {
            var r = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i, j] = m[i, j];
            return r;
        }

        private static double Determinant(double[,] m)
        {
            return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                 - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                 + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        }

        private static double[,] Negate(double[,] m)
        {
            var r = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i, j] = -m[i, j];
            return r;
        }

        /// <summary>R * (half a revolution about n): the same fit with the in-plane directions of the camera reversed.</summary>
        private static double[,] HalfTurnAbout(double[,] m, double[] n)
        {
            var turn = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    turn[i, j] = 2 * n[i] * n[j] - (i == j ? 1 : 0);
            return Mul(m, turn);
        }

        private static double[,] Mul(double[,] a, double[,] b)
        {
            var c = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++)
                        c[i, j] += a[i, k] * b[k, j];
            return c;
        }

        private static double[] Mul(double[,] a, double[] v)
        {
            var r = new double[3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i] += a[i, j] * v[j];
            return r;
        }

        private static double[] Sub(double[] a, double[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double Norm(double[] v)
        {
            return Math.Sqrt(Dot(v, v));
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        /// <summary>An angle difference in (-180, 180].</summary>
        private static double Wrap(double degrees)
        {
            double d = degrees % 360;
            if (d > 180)
                d -= 360;
            else if (d <= -180)
                d += 360;
            return d;
        }

        private static double[] Centroid(double[][] pts)
        {
            var c = new double[3];
            foreach (double[] p in pts)
                for (int i = 0; i < 3; i++)
                    c[i] += p[i] / pts.Length;
            return c;
        }
    }
}
