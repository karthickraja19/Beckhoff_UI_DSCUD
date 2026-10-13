using System.Runtime.InteropServices;

namespace Nova.Wpf.App.Services.Twincat.HmiDisplay
{
    /// <summary>
    /// A scanner's status (typeScnFB.status), the struct the library gives as the scanner's HMI
    /// display: what the Home page's connection list and the Scanner tab show. The PLC declares it as
    /// <code>
    /// TYPE typeScnFBstatus :
    /// STRUCT
    ///     busy : BOOL;
    ///     connected : BOOL;
    ///     triggerSuccess : BOOL;
    ///     triggerFail : BOOL;
    ///     triggerChainFail : BOOL;
    /// END_STRUCT
    /// END_TYPE
    /// </code>
    /// 5 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct typeScnFBstatus : INetworkHmiDisplay
    {
        [MarshalAs(UnmanagedType.I1)] public bool busy;
        [MarshalAs(UnmanagedType.I1)] public bool connected;
        [MarshalAs(UnmanagedType.I1)] public bool triggerSuccess;
        [MarshalAs(UnmanagedType.I1)] public bool triggerFail;
        [MarshalAs(UnmanagedType.I1)] public bool triggerChainFail;

        public bool Connected => connected;
    }
}
