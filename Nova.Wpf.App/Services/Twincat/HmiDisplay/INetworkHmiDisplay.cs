namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A TCP/IP device's HMI display struct - what the PLC publishes about a CCD or scanner.
    /// Every struct App.config can name for them (VisionHmiDisplayType, ScannerHmiDisplayType)
    /// implements this.
    /// </summary>
    public interface INetworkHmiDisplay
    {
        /// <summary>The PLC has the device's socket open.</summary>
        bool Connected { get; }
    }
}
