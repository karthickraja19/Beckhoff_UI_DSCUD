using System;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// What the PLC's Static / Dynamic verification does: the number is the PLC's
    /// <c>GVL_StaticDynamic.nMode</c>.
    /// </summary>
    public enum StaticDynamicMode
    {
        /// <summary>None: the main sequence skips the Static / Dynamic step and the machine dispenses as normal.</summary>
        Off = 0,

        /// <summary>Static_Seq: the camera goes to its recipe position and the vision command is sent <c>Iteration</c> times.</summary>
        Static = 1,

        /// <summary>Dynamic_Seq: as Static, but the camera X moves away and back between two vision commands.</summary>
        Dynamic = 2
    }

    /// <summary>What the PLC's Static / Dynamic sequences report (GVL_StaticDynamic, PLC to HMI).</summary>
    public sealed class StaticDynamicStatus
    {
        public static readonly StaticDynamicStatus NotLive = new StaticDynamicStatus();

        /// <summary>False while the PLC is not there, or before it reported the status.</summary>
        public bool IsLive { get; set; }

        /// <summary>Why <see cref="IsLive"/> is false; empty while live.</summary>
        public string NotLiveReason { get; set; } = string.Empty;

        /// <summary>The PLC's nMode: what a Start (or the main sequence) will run.</summary>
        public StaticDynamicMode Mode { get; set; }

        /// <summary>The PLC's nCcd: the camera an HMI Start runs on (1-based).</summary>
        public int Ccd { get; set; }

        /// <summary>The PLC's nIteration: vision commands per run.</summary>
        public int Iteration { get; set; }

        /// <summary>A run is active.</summary>
        public bool IsBusy { get; set; }

        /// <summary>The last run finished OK (until the next start).</summary>
        public bool IsDone { get; set; }

        /// <summary>The last run was refused, stopped or its vision command was not received (until the next start).</summary>
        public bool IsError { get; set; }

        public int Step { get; set; }

        /// <summary>Vision commands answered so far in this run.</summary>
        public int Count { get; set; }

        /// <summary>The PLC's own CURRENT STATUS text.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>The last X / Y / Z received from the vision, mm.</summary>
        public double VisionX { get; set; }

        public double VisionY { get; set; }

        public double VisionZ { get; set; }
    }

    /// <summary>
    /// The Config page's Calibration &gt; Static &amp; Dynamic tab. The PLC owns the whole run
    /// (recipe position, axis moves, vision commands, custom log); the HMI only picks the
    /// mode, the camera and the iteration count, presses Start / End / Stop, and shows what
    /// the PLC reports. A run needs the machine in AUTO - the PLC says so when it is not.
    /// </summary>
    public interface IStaticDynamicService
    {
        /// <summary>False when the symbol map lacks what a run needs.</summary>
        bool IsAvailable { get; }

        /// <summary>Why <see cref="IsAvailable"/> is false; empty otherwise.</summary>
        string UnavailableReason { get; }

        /// <summary>How many cameras the PLC's sequences serve (CCD 1 .. CCD n).</summary>
        int CameraCount { get; }

        /// <summary>What the PLC last reported; not live while not followed.</summary>
        StaticDynamicStatus Status { get; }

        /// <summary><see cref="Status"/> changed. Raised on any thread.</summary>
        event EventHandler StatusChanged;

        /// <summary>Follows the PLC's status until the handle is disposed - while the tab is on screen.</summary>
        IDisposable FollowStatus();

        /// <summary>
        /// Writes the mode, the camera and the iteration count. The main sequence reads the mode
        /// and the iteration for every unit, so <see cref="StaticDynamicMode.Off"/> is what gives
        /// the machine back to production.
        /// </summary>
        Task ApplyAsync(StaticDynamicMode mode, int ccd, int iteration);

        /// <summary>Writes the settings, then pulses Start. Whether it then runs is the PLC's decision, reported in <see cref="Status"/>.</summary>
        Task StartAsync(StaticDynamicMode mode, int ccd, int iteration);

        /// <summary>Finish after the vision command now running.</summary>
        Task EndAsync();

        /// <summary>Abort now.</summary>
        Task StopAsync();
    }
}
