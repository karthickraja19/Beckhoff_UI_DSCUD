using System.Runtime.InteropServices;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A CCD's status (typeVisionFB.status), the struct the library gives as the CCD's HMI
    /// display: what the Home page's connection list and the Camera tab show. The PLC declares it as
    /// <code>
    /// TYPE typeVisionFBstatus :
    /// STRUCT
    ///     busy : BOOL;
    ///     connected : BOOL;
    ///     triggerSuccess : BOOL;
    ///     triggerFail : BOOL;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// 4 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct typeVisionFBstatus : INetworkHmiDisplay
    {
        [MarshalAs(UnmanagedType.I1)] public bool busy;
        [MarshalAs(UnmanagedType.I1)] public bool connected;
        [MarshalAs(UnmanagedType.I1)] public bool triggerSuccess;
        [MarshalAs(UnmanagedType.I1)] public bool triggerFail;

        public bool Connected => connected;
    }
}
