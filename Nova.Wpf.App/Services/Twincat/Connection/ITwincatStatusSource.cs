using System.Collections.Generic;
using Nova.Alarm;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// A TwinCAT device adapter whose status is built from symbols it registered with
    /// <see cref="PlcSubscriptions"/>, so a monitor can tell which devices a value the
    /// PLC pushed belongs to.
    /// </summary>
    public interface ITwincatStatusSource
    {
        string ComponentId { get; }

        /// <summary>The PLC symbols this device's status is read from.</summary>
        IReadOnlyList<string> WatchedSymbols { get; }
    }
}
