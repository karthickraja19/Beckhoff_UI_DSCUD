using Master.Configuration.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using Teal.Library.Log;

namespace Master.Configuration
{
    public static class ConfigGlobalClass
    {
        public static DIDO DIDO = new DIDO();


        public static int InputPortCfg = 0;
        public static int OutputPortCfg = 0;

        public static List<IOParam> lstInputConfig = new List<IOParam>();
        public static List<IOParam> lstOutputConfig = new List<IOParam>();
        public static List<IOParam> lstAnalogIntputConfig = new List<IOParam>();

        public static List<AxisParam> g_lstAxisConfig = new List<AxisParam>();

        public static List<RobotInputs> lst_RobotInput = new List<RobotInputs>();
        public static List<RobotOutputs> lst_RobotOutput = new List<RobotOutputs>();

        //**devchange: Use g_DOChanNum and g_DIChanNum to calculate OutputPortCfg and InputPortCfg:
        public static int g_DOChanNum = 0;
        public static int g_DIChanNum = 0;

        public static string[] Analog_Input_Names;

        public static List<VacuumConfiguration> VacuumConfigurations = new List<VacuumConfiguration>();
        public static List<StepperConfiguration> StepperConfigurations = new List<StepperConfiguration>();
        public static List<ServoConfiguration> ServoConfigurations = new List<ServoConfiguration>();
        public static List<CylinderConfiguration> CylinderConfigurations = new List<CylinderConfiguration>();
        public static List<SocketClientConfiguration> CCDConfigurations = new List<SocketClientConfiguration>();
        public static List<SocketClientConfiguration> ScannerConfigurations = new List<SocketClientConfiguration>();
        public static List<RobotConfigurations> RobotConfigurations = new List<RobotConfigurations>();
        public static List<SocketClientConfiguration> PdcaConfiguration = new List<SocketClientConfiguration>();
        public static CardManager cardManager = new CardManager();

    }
}
