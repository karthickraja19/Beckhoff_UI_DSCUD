using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The machine's calibrations. The number is the PLC's E_Calibration, which is what
    /// the calibration manager is told to run.
    /// </summary>
    public enum CalibrationKind
    {
        NinePoint = 1,
        FiveAxis = 2,
        NeedleModule = 3,
        CameraNeedle = 4
    }

    /// <summary>9 point calibration: the taught start position of one station (Data\Calibration\NinePoint.json).</summary>
    public sealed class NinePointTeach
    {
        public int Station { get; set; }

        /// <summary>The PLC's bTaught: this station's position is real and may be used.</summary>
        public bool Taught { get; set; }

        public double X { get; set; }

        public double Y { get; set; }
    }

    /// <summary>Needle module calibration: the taught position of one station (Data\Calibration\NeedleModule.json).</summary>
    public sealed class NeedleModuleTeach
    {
        public int Station { get; set; }

        public bool Taught { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }
    }

    /// <summary>One row of the 5 axis translation table: "point1" .. "point9" and their X / Y.</summary>
    public sealed class FiveAxisTranslationPoint
    {
        public string Name { get; set; } = string.Empty;

        public double X { get; set; }

        public double Y { get; set; }
    }

    /// <summary>One row of the 5 axis rotation table: "point10" .. "point34" and their A / C / X / Y / Z.</summary>
    public sealed class FiveAxisRotationPoint
    {
        public string Name { get; set; } = string.Empty;

        public double A { get; set; }

        public double C { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }
    }

    /// <summary>5 axis calibration, translation table of one station (Data\Calibration\FiveAxisTranslation.json): 9 rows.</summary>
    public sealed class FiveAxisTranslation
    {
        public int Station { get; set; }

        public bool Taught { get; set; }

        public List<FiveAxisTranslationPoint> Points { get; set; } = new List<FiveAxisTranslationPoint>();
    }

    /// <summary>5 axis calibration, rotation table of one station (Data\Calibration\FiveAxisRotation.json): 25 rows.</summary>
    public sealed class FiveAxisRotation
    {
        public int Station { get; set; }

        public bool Taught { get; set; }

        public List<FiveAxisRotationPoint> Points { get; set; } = new List<FiveAxisRotationPoint>();
    }

    /// <summary>Both 5 axis tables of one station, as the sequence gets them.</summary>
    public sealed class FiveAxisTeach
    {
        public int Station { get; set; }

        /// <summary>point1 .. point9</summary>
        public List<FiveAxisTranslationPoint> Translation { get; set; } = new List<FiveAxisTranslationPoint>();

        /// <summary>point10 .. point34</summary>
        public List<FiveAxisRotationPoint> Rotation { get; set; } = new List<FiveAxisRotationPoint>();
    }

    /// <summary>
    /// What the 5 axis calibration needs that has no other source and is edited on the 5-Axis tab
    /// (Data\Calibration\FiveAxisSettings.json): the dot spacing of the calibration plate and the
    /// camera's image size. 0 = not set; the calibration will not start.
    /// </summary>
    public sealed class FiveAxisSetup
    {
        /// <summary>The spacing d between neighbouring dots of the 7 x 7 plate, mm.</summary>
        public double PlateSpacingMm { get; set; }

        /// <summary>The camera image size, pixels.</summary>
        public int ImageWidth { get; set; }

        public int ImageHeight { get; set; }
    }

    /// <summary>One named XY position: a glue point or a vision point.</summary>
    public sealed class TeachPoint
    {
        public string Name { get; set; } = string.Empty;

        public double X { get; set; }

        public double Y { get; set; }
    }

    /// <summary>Camera needle calibration: the glue and vision points of one station (Data\Calibration\CameraNeedle.json).</summary>
    public sealed class CameraNeedleTeach
    {
        public int Station { get; set; }

        public bool Taught { get; set; }

        /// <summary>Where the needle dispenses the dots: the PLC's aGlue[1..9].</summary>
        public List<TeachPoint> Glue { get; set; } = new List<TeachPoint>();

        /// <summary>Where the camera looks at the dots: the PLC's aVision[1..9].</summary>
        public List<TeachPoint> Vision { get; set; } = new List<TeachPoint>();
    }

    /// <summary>What the PLC's calibration manager reports.</summary>
    public sealed class CalibrationStatus
    {
        public static readonly CalibrationStatus NotLive = new CalibrationStatus();

        /// <summary>False while the PLC is not there, or before it reported the status.</summary>
        public bool IsLive { get; set; }

        /// <summary>Why <see cref="IsLive"/> is false; empty while live.</summary>
        public string NotLiveReason { get; set; } = string.Empty;

        /// <summary>A calibration is running.</summary>
        public bool IsBusy { get; set; }

        /// <summary>The last one finished without a fault (until Reset or the next start).</summary>
        public bool IsDone { get; set; }

        /// <summary>The last one failed, was aborted or was not allowed (until Reset or the next start).</summary>
        public bool IsError { get; set; }

        /// <summary>The calibration that is running or ran last (E_Calibration); 0 = none.</summary>
        public int Calibration { get; set; }

        /// <summary>Its station.</summary>
        public int Station { get; set; }

        public int Step { get; set; }

        /// <summary>The PLC's own text for where the calibration is, or why it stopped.</summary>
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// The Config page's Calibration section. The HMI keeps the taught positions of every
    /// calibration, one JSON file per calibration in Data\Calibration, and writes them into
    /// the PLC's teach tables; the PLC keeps them (persistent data) and runs every
    /// calibration sequence itself - except the 5 axis calibration, which the HMI runs
    /// (Master.Sequences). For the PLC's calibrations the HMI only selects
    /// the calibration and the station and presses Start / Abort / Reset.
    /// </summary>
    public interface ICalibrationService
    {
        /// <summary>The stations of each file, in station order. A missing file gives the stations with zeros.</summary>
        IReadOnlyList<NinePointTeach> LoadNinePoint();

        /// <summary>5 axis: stations 1 and 2 only, each with its translation table (9 rows) and rotation table (25 rows).</summary>
        IReadOnlyList<FiveAxisTranslation> LoadFiveAxisTranslation();

        IReadOnlyList<FiveAxisRotation> LoadFiveAxisRotation();

        IReadOnlyList<CameraNeedleTeach> LoadCameraNeedle();

        IReadOnlyList<NeedleModuleTeach> LoadNeedleModule();

        /// <summary>Saves the file. Throws when it cannot be written.</summary>
        void SaveNinePoint(IReadOnlyList<NinePointTeach> stations);

        void SaveFiveAxisTranslation(IReadOnlyList<FiveAxisTranslation> stations);

        void SaveFiveAxisRotation(IReadOnlyList<FiveAxisRotation> stations);

        void SaveCameraNeedle(IReadOnlyList<CameraNeedleTeach> stations);

        void SaveNeedleModule(IReadOnlyList<NeedleModuleTeach> stations);

        /// <summary>The 5 axis calibration's plate spacing and image size, from Data\Calibration\FiveAxisSettings.json (0 = not set).</summary>
        FiveAxisSetup LoadFiveAxisSetup();

        /// <summary>Saves them into the same file, leaving its other settings as they are. Throws when it cannot be written.</summary>
        void SaveFiveAxisSetup(FiveAxisSetup setup);

        /// <summary>False when the symbol map has no teach table for the calibration.</summary>
        bool CanWrite(CalibrationKind kind);

        /// <summary>
        /// Writes the calibration's saved file into its teach table in the PLC, every station,
        /// then has the PLC store its persistent data. The task faults, with the reason as its
        /// message, when the table is not mapped, does not have the size the file needs, or
        /// the PLC cannot be written or does not confirm the store.
        /// </summary>
        Task WriteToPlcAsync(CalibrationKind kind);

        /// <summary>
        /// False when the calibration cannot be run at all: the PLC runs it and the symbol map
        /// has no calibration manager. The 5 axis calibration is the HMI's own, so it needs none.
        /// </summary>
        bool CanRun(CalibrationKind kind);

        /// <summary>Why <see cref="CanRun"/> is false; empty otherwise.</summary>
        string CannotRunReason(CalibrationKind kind);

        /// <summary>
        /// What the manager last reported, or - while the HMI's own calibration is running, or
        /// until its result is cleared by Reset - what that one reports. Not live while not followed.
        /// </summary>
        CalibrationStatus Status { get; }

        /// <summary><see cref="Status"/> changed. Raised on any thread.</summary>
        event EventHandler StatusChanged;

        /// <summary>Follows the manager's status until the handle is disposed - while the section is on screen.</summary>
        IDisposable FollowStatus();

        /// <summary>Selects the calibration and the station and pulses Start. Whether it then runs is the PLC's decision, reported in <see cref="Status"/>.</summary>
        Task StartAsync(CalibrationKind kind, int station);

        /// <summary>
        /// Stops a calibration the HMI runs itself, whichever tab asks; for a calibration the
        /// PLC runs, pulses the manager's Abort.
        /// </summary>
        Task AbortAsync(CalibrationKind kind);

        /// <summary>Clears the Done / Error / Stopped result and message of the calibration.</summary>
        Task ResetAsync(CalibrationKind kind);
    }
}
