using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>Where the PLC's calibration sequence is (its E_CalibState).</summary>
    public enum CalibrationState
    {
        Idle = 0,
        Running = 1,
        Paused = 2,

        /// <summary>The sequence waits for the operator to accept or reject a check (<see cref="CalibrationStatus.Prompt"/>).</summary>
        WaitingOperator = 3,

        Done = 4,
        Failed = 5,

        /// <summary>Stopped by the Stop button before it finished.</summary>
        Stopped = 6
    }

    /// <summary>The PLC's calibration status as it stands now.</summary>
    public sealed class CalibrationStatus
    {
        public static readonly CalibrationStatus NotLive = new CalibrationStatus();

        /// <summary>False while the PLC is not there, or before it reported the status.</summary>
        public bool IsLive { get; set; }

        /// <summary>Why <see cref="IsLive"/> is false; empty while live.</summary>
        public string NotLiveReason { get; set; } = string.Empty;

        public CalibrationState State { get; set; }

        /// <summary>The station the PLC is calibrating, or last calibrated (1 or 2).</summary>
        public int Station { get; set; }

        /// <summary>The routine the PLC is running, or last ran (its E_CalibRoutine number).</summary>
        public int Routine { get; set; }

        public int Step { get; set; }

        /// <summary>The sequence's own text for its current step.</summary>
        public string StepText { get; set; } = string.Empty;

        /// <summary>What the operator must check while <see cref="CalibrationState.WaitingOperator"/>.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>The PLC's verdict once Done: TRUE when every check passed.</summary>
        public bool ResultOk { get; set; }

        /// <summary>The measured values, laid out as the routine says (row by row).</summary>
        public IReadOnlyList<double> Results { get; set; } = new double[0];

        /// <summary>A calibration is under way: running, paused or waiting for the operator.</summary>
        public bool IsBusy => IsLive
            && (State == CalibrationState.Running || State == CalibrationState.Paused || State == CalibrationState.WaitingOperator);
    }

    /// <summary>A routine's parameters as the HMI last saved them for one station.</summary>
    public sealed class CalibrationParameterSet
    {
        public IDictionary<string, double> Values { get; set; } = new Dictionary<string, double>();

        public string SavedBy { get; set; } = string.Empty;

        public DateTime SavedAt { get; set; }
    }

    /// <summary>
    /// The Setting page's Calibration tab. The PLC owns every calibration sequence; the HMI
    /// picks the station and the routine, writes the routine's parameters (which it keeps,
    /// per station), and presses Start / Pause / Stop. The PLC reports where the sequence is
    /// and what it measured.
    /// </summary>
    public interface ICalibrationService
    {
        /// <summary>False when the PLC calibration symbols are not configured.</summary>
        bool IsAvailable { get; }

        /// <summary>Why <see cref="IsAvailable"/> is false; empty otherwise.</summary>
        string UnavailableReason { get; }

        /// <summary>How many parameters the PLC takes (its aParam array).</summary>
        int MaxParameters { get; }

        /// <summary>How many result values the PLC reports (its aResult array).</summary>
        int MaxResults { get; }

        /// <summary>The status the PLC last pushed; <see cref="CalibrationStatus.IsLive"/> is false while not followed or not live.</summary>
        CalibrationStatus Status { get; }

        /// <summary><see cref="Status"/> changed. Raised on any thread.</summary>
        event EventHandler StatusChanged;

        /// <summary>Follows the PLC's calibration status until the handle is disposed - while the tab is on screen.</summary>
        IDisposable FollowStatus();

        /// <summary>The parameters last saved for the routine on the station, or null when none were.</summary>
        CalibrationParameterSet LoadParameters(int station, string routineKey);

        /// <summary>Saves the routine's parameters for the station. Throws when they cannot be written.</summary>
        void SaveParameters(int station, string routineKey, IDictionary<string, double> values, string savedBy);

        /// <summary>
        /// Writes the station, the routine and its parameters (in the routine's order) to the
        /// PLC, then presses Start. Completes when the PLC has been written; faults when it
        /// could not be.
        /// </summary>
        Task StartAsync(int station, int routine, IReadOnlyList<double> parameters);

        /// <summary>Presses Start again: the PLC resumes a paused calibration.</summary>
        Task ResumeAsync();

        Task PauseAsync();

        Task StopAsync();

        /// <summary>Answers the PLC's operator check: accepted, or rejected.</summary>
        Task AnswerPromptAsync(bool accepted);
    }
}
