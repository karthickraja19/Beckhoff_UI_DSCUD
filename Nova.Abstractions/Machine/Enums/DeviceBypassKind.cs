using System.ComponentModel;

namespace Nova.Abstractions.Machine.Enums
{
    public enum DeviceBypassKind
    {
        [Description("Safety Door Bypass")] SafetyDoor,
        [Description("Scanner Bypass")] Scanner,
        [Description("Buzzer Bypass")] Buzzer,
        [Description("SFC_PDCA Bypass")] SfcPdca,
        [Description("Vision Bypass")] Vision,
        [Description("Robo Bypass")] Robot,
        //[Description("Test Bypass")] Test  //This will automatically add in UI, wire it properly in MachineContext ( GetBypass )
    }
}
