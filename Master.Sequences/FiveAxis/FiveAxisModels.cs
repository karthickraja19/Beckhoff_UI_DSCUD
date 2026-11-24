using System;
using System.Collections.Generic;

namespace Master.Sequences.FiveAxis
{
    /// <summary>One place of the five axes: X, Y, Z in mm, A and C in degrees (the SOP's gantry columns).</summary>
    public sealed class FiveAxisPose
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }

        public double A { get; set; }

        public double C { get; set; }

        public override string ToString()
        {
            return "X " + X.ToString("0.###") + "  Y " + Y.ToString("0.###") + "  Z " + Z.ToString("0.###")
                + "  A " + A.ToString("0.###") + "  C " + C.ToString("0.###");
        }
    }

    /// <summary>
    /// Everything the 5 axis calibration needs that the PLC and the taught tables do not hold.
    /// Read from Data\Calibration\FiveAxisSettings.json; a value of 0 where the SOP gives none
    /// (plate pitch, image size) is refused at Start, never guessed.
    /// </summary>
    public sealed class FiveAxisSettings
    {
        // ---- Vision (Five-Axis Calibration.xlsx, sheet Protocol: CCD1, Ethernet, server, port 8000)
        public string VisionIp { get; set; } = "127.0.0.1";

        public int VisionPort { get; set; } = 8000;

        /// <summary>Appended to every command ("C5A,1"); the protocol sheet names none, so empty.</summary>
        public string VisionTerminator { get; set; } = string.Empty;

        /// <summary>How long one capture may take, from the command to the last point.</summary>
        public int VisionTimeoutMs { get; set; } = 15000;

        /// <summary>A capture whose result is 0 (positioning NG) or that times out is asked for again this many more times.</summary>
        public int VisionRetries { get; set; } = 2;

        // ---- The plate and the camera (SOP step 1.1 and 1.4)
        /// <summary>The spacing d between neighbouring dots of the 7 x 7 plate, mm. Required.</summary>
        public double PlateSpacingMm { get; set; }

        /// <summary>The camera image size, pixels (cv2.calibrateCamera's imageSize). Required.</summary>
        public int ImageWidth { get; set; }

        public int ImageHeight { get; set; }

        // ---- Reference position (SOP: Z0, A0, C0 of images 1 to 9)
        /// <summary>Null = the position the axes are at when Start is pressed.</summary>
        public double? Z0 { get; set; }

        public double? A0 { get; set; }

        public double? C0 { get; set; }

        // ---- Motion
        /// <summary>"MDI" (default): each pose is one G-code block in the CNC channel's MDI mode. "Auto": the block is written to <see cref="AutoProgramFile"/> and the channel runs that program in Auto mode.</summary>
        public string CncMode { get; set; } = "MDI";

        /// <summary>Auto mode only: the full path of the .nc program the CNC channel runs (the HMI rewrites it before every move). Must be a path the HMI can write and the CNC can read.</summary>
        public string AutoProgramFile { get; set; } = string.Empty;

        /// <summary>Path feed of the G-code moves, mm/min.</summary>
        public double FeedMmPerMin { get; set; } = 1000;

        /// <summary>Wait after a move before the camera is triggered, ms.</summary>
        public int SettleMs { get; set; } = 500;

        /// <summary>One move may take this long, ms.</summary>
        public int MoveTimeoutMs { get; set; } = 60000;

        /// <summary>After a move every axis must be within this of its target (mm, deg). 0 = not checked.</summary>
        public double InPositionTolerance { get; set; } = 0.05;

        // ---- Axes. The SOP's X / Y / Z / A / C, as the HMI names the channel's axes (Data\CncRecipe.json:
        // X1, Y1, Z1, A, R) and as the CNC program names them in the G-code block (X1, Y1, Z1, B1, C1).
        public string AxisX { get; set; } = "X1";

        public string AxisY { get; set; } = "Y1";

        public string AxisZ { get; set; } = "Z1";

        public string AxisA { get; set; } = "A";

        public string AxisC { get; set; } = "R";

        public string GCodeX { get; set; } = "X1";

        public string GCodeY { get; set; } = "Y1";

        public string GCodeZ { get; set; } = "Z1";

        public string GCodeA { get; set; } = "B1";

        public string GCodeC { get; set; } = "C1";

        /// <summary>Why the calibration cannot start, or an empty string when the settings are complete.</summary>
        public string Problem()
        {
            if (PlateSpacingMm <= 0)
                return "PlateSpacingMm (the dot spacing d of the calibration plate) is not set: enter it under Setup on the 5-Axis tab.";
            if (ImageWidth <= 0 || ImageHeight <= 0)
                return "ImageWidth and ImageHeight (the camera image size in pixels) are not set: enter them under Setup on the 5-Axis tab.";
            if (string.IsNullOrWhiteSpace(VisionIp) || VisionPort <= 0)
                return "VisionIp / VisionPort are not set.";
            if (FeedMmPerMin <= 0)
                return "FeedMmPerMin must be more than 0.";
            if (IsAuto && string.IsNullOrWhiteSpace(AutoProgramFile))
                return "CncMode is Auto but AutoProgramFile (the .nc file the CNC runs) is not set.";
            return string.Empty;
        }

        public bool IsAuto
        {
            get { return string.Equals(CncMode, "Auto", StringComparison.OrdinalIgnoreCase); }
        }
    }

    /// <summary>The vision system's answer to one C5A command.</summary>
    public sealed class VisionCapture
    {
        /// <summary>The image number (1 to 34) the answer is for.</summary>
        public int Index { get; set; }

        /// <summary>1 = positioning OK, 0 = NG.</summary>
        public bool Ok { get; set; }

        /// <summary>The dots' pixel positions P1 .. Pn: [n][u, v].</summary>
        public double[][] Points { get; set; } = new double[0][];
    }

    /// <summary>One validation image of SOP step 4.</summary>
    public sealed class FiveAxisValidationPoint
    {
        public string Name { get; set; }

        public double A { get; set; }

        public double C { get; set; }

        public double XPredicted { get; set; }

        public double YPredicted { get; set; }

        public double ZPredicted { get; set; }

        /// <summary>The centre distance error E, mm (step 4.3).</summary>
        public double ErrorMm { get; set; }
    }

    /// <summary>What a finished calibration found. Saved as Data\Calibration\FiveAxisResult_Station{n}.json.</summary>
    public sealed class FiveAxisResult
    {
        public int Station { get; set; }

        public DateTime FinishedUtc { get; set; }

        public double Z0 { get; set; }

        public double A0 { get; set; }

        public double C0 { get; set; }

        // Step 1.4
        public double CameraRms { get; set; }

        public double Fx { get; set; }

        public double Fy { get; set; }

        public double Cx { get; set; }

        public double Cy { get; set; }

        /// <summary>k1, k2, p1, p2, k3.</summary>
        public double[] Distortion { get; set; }

        // Step 2: M_hand_eye = [R | t], 3 x 4
        public double[][] HandEye { get; set; }

        // Step 3.2
        public double YC0 { get; set; }

        public double Rx { get; set; }

        public double Ry { get; set; }

        public double XA { get; set; }

        public double ZA { get; set; }

        public double Lx { get; set; }

        public double Lz { get; set; }

        // Step 4
        public List<FiveAxisValidationPoint> Validation { get; set; } = new List<FiveAxisValidationPoint>();

        public double ErrorMax { get; set; }

        public double ErrorAverage { get; set; }

        /// <summary>Things the operator should know about the numbers (a coplanar hand-eye fit, a large RMS).</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
