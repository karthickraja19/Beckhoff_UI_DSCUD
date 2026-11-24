using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Manual
{
    /// <summary>
    /// Manual &gt; Dispenser: the two-component glue dispensers (SV series, RS-232), one per
    /// station. Recipe, ratio / time / volume and speed are written, then Save sends them
    /// to the dispenser; Start / Stop dispense; Read status runs the cyclic status read
    /// that fills the live values.
    /// </summary>
    public sealed class DispenserControlViewModel : PlcDevicePanelViewModel
    {
        public override string SelectorCaption => "Select dispenser";

        public DispenserControlViewModel(IPlcDeviceService service, IDialogService dialog = null)
            : base(service, dialog)
        {
            ConnectedKey = "Connected";
            AutoModeKey = "AutoMode";
            AlarmActiveKey = "AlarmActive";
            AlarmCodeKey = "AlarmCode";
            AlarmTextKey = "AlarmText";

            AddIndicator("Powered", "PowerOn");
            AddIndicator("Ready", "Ready");
            AddIndicator("Busy", "Busy");
            AddIndicator("Dispensing", "Dispensing");
            AddIndicator("Done", "Done");
            AddIndicator("Status valid", "DataValid");
            AddIndicator("Suction on", "SuctionOn");
            AddIndicator("Error", "Error", onIsBad: true);

            AddSwitch("Read status", "ReadStatus", "Reads the dispenser's status continuously, which fills the values below.");

            AddCommand("Start", "Start", "Play", isPrimary: true);
            AddCommand("Stop", "Stop", "Square");
            AddCommand("Reset error", "Reset", "RotateCcw");

            AddSettings("Dispensing settings", "Save to dispenser", "Save",
                new DeviceSetting("Recipe (file) no.", "Recipe", "", 0, 99, 0, readBackKey: "RecipeInUse"),
                new DeviceSetting("Mixing ratio", "Ratio", ": 1", 0, 99.9, 1, readBackKey: "RatioInUse",
                    visibleWhenKey: "DualFluid", visibleWhenValue: true),
                new DeviceSetting("Time", "Time", "s", 0, 99.9, 1, readBackKey: "TimeInUse",
                    visibleWhenKey: "DualFluid", visibleWhenValue: false),
                new DeviceSetting("Volume", "Volume", "µl", 0, 999.9, 1, readBackKey: "VolumeInUse",
                    visibleWhenKey: "DualFluid", visibleWhenValue: false),
                new DeviceSetting("Speed", "Speed", "µl/s", 0, 999.9, 1, readBackKey: "SpeedInUse"));

            AddReadings("Dispenser",
                Number("Mode (0 time, 1 auto, 2 volume, 3 suction, 4 drop)", "Mode", "", 0),
                Number("Dispenser alarm code", "DeviceAlarm", "", 0),
                Text("Last reply", "LastReply"));

            AddReadings("Side A",
                Number("Speed", "SpeedA", "rpm"),
                Number("Output", "OutputA", "µl"),
                Number("Pressure", "PressureA", "kg"),
                Number("Glue level", "GlueA", "mm"),
                Number("Current", "CurrentA", "A"),
                Number("Turns", "TurnsA", "r"));

            AddReadings("Side B",
                Number("Speed", "SpeedB", "rpm"),
                Number("Output", "OutputB", "µl"),
                Number("Pressure", "PressureB", "kg"),
                Number("Glue level", "GlueB", "mm"),
                Number("Current", "CurrentB", "A"),
                Number("Turns", "TurnsB", "r"));

            AddReadings("As the dispenser reports",
                Number("Ratio", "ReportedRatio", "", 1),
                Number("Time", "ReportedTime", "s", 1),
                Number("Time speed", "ReportedTimeSpeed", "µl/s", 1),
                Number("Volume", "ReportedVolume", "µl", 1),
                Number("Volume speed", "ReportedVolumeSpeed", "µl/s", 1),
                Number("Auto speed", "ReportedAutoSpeed", "µl/s", 1),
                Number("Pressure limit", "PressureLimit", "kg", 2),
                Number("Ratio fix", "RatioFix", "", 2),
                Number("Level deviation", "LevelDeviation", "", 2),
                Number("Suction speed", "SuctionSpeed", "", 1),
                Number("Suction time", "SuctionTime", "s", 1),
                Number("Drop speed", "DropSpeed", "", 1),
                Number("Drop time", "DropTime", "s", 1),
                Number("Drop interval", "DropInterval", "s", 1));

            Complete();
        }
    }

    /// <summary>
    /// Manual &gt; Weighing: the glue weighing scale (LY-60005003, RS-232). Weigh waits for
    /// a stable weight and judges it against the min / max limits; Read once takes the
    /// current weight; Tare zeroes the scale.
    /// </summary>
    public sealed class WeighingScaleControlViewModel : PlcDevicePanelViewModel
    {
        public override string SelectorCaption => "Select weighing scale";

        public WeighingScaleControlViewModel(IPlcDeviceService service, IDialogService dialog = null)
            : base(service, dialog)
        {
            ConnectedKey = "Connected";
            AutoModeKey = "AutoMode";
            AlarmActiveKey = "AlarmActive";
            AlarmCodeKey = "AlarmCode";
            AlarmTextKey = "AlarmText";
            AlarmDetailKey = "AlarmDetail";

            AddIndicator("Stable", "Stable");
            AddIndicator("Unstable", "Unstable", onIsBad: true);
            AddIndicator("Busy", "Busy");
            AddIndicator("Weighing done", "WeighDone");
            AddIndicator("Pass", "Pass");
            AddIndicator("Fail", "Fail", onIsBad: true);
            AddIndicator("Communication error", "CommError", onIsBad: true);

            AddCommand("Weigh", "Weigh", "Scale", isPrimary: true);
            AddCommand("Read once", "ReadOnce", "Eye");
            AddCommand("Tare / zero", "Tare", "CircleDashed");
            AddCommand("Stop", "Stop", "Square");
            AddCommand("Reset error", "Reset", "RotateCcw");

            AddSettings("Pass / fail limits", null, null,
                new DeviceSetting("Minimum weight", "MinWeight", "g", 0, 1000, 4),
                new DeviceSetting("Maximum weight", "MaxWeight", "g", 0, 1000, 4));

            AddReadings("Weight",
                Number("Actual weight", "Weight", "g", 4),
                Text("Last reply", "LastReply"));

            AddReadings("Scale settings (set by the PLC)",
                Number("Read mode (0 S, 1 SI, 2 SIR, 3 SNR)", "ReadMode", "", 0),
                Number("Stable timeout", "StableTimeout", "ms", 0));

            Complete();
        }
    }

    /// <summary>
    /// Manual &gt; Vibration: the vibration and temperature sensors (Banner QM30VT3, Modbus
    /// RS-485). Monitoring reads vibration and temperature continuously and checks them
    /// against the health limits; the sensor's own settings (Fmax) are read and written
    /// with monitoring off.
    /// </summary>
    public sealed class VibrationSensorControlViewModel : PlcDevicePanelViewModel
    {
        public override string SelectorCaption => "Select vibration sensor";

        public VibrationSensorControlViewModel(IPlcDeviceService service, IDialogService dialog = null)
            : base(service, dialog)
        {
            ConnectedKey = "Connected";
            AlarmActiveKey = "AlarmActive";
            AlarmCodeKey = "AlarmCode";

            AddIndicator("Data valid", "DataValid");
            AddIndicator("Busy", "Busy");
            AddIndicator("Vibration warning", "VibrationWarning", onIsBad: true);
            AddIndicator("Over-jerk", "OverJerk", onIsBad: true);
            AddIndicator("Temperature warning", "TemperatureWarning", onIsBad: true);
            AddIndicator("Over-temperature", "OverTemperature", onIsBad: true);
            AddIndicator("Settings read", "ConfigReadDone");
            AddIndicator("Settings written", "ConfigWriteDone");
            AddIndicator("Settings write failed", "ConfigWriteError", onIsBad: true);
            AddIndicator("Communication error", "CommError", onIsBad: true);

            AddSwitch("Monitoring", "Monitoring", "Reads vibration and temperature continuously and checks them against the limits. "
                + "Switch it off to read or write the sensor's settings.");

            AddCommand("Read sensor settings", "ReadSettings", "Download");
            AddCommand("Reset warnings", "Reset", "RotateCcw");

            AddSettings("Sensor setting", "Write to sensor", "WriteSettings",
                new DeviceSetting("Fmax setting", "Fmax", "", 0, 15, 0, readBackKey: "FmaxInUse"));

            AddSettings("Health limits", null, null,
                new DeviceSetting("Vibration minimum", "VibrationMin", "", 0, 1000, 3),
                new DeviceSetting("Vibration maximum", "VibrationMax", "", 0, 1000, 3),
                new DeviceSetting("Over-jerk limit", "OverJerkLimit", "", 0, 1000, 3),
                new DeviceSetting("Temperature minimum", "TemperatureMin", "°C", -40, 150, 1),
                new DeviceSetting("Temperature maximum", "TemperatureMax", "°C", -40, 150, 1),
                new DeviceSetting("Over-temperature limit", "OverTemperatureLimit", "°C", -40, 150, 1),
                new DeviceSetting("Scale factor", "ScaleFactor", "", 0, 100, 4));

            AddReadings("Live",
                Number("Vibration (RMS velocity)", "Vibration", "", 3),
                Number("Temperature", "Temperature", "°C", 1));

            AddReadings("Sensor settings (read back)",
                Number("Fmax", "FmaxInUse", "", 0),
                Number("Baud rate (code)", "BaudRate", "", 0),
                Number("Parity (code)", "Parity", "", 0),
                Number("Modbus address", "ModbusAddress", "", 0),
                Number("Axis (1 X, 2 Y, 3 Z)", "Axis", "", 0),
                Number("Error id", "ErrorId", "", 0));

            Complete();
        }
    }
}
