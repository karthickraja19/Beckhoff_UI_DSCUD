using TwinCAT.Ads;

namespace Nova.Alarm
{
    /// <summary>
    /// The one ADS connection to the PLC that the whole HMI shares - the Beckhoff library's
    /// TwincatCard, wrapped by Nova.Wpf.App's TwincatPlcConnection. Declared here because
    /// Nova.Alarm is the lowest project that talks to the PLC: the alarm and HIVE readers
    /// cannot reference the app, which references them.
    /// </summary>
    /// <remarks>
    /// The connection belongs to whoever created it: users subscribe and read through it,
    /// but never connect it, disconnect it or dispose it.
    /// </remarks>
    public interface IPlcConnection
    {
        /// <summary>The shared ADS client. Its notification events are shared too, so a handler must ignore notifications that are not its own.</summary>
        AdsClient Client { get; }

        /// <summary>Held around every call made through <see cref="Client"/>, so calls from different threads do not interleave. Never take it inside a notification handler.</summary>
        object SyncRoot { get; }

        /// <summary>True when the PLC answers and is in Run. Reconnects first if the connection dropped.</summary>
        bool IsConnected { get; }

        /// <summary>The PLC's AMS Net ID and port, for messages.</summary>
        string Address { get; }
    }
}
