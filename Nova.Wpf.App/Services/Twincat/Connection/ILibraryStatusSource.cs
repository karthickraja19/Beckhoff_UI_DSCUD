using System;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// A TwinCAT device adapter that has the library read its current status when a screen
    /// starts showing it, before the PLC pushes any change - so a monitor publishes the
    /// device then as well as on each push.
    /// </summary>
    public interface ILibraryStatusSource
    {
        /// <summary>The library has read the device's current status. Raised on a worker thread.</summary>
        event EventHandler LibraryStatusRead;
    }
}
