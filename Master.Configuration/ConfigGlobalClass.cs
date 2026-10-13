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

        //**devchange: Use g_DOChanNum and g_DIChanNum to calculate OutputPortCfg and InputPortCfg:
        public static int g_DOChanNum = 0;
        public static int g_DIChanNum = 0;

        public static string[] Analog_Input_Names;

        public static List<VacuumConfiguration> VacuumConfigurations = new List<VacuumConfiguration>();
        public static List<StepperConfiguration> StepperConfigurations = new List<StepperConfiguration>();
        public static List<ServoConfiguration> ServoConfigurations = new List<ServoConfiguration>();
        public static List<CylinderConfiguration> CylinderConfigurations = new List<CylinderConfiguration>();
        public static List<VisionConfiguration> CCDConfigurations = new List<VisionConfiguration>();
        public static List<NetworkDeviceConfiguration> ScannerConfigurations = new List<NetworkDeviceConfiguration>();
        public static List<RobotConfiguration> RobotConfigurations = new List<RobotConfiguration>();
        public static List<SocketClientConfiguration> PdcaConfiguration = new List<SocketClientConfiguration>();
        public static List<SocketClientConfiguration> SfcConfigurations = new List<SocketClientConfiguration>();

        /// <summary>The Beckhoff card from CardsConfiguration.json; null until loaded, or when the file is missing or wrong.</summary>
        public static CardConfiguration CardConfiguration;

        private static CardManager _cardManager;

        /// <summary>
        /// The card, created on first use from <see cref="CardConfiguration"/>. Not created when
        /// this class is first touched: that used to read the card file straight away, from the
        /// WinForms startup folder, and show a message box if it was missing.
        /// </summary>
        public static CardManager cardManager
        {
            get { return _cardManager ?? (_cardManager = new CardManager()); }
        }
    }
}
