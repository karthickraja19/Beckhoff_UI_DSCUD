using System;

namespace Nova.Abstractions.Services
{
    /// <summary>How a link to an outside service (HIVE, MES, PDCA) stands.</summary>
    public enum LinkState
    {
        /// <summary>Not checked yet.</summary>
        Unknown,
        Connected,
        Disconnected,
        /// <summary>Switched off in the configuration.</summary>
        Disabled
    }

    /// <summary>
    /// The state of one outside service's link, for the station card's link row
    /// (Audio SW DFM v5.4 slide 43: green when connected, red when not).
    /// </summary>
    public interface IServiceLinkStatus
    {
        /// <summary>"HIVE", "MES" or "PDCA".</summary>
        string Name { get; }

        LinkState State { get; }

        /// <summary>What lies behind the state, for a tooltip, e.g. the gateway address.</summary>
        string Detail { get; }

        /// <summary>Raised when <see cref="State"/> or <see cref="Detail"/> changes. May be on a background thread.</summary>
        event EventHandler Changed;
    }
}
