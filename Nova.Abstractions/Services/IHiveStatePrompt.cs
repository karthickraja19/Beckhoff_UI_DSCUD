using System;
using Nova.Abstractions.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The HIVE state as the indicator bar shows it, and the pop-ups that let a level
    /// 2/3 operator change it (Audio SW DFM v5.4, slides 47 and 49).
    /// </summary>
    /// <remarks>
    /// The dialogs are WPF windows, so they live in Nova.Hive; this contract is what the
    /// shell view model uses to open them. Without a PLC nothing implements it and the
    /// HIVE chip stays read-only.
    /// </remarks>
    public interface IHiveStatePrompt
    {
        /// <summary>The state to show on the chip.</summary>
        HiveState CurrentState { get; }

        /// <summary>Raised on the UI thread after the state changed, for any reason.</summary>
        event EventHandler<HiveState> StateChanged;

        /// <summary>
        /// Slide 49: shows the state options, then the planned downtime reasons when
        /// that was chosen, then asks for a badge. Nothing changes until the badge is read.
        /// </summary>
        void ShowStateChange();

        /// <summary>
        /// Slide 47: the GUI Stop button puts HIVE into downtime, and "every time when
        /// pressing the stop button on GUI will pop up to record operator ID".
        /// </summary>
        void RecordStopDowntime();
    }
}
