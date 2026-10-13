using System;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The machine's Homing button. The PLC owns the homing sequence: the HMI only raises the
    /// PLC's home command bit, and the PLC raises its own "homing done" bit when every
    /// component is at its home position.
    /// </summary>
    public interface IHomingService
    {
        /// <summary>False when no command symbol is configured, so the button has nothing to write.</summary>
        bool IsAvailable { get; }

        /// <summary>Why <see cref="IsAvailable"/> is false, for the button's tooltip; empty otherwise.</summary>
        string UnavailableReason { get; }

        /// <summary>True when the PLC's homing-done bit is configured, so <see cref="IsHomed"/> means something.</summary>
        bool ReportsHomed { get; }

        /// <summary>
        /// The PLC's homing-done bit as it stands now: TRUE once every component is homed. False
        /// when the bit is FALSE, unknown, or the PLC is not live. Only meaningful while
        /// <see cref="ReportsHomed"/>.
        /// </summary>
        bool IsHomed { get; }

        /// <summary>True from the command until the PLC reports homing done.</summary>
        bool IsHoming { get; }

        /// <summary><see cref="IsHoming"/> or <see cref="IsHomed"/> changed. Raised on any thread.</summary>
        event EventHandler StateChanged;

        /// <summary>The PLC reported homing done. Raised on any thread.</summary>
        event EventHandler HomingCompleted;

        /// <summary>Begins following the PLC's homing-done bit.</summary>
        void Start();

        /// <summary>Sets the PLC's home command bit TRUE. Throws when the PLC cannot be written.</summary>
        void StartHoming();
    }
}
