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

    /// <summary>One row of the 5 axis table, e.g. "A-5": A 5, C 0 and the taught X / Y / Z.</summary>
    public sealed class FiveAxisTeachPoint
    {
        public string Name { get; set; } = string.Empty;

        public double A { get; set; }

        public double C { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }
    }

    /// <summary>5 axis calibration: the taught table of one station (Data\Calibration\FiveAxis.json).</summary>
    public sealed class FiveAxisTeach
    {
        public int Station { get; set; }

        public bool Taught { get; set; }

        /// <summary>Row n is the PLC's aPoint[n]. Rows the PLC holds beyond these are written as zeros.</summary>
        public List<FiveAxisTeachPoint> Points { get; set; } = new List<FiveAxisTeachPoint>();
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
    /// calibration sequence itself. The HMI only selects the calibration and the station
    /// and presses Start / Abort / Reset.
    /// </summary>
    public interface ICalibrationService
    {
        /// <summary>The stations of each file, in station order. A missing file gives the stations with zeros.</summary>
        IReadOnlyList<NinePointTeach> LoadNinePoint();

        IReadOnlyList<FiveAxisTeach> LoadFiveAxis();

        IReadOnlyList<CameraNeedleTeach> LoadCameraNeedle();

        IReadOnlyList<NeedleModuleTeach> LoadNeedleModule();

        /// <summary>Saves the file. Throws when it cannot be written.</summary>
        void SaveNinePoint(IReadOnlyList<NinePointTeach> stations);

        void SaveFiveAxis(IReadOnlyList<FiveAxisTeach> stations);

        void SaveCameraNeedle(IReadOnlyList<CameraNeedleTeach> stations);

        void SaveNeedleModule(IReadOnlyList<NeedleModuleTeach> stations);

        /// <summary>False when the symbol map has no teach table for the calibration.</summary>
        bool CanWrite(CalibrationKind kind);

        /// <summary>
        /// Writes the calibration's saved file into its teach table in the PLC, every station,
        /// then has the PLC store its persistent data. The task faults, with the reason as its
        /// message, when the table is not mapped, does not have the size the file needs, or
        /// the PLC cannot be written or does not confirm the store.
        /// </summary>
        Task WriteToPlcAsync(CalibrationKind kind);

        /// <summary>False when the symbol map has no calibration manager.</summary>
        bool CanRun { get; }

        /// <summary>Why <see cref="CanRun"/> is false; empty otherwise.</summary>
        string CannotRunReason { get; }

        /// <summary>What the manager last reported; not live while not followed.</summary>
        CalibrationStatus Status { get; }

        /// <summary><see cref="Status"/> changed. Raised on any thread.</summary>
        event EventHandler StatusChanged;

        /// <summary>Follows the manager's status until the handle is disposed - while the section is on screen.</summary>
        IDisposable FollowStatus();

        /// <summary>Selects the calibration and the station and pulses Start. Whether it then runs is the PLC's decision, reported in <see cref="Status"/>.</summary>
        Task StartAsync(CalibrationKind kind, int station);

        Task AbortAsync();

        /// <summary>Clears the manager's Done / Error and its message.</summary>
        Task ResetAsync();
    }
}
