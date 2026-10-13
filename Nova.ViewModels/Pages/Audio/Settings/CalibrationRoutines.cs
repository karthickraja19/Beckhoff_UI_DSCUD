using System.Collections.Generic;
using System.Linq;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One parameter of a calibration routine, as the operator enters it and the PLC receives it.</summary>
    public sealed class CalibrationParameter
    {
        public CalibrationParameter(string key, string label, string unit, double? defaultValue, double min, double max, int decimals = 3)
        {
            Key = key;
            Label = label;
            Unit = unit ?? string.Empty;
            Default = defaultValue;
            Min = min;
            Max = max;
            Decimals = decimals;
        }

        /// <summary>The name it is saved under; never changes, so saved values survive a reorder.</summary>
        public string Key { get; }

        public string Label { get; }

        public string Unit { get; }

        /// <summary>The value from the SOP / DFM, or null where they give none and it must be entered.</summary>
        public double? Default { get; }

        public double Min { get; }

        public double Max { get; }

        public int Decimals { get; }
    }

    /// <summary>A column of a routine's result table.</summary>
    public sealed class CalibrationResultColumn
    {
        public CalibrationResultColumn(string header, string unit = "", bool isPass = false)
        {
            Header = header;
            Unit = unit ?? string.Empty;
            IsPass = isPass;
        }

        public string Header { get; }

        public string Unit { get; }

        /// <summary>A pass/fail flag: non-zero shows OK, zero NG.</summary>
        public bool IsPass { get; }
    }

    /// <summary>
    /// A calibration routine the PLC runs. <see cref="Number"/> is its E_CalibRoutine value;
    /// <see cref="Parameters"/> go to the PLC's aParam[1..] in this order; the PLC reports its
    /// results in aResult[1..] row by row: row r, column c is aResult[(r - 1) * columns + c].
    /// </summary>
    public sealed class CalibrationRoutine
    {
        public CalibrationRoutine(
            int number, string key, string name, string reference,
            IEnumerable<string> procedure,
            IEnumerable<CalibrationParameter> parameters,
            IEnumerable<CalibrationResultColumn> resultColumns,
            IEnumerable<string> resultRows)
        {
            Number = number;
            Key = key;
            Name = name;
            Reference = reference;
            Procedure = procedure.ToList();
            Parameters = parameters.ToList();
            ResultColumns = resultColumns.ToList();
            ResultRows = resultRows.ToList();
        }

        public int Number { get; }

        /// <summary>The name its parameters are saved under.</summary>
        public string Key { get; }

        public string Name { get; }

        /// <summary>Where the procedure comes from (DFM pages, SOP sheet).</summary>
        public string Reference { get; }

        /// <summary>What the operator does and what the PLC does, step by step.</summary>
        public IReadOnlyList<string> Procedure { get; }

        public IReadOnlyList<CalibrationParameter> Parameters { get; }

        public IReadOnlyList<CalibrationResultColumn> ResultColumns { get; }

        public IReadOnlyList<string> ResultRows { get; }

        public int ResultCount => ResultColumns.Count * ResultRows.Count;

        public override string ToString() => Name;
    }

    /// <summary>
    /// The TE010 calibration routines - SOP_TEAL_H59_TE010 sheet TE010_CALIBRATION and DFM
    /// v2.3 pages 44-48 and 57-76. Each is run per station. The PLC's E_CalibRoutine must use
    /// the same numbers, its aParam the same order, and its aResult the same layout.
    /// </summary>
    /// <remarks>
    /// The barcode scanner is calibrated in Keyence's own tool, and the mechanical settings
    /// (conveyor width, gripper, lifter, stopper) with gauge blocks, so neither is here. The
    /// 5-axis kinematic calibration is added once its procedure is available.
    /// </remarks>
    public static class CalibrationRoutines
    {
        public static readonly CalibrationRoutine NinePoint = new CalibrationRoutine(
            1, "NinePoint", "9-point calibration (CCD1 / CCD2)", "DFM p.67-73, SOP step 1-5",
            new[]
            {
                "Place the camera checker sheet under the station camera; the PLC checks lighting and focus.",
                "Replace it with the glue calibration block; all nine fiducials clean and visible.",
                "For each of the nine points the gantry moves, dispenses one dot beside the fiducial and triggers the camera.",
                "Each dot is checked for area, circularity and distance to its fiducial; only valid dots are used.",
                "The nine gantry positions are paired with the camera's glue centres and the camera-to-gantry transform is stored.",
            },
            new[]
            {
                new CalibrationParameter("GlueAreaMin", "Glue area min", "mm²", null, 0, 10),
                new CalibrationParameter("GlueAreaMax", "Glue area max", "mm²", null, 0, 10),
                new CalibrationParameter("CircularityMin", "Circularity min", "0-1", null, 0, 1),
                new CalibrationParameter("FiducialDistanceMax", "Glue to fiducial max", "mm", null, 0, 5),
                new CalibrationParameter("Exposure", "Camera exposure", "µs", null, 1, 1000000, 0),
                new CalibrationParameter("Gain", "Camera gain", "", null, 0, 100, 1),
                new CalibrationParameter("Threshold", "Image threshold", "0-255", null, 0, 255, 0),
            },
            new[]
            {
                new CalibrationResultColumn("Fiducial X", "mm"),
                new CalibrationResultColumn("Fiducial Y", "mm"),
                new CalibrationResultColumn("Glue X", "mm"),
                new CalibrationResultColumn("Glue Y", "mm"),
                new CalibrationResultColumn("Distance", "mm"),
                new CalibrationResultColumn("Area", "mm²"),
                new CalibrationResultColumn("Circularity"),
                new CalibrationResultColumn("Result", isPass: true),
            },
            Enumerable.Range(1, 9).Select(i => "P" + i));

        public static readonly CalibrationRoutine NeedleOffset = new CalibrationRoutine(
            2, "NeedleOffset", "Needle offset (CCD1 / CCD2)", "DFM p.74-76, SOP step 6",
            new[]
            {
                "Place the needle calibration plate with the reference fiducial under the station camera.",
                "The camera finds the fiducial centre and the gantry dispenses a dot on it.",
                "The camera finds the glue centre; glue centre minus fiducial centre is the needle offset.",
                "Within the limits, the offset is stored and applied to every later dispense.",
            },
            new[]
            {
                new CalibrationParameter("OffsetXMax", "Offset X max", "± mm", null, 0, 5),
                new CalibrationParameter("OffsetYMax", "Offset Y max", "± mm", null, 0, 5),
            },
            new[]
            {
                new CalibrationResultColumn("X", "mm"),
                new CalibrationResultColumn("Y", "mm"),
            },
            new[] { "Fiducial centre", "Glue centre", "Offset" });

        public static readonly CalibrationRoutine NeedleTemplate = new CalibrationRoutine(
            3, "NeedleTemplate", "Needle template teach (CCD3)", "DFM p.46, SOP CCD3",
            new[]
            {
                "Fit a new, straight needle.",
                "The dispensing X and Z move to the needle calibration module's teach position.",
                "CCD3 finds the needle's left, right and bottom edges, its tip centre and angle T.",
                "X, Y, Z and T are stored as the template every later needle is compared with.",
            },
            new CalibrationParameter[0],
            new[]
            {
                new CalibrationResultColumn("X", "mm"),
                new CalibrationResultColumn("Y", "mm"),
                new CalibrationResultColumn("Z", "mm"),
                new CalibrationResultColumn("T", "°"),
            },
            new[] { "Template" });

        public static readonly CalibrationRoutine NeedleDeviation = new CalibrationRoutine(
            4, "NeedleDeviation", "Needle deviation check (CCD3)", "DFM p.45-48, SOP CCD3",
            new[]
            {
                "After a needle change, the needle moves to the needle calibration module.",
                "CCD3 measures the needle's X, Y, Z and T; deviation = measured - template.",
                "Within the limits the deviation is stored as the needle offset; otherwise adjust or change the needle.",
            },
            new[]
            {
                new CalibrationParameter("DeviationXMax", "Deviation X max", "± mm", 0.5, 0, 5),
                new CalibrationParameter("DeviationYMax", "Deviation Y max", "± mm", 0.5, 0, 5),
                new CalibrationParameter("DeviationZMax", "Deviation Z max", "± mm", 0.5, 0, 5),
                new CalibrationParameter("DeviationTMax", "Deviation T max", "± °", 5, 0, 45, 2),
            },
            new[]
            {
                new CalibrationResultColumn("X", "mm"),
                new CalibrationResultColumn("Y", "mm"),
                new CalibrationResultColumn("Z", "mm"),
                new CalibrationResultColumn("T", "°"),
            },
            new[] { "Template", "Measured", "Deviation" });

        public static readonly CalibrationRoutine BasePlateFlatness = new CalibrationRoutine(
            5, "BasePlateFlatness", "Base plate flatness (laser)", "DFM p.57, SOP laser flatness",
            new[]
            {
                "Place the calibration plate on the fixture; the presence sensor detects it.",
                "The laser on the vertical Z measures point 1, then 2 (Y), 4 (X), 3 (Y) and back to 1 (X).",
                "Max - min of the heights must be within the limit; otherwise offset with A / R and repeat, or adjust mechanically.",
            },
            new[]
            {
                new CalibrationParameter("FlatnessMax", "Flatness max (max - min)", "mm", 0.1, 0, 5),
                new CalibrationParameter("PitchX", "Point pitch X", "mm", 100, 1, 500, 1),
                new CalibrationParameter("PitchY", "Point pitch Y", "mm", 65, 1, 500, 1),
            },
            new[] { new CalibrationResultColumn("Height", "mm") },
            new[] { "Point 1", "Point 2", "Point 3", "Point 4", "Max - min" });

        public static readonly CalibrationRoutine BasePlateAngleZero = new CalibrationRoutine(
            6, "BasePlateAngleZero", "Base plate angle zero (laser)", "DFM p.58, SOP angle zero",
            new[]
            {
                "Place the calibration plate on the fixture; the presence sensor detects it.",
                "The laser, mounted horizontal and parallel to the fixture, measures point 1, then point 2 along Y.",
                "Max - min of the two heights must be within the limit; otherwise adjust mechanically.",
            },
            new[]
            {
                new CalibrationParameter("FlatnessMax", "Height difference max", "mm", 0.1, 0, 5),
                new CalibrationParameter("PitchY", "Point pitch Y", "mm", 100, 1, 500, 1),
            },
            new[] { new CalibrationResultColumn("Height", "mm") },
            new[] { "Point 1", "Point 2", "Max - min" });

        public static readonly CalibrationRoutine NeedleHeight = new CalibrationRoutine(
            7, "NeedleHeight", "Needle height (feeler gauge)", "DFM p.64, SOP needle height",
            new[]
            {
                "Place the calibration plate with the angled block on the fixture; the presence sensor detects it.",
                "A and R turn to the block's angles and the dispensing Z stops above the block at the gauge height.",
                "Check the gap between needle and block with the feeler gauge, then accept or reject on screen.",
                "Accepted: A and R at these angles become the zero reference.",
            },
            new[]
            {
                new CalibrationParameter("AAngle", "A axis angle", "°", 27.7, -70, 70, 2),
                new CalibrationParameter("RAngle", "R axis angle", "°", 115.6, 0, 360, 2),
                new CalibrationParameter("GaugeThickness", "Feeler gauge", "mm", null, 0.05, 1, 2),
            },
            new[] { new CalibrationResultColumn("Value") },
            new[] { "A axis (°)", "R axis (°)", "Dispensing Z (mm)" });

        public static readonly CalibrationRoutine NeedleZeroTouch = new CalibrationRoutine(
            8, "NeedleZeroTouch", "Needle Z zero (touch sensor)", "DFM p.65, SOP touch sensor",
            new[]
            {
                "Place the five-axis calibration plate on the fixture; the presence sensor detects it.",
                "The dispensing Z moves down onto the touch sensor (tool setter) until it detects the needle.",
                "That position becomes Z = 0 for the dispensing Z.",
                "The vision Z moves its laser to the same 0.",
            },
            new[]
            {
                new CalibrationParameter("SearchSpeed", "Search speed", "mm/s", null, 0.1, 50, 1),
                new CalibrationParameter("SearchTravelMax", "Search travel max", "mm", null, 0.1, 110, 1),
            },
            new[] { new CalibrationResultColumn("Position", "mm") },
            new[] { "Dispensing Z at touch", "Vision Z at laser zero" });

        public static IReadOnlyList<CalibrationRoutine> All { get; } = new[]
        {
            NinePoint, NeedleOffset, NeedleTemplate, NeedleDeviation,
            BasePlateFlatness, BasePlateAngleZero, NeedleHeight, NeedleZeroTouch,
        };
    }
}
