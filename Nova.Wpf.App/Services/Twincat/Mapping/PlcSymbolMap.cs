using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Nova.Wpf.App.Services.Twincat.Mapping
{
    /// <summary>
    /// Every TwinCAT variable name the HMI uses, in one place: <c>Data\PlcSymbols.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing else in the HMI holds a PLC variable name. A service is handed the part of this
    /// map it needs when it is created (App.xaml.cs), so when a PLC variable is renamed, moved
    /// or finally implemented, only the JSON file changes - no code.
    /// </para>
    /// <para>
    /// A blank entry means "the PLC has no variable for this yet": the function it belongs to
    /// reports itself as not available (a disabled button with the reason, a panel that says
    /// so) and nothing is written or shown in its place.
    /// </para>
    /// <para>
    /// The two exceptions are owned by the Beckhoff library, not by the HMI: where each
    /// device's function block is (<c>Data\CardsConfiguration.json</c>: DBName and the
    /// *DUTName entries) and the member names inside those blocks (hmicmd.*, HMI_CMD.*),
    /// which are compiled into <c>References\Beckhoff\Teal.Library.*.dll</c>.
    /// </para>
    /// </remarks>
    public sealed class PlcSymbolMap
    {
        public const string FileName = "PlcSymbols.json";

        /// <summary>HMI to PLC: the machine's push buttons and mode levels.</summary>
        public CommandSymbols Commands { get; set; } = new CommandSymbols();

        /// <summary>PLC to HMI: what the machine is doing.</summary>
        public StatusSymbols Status { get; set; } = new StatusSymbols();

        /// <summary>The operation mode of Config &gt; Device Settings (production, dry run, ...).</summary>
        public RunModeSymbols RunMode { get; set; } = new RunModeSymbols();

        /// <summary>The bypass switches and maintenance stations of Config &gt; Device Settings.</summary>
        public BypassSymbols Bypass { get; set; } = new BypassSymbols();

        public AlarmSymbols Alarms { get; set; } = new AlarmSymbols();

        public SequenceLogSymbols SequenceLog { get; set; } = new SequenceLogSymbols();

        /// <summary>The PLC's custom logs (FB_CustomLog): one row per log call, written by the HMI as CSV.</summary>
        public CustomLogSymbols CustomLog { get; set; } = new CustomLogSymbols();

        /// <summary>Where the taught recipes are written.</summary>
        public RecipeSymbols Recipe { get; set; } = new RecipeSymbols();

        public CalibrationSymbols Calibration { get; set; } = new CalibrationSymbols();

        /// <summary>The CNC channels that run the two dispensing stations.</summary>
        public CncSymbols Cnc { get; set; } = new CncSymbols();

        /// <summary>Process signals the HMI raises: glue warnings, the picture check's dispensing stop.</summary>
        public ProcessSymbols Process { get; set; } = new ProcessSymbols();

        /// <summary>PLC type names the library-backed devices need.</summary>
        public DeviceSymbols Devices { get; set; } = new DeviceSymbols();

        /// <summary>What the PLC holds about the unit that just finished (read once per Status.UnitDoneCount step).</summary>
        public UnitRecordSymbols UnitRecord { get; set; } = new UnitRecordSymbols();

        /// <summary>Data &gt; Axis: the measured coordinates of the unit that just finished.</summary>
        public CoordinateSymbols Coordinates { get; set; } = new CoordinateSymbols();

        /// <summary>Data &gt; Glue and Health, Home's glue panel, the glue warnings.</summary>
        public GlueSymbols Glue { get; set; } = new GlueSymbols();

        /// <summary>The Manual tab's dispensers, weighing scale and vibration sensors.</summary>
        public ThirdPartyDeviceSymbols ThirdPartyDevices { get; set; } = new ThirdPartyDeviceSymbols();

        /// <summary>
        /// Home &gt; Parameter Dashboard: the PLC variable behind each parameter of
        /// Config &gt; Spec Limits (Production), by parameter name.
        /// </summary>
        public Dictionary<string, ParameterSymbol> Parameters { get; set; }
            = new Dictionary<string, ParameterSymbol>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Home &gt; Sensor Connection Dashboard: the PLC's array of BOOLs that disables the sensors,
        /// one element per <c>Master.Configuration.ESensor</c> member in the enum's order.
        /// </summary>
        public SensorDisableSymbols SensorDisable { get; set; } = new SensorDisableSymbols();

        /// <summary>
        /// Reads the map. A missing or unreadable file gives an empty map - every function
        /// then reports itself as not wired - and says so in <paramref name="warnings"/>.
        /// </summary>
        public static PlcSymbolMap Load(string file, ICollection<string> warnings)
        {
            if (!File.Exists(file))
            {
                warnings?.Add("Data\\" + FileName + " is missing, so nothing is mapped to the PLC: the machine buttons, "
                    + "modes, recipes and calibration are not wired until it is restored.");
                return new PlcSymbolMap();
            }

            try
            {
                var settings = new JsonSerializerSettings
                {
                    // An entry the file sets to null keeps the section's own default.
                    NullValueHandling = NullValueHandling.Ignore,
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                };
                PlcSymbolMap map = JsonConvert.DeserializeObject<PlcSymbolMap>(File.ReadAllText(file), settings);
                return (map ?? new PlcSymbolMap()).Normalised();
            }
            catch (Exception ex)
            {
                warnings?.Add("Data\\" + FileName + " could not be read (" + ex.Message + "), so nothing is mapped to the "
                    + "PLC until it is corrected.");
                return new PlcSymbolMap();
            }
        }

        /// <summary>A section the file left out is an empty one, never null.</summary>
        private PlcSymbolMap Normalised()
        {
            Commands = Commands ?? new CommandSymbols();
            Status = Status ?? new StatusSymbols();
            RunMode = RunMode ?? new RunModeSymbols();
            Bypass = Bypass ?? new BypassSymbols();
            Alarms = Alarms ?? new AlarmSymbols();
            SequenceLog = SequenceLog ?? new SequenceLogSymbols();
            CustomLog = CustomLog ?? new CustomLogSymbols();
            CustomLog.Logs = new Dictionary<string, string>(
                CustomLog.Logs ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            Recipe = Recipe ?? new RecipeSymbols();
            Calibration = Calibration ?? new CalibrationSymbols();
            Calibration.StaticDynamic = Calibration.StaticDynamic ?? new StaticDynamicSymbols();
            Cnc =Cnc ?? new CncSymbols();
            Process = Process ?? new ProcessSymbols();
            Devices = Devices ?? new DeviceSymbols();
            UnitRecord = UnitRecord ?? new UnitRecordSymbols();
            Coordinates = Coordinates ?? new CoordinateSymbols();
            Glue = Glue ?? new GlueSymbols();
            ThirdPartyDevices = ThirdPartyDevices ?? new ThirdPartyDeviceSymbols();
            Parameters = new Dictionary<string, ParameterSymbol>(
                Parameters ?? new Dictionary<string, ParameterSymbol>(), StringComparer.OrdinalIgnoreCase);
            SensorDisable = SensorDisable ?? new SensorDisableSymbols();
            return this;
        }

        /// <summary>
        /// Every single PLC variable the map names (templates with {placeholders} left out),
        /// with what it is for - what <see cref="PlcSymbolCheck"/> looks up in the PLC.
        /// </summary>
        public IEnumerable<KeyValuePair<string, string>> NamedSymbols()
        {
            var symbols = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (purpose, symbol) =>
            {
                if (Has(symbol) && symbol.IndexOf('{') < 0)
                    symbols.Add(new KeyValuePair<string, string>(purpose, symbol.Trim()));
            };

            add("Commands.Start", Commands.Start);
            add("Commands.Pause", Commands.Pause);
            add("Commands.Stop", Commands.Stop);
            foreach (string reset in Commands.Reset ?? new List<string>())
                add("Commands.Reset", reset);
            add("Commands.ManualMode", Commands.ManualMode);
            add("Commands.HomingStart", Commands.HomingStart);

            add("Status.MachineState", Status.MachineState);
            add("Status.HomingDone", Status.HomingDone);
            add("Status.OperatorMessage", Status.OperatorMessage);
            add("Status.UnitEntered", Status.UnitEntered);
            add("Status.UnitDoneCount", Status.UnitDoneCount);
            add("Status.UnitDoneSerial", Status.UnitDoneSerial);

            add("RunMode.Request", RunMode.Request);
            add("RunMode.Active", RunMode.Active);
            add("RunMode.ChangeAllowed", RunMode.ChangeAllowed);

            foreach (var bypass in Bypass.Devices ?? new Dictionary<string, List<string>>())
                foreach (string symbol in bypass.Value ?? new List<string>())
                    add("Bypass.Devices." + bypass.Key, symbol);
            foreach (var station in Bypass.MaintenanceStations ?? new Dictionary<string, string>())
                add("Bypass.MaintenanceStations." + station.Key, station.Value);

            add("Alarms.List", Alarms.List);
            foreach (string entry in SequenceLog.Entries ?? new List<string>())
                add("SequenceLog.Entries", entry);
            foreach (var log in CustomLog.Logs ?? new Dictionary<string, string>())
                add("CustomLog.Logs." + log.Key, Has(log.Value) ? log.Value.Trim() + ".nWriteCount" : null);

            add("Recipe.Gantry", Recipe.Gantry);
            add("Recipe.Robot", Recipe.Robot);

            CalibrationManagerSymbols manager = Calibration.Manager ?? new CalibrationManagerSymbols();
            add("Calibration.Manager.Select", manager.Select);
            add("Calibration.Manager.Station", manager.Station);
            add("Calibration.Manager.Start", manager.Start);
            add("Calibration.Manager.Abort", manager.Abort);
            add("Calibration.Manager.Reset", manager.Reset);
            add("Calibration.Manager.Busy", manager.Busy);
            add("Calibration.Manager.Done", manager.Done);
            add("Calibration.Manager.Error", manager.Error);
            add("Calibration.Manager.Active", manager.Active);
            add("Calibration.Manager.ActiveStation", manager.ActiveStation);
            add("Calibration.Manager.Step", manager.Step);
            add("Calibration.Manager.Message", manager.Message);

            CalibrationTeachSymbols teach = Calibration.Teach ?? new CalibrationTeachSymbols();
            add("Calibration.Teach.NinePoint", teach.NinePoint);
            add("Calibration.Teach.FiveAxis", teach.FiveAxis);
            add("Calibration.Teach.CameraNeedle", teach.CameraNeedle);
            add("Calibration.Teach.NeedleModule", teach.NeedleModule);
            add("Calibration.Teach.Save", teach.Save);
            add("Calibration.Teach.SaveBusy", teach.SaveBusy);
            add("Calibration.Teach.SaveDone", teach.SaveDone);
            add("Calibration.Teach.SaveError", teach.SaveError);
            add("Calibration.Teach.SaveErrorId", teach.SaveErrorId);

            StaticDynamicSymbols sd = Calibration.StaticDynamic ?? new StaticDynamicSymbols();
            add("Calibration.StaticDynamic.Mode", sd.Mode);
            add("Calibration.StaticDynamic.Ccd", sd.Ccd);
            add("Calibration.StaticDynamic.Iteration", sd.Iteration);
            add("Calibration.StaticDynamic.Start", sd.Start);
            add("Calibration.StaticDynamic.End", sd.End);
            add("Calibration.StaticDynamic.Stop", sd.Stop);
            add("Calibration.StaticDynamic.Busy", sd.Busy);
            add("Calibration.StaticDynamic.Done", sd.Done);
            add("Calibration.StaticDynamic.Error", sd.Error);
            add("Calibration.StaticDynamic.Step", sd.Step);
            add("Calibration.StaticDynamic.Count", sd.Count);
            add("Calibration.StaticDynamic.Status", sd.Status);
            add("Calibration.StaticDynamic.VisionX", sd.VisionX);
            add("Calibration.StaticDynamic.VisionY", sd.VisionY);
            add("Calibration.StaticDynamic.VisionZ", sd.VisionZ);

            if (Calibration.Repeatability != null)
            {
                add("Calibration.Repeatability.CaptureCount", Calibration.Repeatability.CaptureCount);
                add("Calibration.Repeatability.Station", Calibration.Repeatability.Station?.Symbol);
                add("Calibration.Repeatability.X", Calibration.Repeatability.X?.Symbol);
                add("Calibration.Repeatability.Y", Calibration.Repeatability.Y?.Symbol);
                add("Calibration.Repeatability.Serial", Calibration.Repeatability.Serial?.Symbol);
            }

            if (Calibration.Pose != null)
            {
                add("Calibration.Pose.DoneCount", Calibration.Pose.DoneCount);
                foreach (var result in Calibration.Pose.Results ?? new Dictionary<string, ParameterSymbol>())
                    add("Calibration.Pose.Results." + result.Key, result.Value == null ? null : result.Value.Symbol);
            }

            add("Cnc.AxisEnable", Cnc.AxisEnable);

            // The members the 5 axis calibration's G-code moves use, for each mapped channel.
            foreach (var channel in Cnc.Channels ?? new Dictionary<string, CncChannelSymbols>())
            {
                if (channel.Value == null || channel.Value.PlcChannel < 1)
                    continue;

                var members = new[]
                {
                    new KeyValuePair<string, string>("MdiModeRequest", Cnc.Members.MdiModeRequest),
                    new KeyValuePair<string, string>("MdiProgram", Cnc.Members.MdiProgram),
                    new KeyValuePair<string, string>("CycleStart", Cnc.Members.CycleStart),
                    new KeyValuePair<string, string>("CycleStop", Cnc.Members.CycleStop),
                    new KeyValuePair<string, string>("MdiModeActive", Cnc.Members.MdiModeActive),
                    new KeyValuePair<string, string>("CycleRunning", Cnc.Members.CycleRunning),
                    new KeyValuePair<string, string>("ErrorState", Cnc.Members.ErrorState),
                };
                foreach (KeyValuePair<string, string> member in members)
                    add("Cnc.Members." + member.Key + " (channel " + channel.Key + ")", Cnc.PathOf(channel.Value.PlcChannel, member.Value));
            }

            add("Process.GlueAlertLevel", Process.GlueAlertLevel);
            add("Process.GlueBuzzerMute", Process.GlueBuzzerMute);
            add("Process.PictureDispensingStop", Process.PictureDispensingStop);
            add("Process.HmiStopRequest", Process.HmiStopRequest);
            add("Process.UnitUploadNg", Process.UnitUploadNg);
            add("Process.UnitUploadNgSerial", Process.UnitUploadNgSerial);
            add("Process.EarlyWarningLamp", Process.EarlyWarningLamp);
            add("Process.EarlyWarningBuzzer", Process.EarlyWarningBuzzer);
            add("Process.NgWarningBuzzer", Process.NgWarningBuzzer);

            foreach (var member in UnitRecord.All())
                add("UnitRecord." + member.Key, member.Value == null ? null : member.Value.Symbol);
            foreach (CoordinatePointSymbol point in Coordinates.Points ?? new List<CoordinatePointSymbol>())
                add("Coordinates.Points." + point.Point + "." + point.Axis, point.Symbol);
            foreach (var member in Glue.NamedSymbols())
                add("Glue." + member.Key, member.Value);
            foreach (var member in ThirdPartyDevices.NamedSymbols())
                add(member.Key, member.Value);

            foreach (var parameter in Parameters)
                add("Parameters." + parameter.Key, parameter.Value == null ? null : parameter.Value.Symbol);
            add("SensorDisable.Array", SensorDisable.Array);

            return symbols;
        }

        /// <summary>True when the entry names a PLC variable.</summary>
        public static bool Has(string symbol)
        {
            return !string.IsNullOrWhiteSpace(symbol);
        }

        /// <summary>The entry trimmed, or empty when it is blank.</summary>
        public static string Of(string symbol)
        {
            return string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim();
        }
    }

    /// <summary>HMI to PLC. Each is a BOOL unless said otherwise.</summary>
    /// <summary>The PLC array behind the Sensor Connection Dashboard's disable switches.</summary>
    public sealed class SensorDisableSymbols
    {
        /// <summary>
        /// The ARRAY OF BOOL: element FirstIndex + (int)ESensor disables that sensor when TRUE.
        /// Blank = the PLC has none yet, no sensor can be disabled.
        /// </summary>
        public string Array { get; set; }

        /// <summary>The array's lower bound in the PLC: 0 for ARRAY[0..n], 1 for ARRAY[1..n].</summary>
        public int FirstIndex { get; set; }
    }

    public sealed class CommandSymbols
    {
        /// <summary>START push button, pulsed. Also resumes from pause.</summary>
        public string Start { get; set; } = string.Empty;

        /// <summary>PAUSE push button, pulsed.</summary>
        public string Pause { get; set; } = string.Empty;

        /// <summary>STOP push button, pulsed.</summary>
        public string Stop { get; set; } = string.Empty;

        /// <summary>RESET push buttons, pulsed together in this order.</summary>
        public List<string> Reset { get; set; } = new List<string>();

        /// <summary>Held TRUE while a manual screen (Manual, Calibration, Recipe teach) is in front.</summary>
        public string ManualMode { get; set; } = string.Empty;

        /// <summary>Set TRUE by the Homing button; the PLC clears it. Blank: the PLC has no home command.</summary>
        public string HomingStart { get; set; } = string.Empty;

        /// <summary>How long a pulsed button stays TRUE, in ms. The PLC acts on the rising edge.</summary>
        public int PulseMilliseconds { get; set; } = 200;
    }

    /// <summary>PLC to HMI.</summary>
    public sealed class StatusSymbols
    {
        /// <summary>E_MachineState (INT): 0 idle, 1 auto running, 2 pause, 3 homing, 4 alarm, 5 manual, 6 stop.</summary>
        public string MachineState { get; set; } = string.Empty;

        /// <summary>BOOL, TRUE once the machine is homed.</summary>
        public string HomingDone { get; set; } = string.Empty;

        /// <summary>STRING(80): the PLC's own operator message for the state it is in.</summary>
        public string OperatorMessage { get; set; } = string.Empty;

        /// <summary>HIVE Running / Idle: a BOOL pulsed, or an INT counted up, for every unit that enters.</summary>
        public string UnitEntered { get; set; } = string.Empty;

        /// <summary>True when <see cref="UnitEntered"/> is a counter rather than a pulse.</summary>
        public bool UnitEnteredIsCounter { get; set; }

        /// <summary>UDINT/DINT counted up by the PLC for every finished unit (HIVE unit counting, audits).</summary>
        public string UnitDoneCount { get; set; } = string.Empty;

        /// <summary>STRING: serial number of the unit that last finished. Optional.</summary>
        public string UnitDoneSerial { get; set; } = string.Empty;
    }

    /// <summary>The PLC's run mode (E_RunMode).</summary>
    public sealed class RunModeSymbols
    {
        /// <summary>INT the HMI writes: the mode the operator wants.</summary>
        public string Request { get; set; } = string.Empty;

        /// <summary>INT the PLC reports: the mode it is really in.</summary>
        public string Active { get; set; } = string.Empty;

        /// <summary>BOOL, TRUE while the PLC accepts a mode change (standing still and empty).</summary>
        public string ChangeAllowed { get; set; } = string.Empty;

        /// <summary>
        /// The PLC's E_RunMode number for each HMI operation mode, by the mode's name
        /// (Production, DryRun, GRR, CPK, Static, Dynamic, Maintenance). A mode left out or
        /// null has no PLC mode behind it and cannot be selected.
        /// </summary>
        public Dictionary<string, int?> Values { get; set; } = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Bypass levels, each a BOOL the HMI holds.</summary>
    public sealed class BypassSymbols
    {
        /// <summary>
        /// The PLC BOOLs behind each bypass switch, by its name (SafetyDoor, Scanner, Buzzer,
        /// SfcPdca, Vision, Robot). All of a switch's BOOLs are written together. A switch
        /// with none cannot be changed.
        /// </summary>
        public Dictionary<string, List<string>> Devices { get; set; }
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The PLC BOOL that takes each station out of production, by station number.</summary>
        public Dictionary<string, string> MaintenanceStations { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class AlarmSymbols
    {
        /// <summary>ARRAY OF ST_Alarms: the PLC's central list of active alarms.</summary>
        public string List { get; set; } = string.Empty;
    }

    public sealed class SequenceLogSymbols
    {
        /// <summary>One ST_SeqLog per module: what that module's sequence last reported.</summary>
        public List<string> Entries { get; set; } = new List<string>();
    }

    public sealed class CustomLogSymbols
    {
        /// <summary>
        /// The PLC's FB_CustomLog instance of each custom log, by the key the HMI's
        /// <c>CustomLogs.All</c> uses (for example "Production": "MAIN.prodlog"). Blank = not fed from the PLC.
        /// </summary>
        public Dictionary<string, string> Logs { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RecipeSymbols
    {
        /// <summary>ARRAY OF FB_Recipe, indexed by gantry: the taught positions.</summary>
        public string Gantry { get; set; } = string.Empty;

        /// <summary>ARRAY OF typeRobotFB, indexed by robot. Blank on a machine without robots.</summary>
        public string Robot { get; set; } = string.Empty;
    }

    public sealed class CalibrationSymbols
    {
        /// <summary>The PLC's calibration manager (GVL_Calibration): what to run, its buttons and its status.</summary>
        public CalibrationManagerSymbols Manager { get; set; } = new CalibrationManagerSymbols();

        /// <summary>The PLC's taught calibration positions (GVL_CalTeach), written from Data\Calibration\*.json.</summary>
        public CalibrationTeachSymbols Teach { get; set; } = new CalibrationTeachSymbols();

        /// <summary>PD-13 CCD repeatability: one CCD capture at a time (S3.4).</summary>
        public RepeatabilitySymbols Repeatability { get; set; } = new RepeatabilitySymbols();

        /// <summary>PD-14 dispensing pose calibration: the results of one whole calibration (S3.5).</summary>
        public PoseCalibrationSymbols Pose { get; set; } = new PoseCalibrationSymbols();

        /// <summary>The Static &amp; Dynamic tab: the PLC's GVL_StaticDynamic (Static_Seq / Dynamic_Seq).</summary>
        public StaticDynamicSymbols StaticDynamic { get; set; } = new StaticDynamicSymbols();
    }

    /// <summary>
    /// The PLC's Static / Dynamic interface, GVL_StaticDynamic. Start / End / Stop are rising
    /// edges (R_TRIG in the sequences). The positions the cameras move to are the PLC's own
    /// (its recipe rows), so the HMI sends only the mode, the camera and the iteration count.
    /// </summary>
    public sealed class StaticDynamicSymbols
    {
        /// <summary>How many cameras the PLC's sequences serve: CCD 1 .. CCD n (the length of its aRcpGantry / aCncAxis tables).</summary>
        public int Cameras { get; set; } = 2;

        // HMI to PLC
        public string Mode { get; set; } = string.Empty;        // INT: 0 none, 1 static, 2 dynamic
        public string Ccd { get; set; } = string.Empty;         // INT: 1..Cameras
        public string Iteration { get; set; } = string.Empty;   // INT: vision commands per run, 1 or more
        public string Start { get; set; } = string.Empty;       // BOOL, rising edge
        public string End { get; set; } = string.Empty;        // BOOL, rising edge: finish after the running vision command
        public string Stop { get; set; } = string.Empty;        // BOOL, rising edge: abort now

        // PLC to HMI
        public string Busy { get; set; } = string.Empty;        // BOOL
        public string Done { get; set; } = string.Empty;        // BOOL
        public string Error { get; set; } = string.Empty;       // BOOL
        public string Step { get; set; } = string.Empty;        // INT
        public string Count { get; set; } = string.Empty;       // INT: vision commands answered so far
        public string Status { get; set; } = string.Empty;      // STRING(80): CURRENT STATUS
        public string VisionX { get; set; } = string.Empty;     // LREAL
        public string VisionY { get; set; } = string.Empty;     // LREAL
        public string VisionZ { get; set; } = string.Empty;     // LREAL

        /// <summary>What a run needs: the three settings and the three buttons.</summary>
        public bool CanRun => PlcSymbolMap.Has(Mode) && PlcSymbolMap.Has(Ccd) && PlcSymbolMap.Has(Iteration)
            && PlcSymbolMap.Has(Start) && PlcSymbolMap.Has(End) && PlcSymbolMap.Has(Stop);
    }

    /// <summary>
    /// One CCD capture of a repeatability test, read each time <see cref="CaptureCount"/> steps
    /// up. The PLC holds the capture's values until it counts the next one.
    /// </summary>
    public sealed class RepeatabilitySymbols
    {
        /// <summary>UDINT/DINT counted up by the PLC for every capture of a repeatability test.</summary>
        public string CaptureCount { get; set; } = string.Empty;

        /// <summary>INT: the station (1 / 2) whose CCD made the capture. Blank: station 1.</summary>
        public ParameterSymbol Station { get; set; } = new ParameterSymbol();

        /// <summary>LREAL: the capture's X result, mm. Required.</summary>
        public ParameterSymbol X { get; set; } = new ParameterSymbol();

        /// <summary>LREAL: the capture's Y result, mm. Required.</summary>
        public ParameterSymbol Y { get; set; } = new ParameterSymbol();

        /// <summary>STRING: the SN of the unit / UC under the CCD. Blank: Status.UnitDoneSerial is used.</summary>
        public ParameterSymbol Serial { get; set; } = new ParameterSymbol();

        /// <summary>By station number: the gantry name the key starts with ("front", "rear").</summary>
        public Dictionary<string, string> Gantries { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsMapped => PlcSymbolMap.Has(CaptureCount) && PlcSymbolMap.Has(X?.Symbol) && PlcSymbolMap.Has(Y?.Symbol);
    }

    /// <summary>
    /// The results of one dispensing pose calibration, read each time <see cref="DoneCount"/>
    /// steps up - the PLC counts it once the flatness and pose routines have all run.
    /// </summary>
    public sealed class PoseCalibrationSymbols
    {
        /// <summary>UDINT/DINT counted up by the PLC when a whole calibration has finished.</summary>
        public string DoneCount { get; set; } = string.Empty;

        /// <summary>
        /// Each result by the key HIVE wants (S3.5 P-12), e.g.
        /// "conveyor_L&amp;front_base_flatness", "conveyor_L&amp;front_dispensing_pose1_C".
        /// </summary>
        public Dictionary<string, ParameterSymbol> Results { get; set; } = new Dictionary<string, ParameterSymbol>(StringComparer.Ordinal);

        public bool IsMapped => PlcSymbolMap.Has(DoneCount)
            && (Results ?? new Dictionary<string, ParameterSymbol>()).Values.Any(r => r != null && PlcSymbolMap.Has(r.Symbol));
    }

    /// <summary>
    /// The calibration manager, FB_CalibrationManager: the only thing the HMI talks to for
    /// running a calibration. The buttons are rising edges the PLC does not clear.
    /// </summary>
    public sealed class CalibrationManagerSymbols
    {
        // HMI to PLC
        public string Select { get; set; } = string.Empty;          // INT: E_Calibration (1 nine point, 2 five axis, 3 needle, 4 camera needle)
        public string Station { get; set; } = string.Empty;         // INT: 1..4
        public string Start { get; set; } = string.Empty;           // BOOL, rising edge
        public string Abort { get; set; } = string.Empty;           // BOOL, rising edge
        public string Reset { get; set; } = string.Empty;           // BOOL, rising edge: clears Done / Error

        // PLC to HMI
        public string Busy { get; set; } = string.Empty;            // BOOL
        public string Done { get; set; } = string.Empty;            // BOOL
        public string Error { get; set; } = string.Empty;           // BOOL
        public string Active { get; set; } = string.Empty;          // INT: the calibration running or run last
        public string ActiveStation { get; set; } = string.Empty;   // INT
        public string Step { get; set; } = string.Empty;            // INT
        public string Message { get; set; } = string.Empty;         // STRING(80)
    }

    /// <summary>
    /// The teach tables, each ARRAY[1..stations] of a pack_mode 1 record, and the request
    /// that has the PLC store its persistent data.
    /// </summary>
    public sealed class CalibrationTeachSymbols
    {
        public string NinePoint { get; set; } = string.Empty;       // ARRAY OF ST_TeachNinePoint
        public string FiveAxis { get; set; } = string.Empty;        // ARRAY OF ST_TeachFiveAxis
        public string CameraNeedle { get; set; } = string.Empty;    // ARRAY OF ST_TeachCameraNeedle
        public string NeedleModule { get; set; } = string.Empty;    // ARRAY OF ST_TeachNeedleModule

        public string Save { get; set; } = string.Empty;            // BOOL the HMI sets, the PLC clears
        public string SaveBusy { get; set; } = string.Empty;        // BOOL
        public string SaveDone { get; set; } = string.Empty;        // BOOL
        public string SaveError { get; set; } = string.Empty;       // BOOL
        public string SaveErrorId { get; set; } = string.Empty;     // UDINT
    }

    /// <summary>
    /// The CNC channel interface (ST_ChannelData per channel). <see cref="Channel"/> is the
    /// path of one channel with {channel} where its number goes; every member below is
    /// relative to it, with {axis} where an axis number goes.
    /// </summary>
    public sealed class CncSymbols
    {
        /// <summary>For example GVL_Channel_Interface.CNCData.Ch[{channel}].</summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>BOOL the HMI holds TRUE to enable the CNC axes.</summary>
        public string AxisEnable { get; set; } = string.Empty;

        /// <summary>How many axes a channel has (the PLC's AXIS_COUNT).</summary>
        public int AxesPerChannel { get; set; } = 7;

        /// <summary>
        /// By HMI channel number: which recipe table (<see cref="RecipeSymbols.Gantry"/> index)
        /// holds the channel's taught positions.
        /// </summary>
        public Dictionary<string, CncChannelSymbols> Channels { get; set; }
            = new Dictionary<string, CncChannelSymbols>(StringComparer.OrdinalIgnoreCase);

        public CncMemberSymbols Members { get; set; } = new CncMemberSymbols();

        /// <summary>
        /// The number of each HMI axis (X1, Y1, Z1, A, R, X2, Z2) inside its channel, 1 to
        /// <see cref="AxesPerChannel"/>: its place in the PLC's per-axis arrays and its
        /// column in the channel's recipe table. An axis left out is not offered.
        /// </summary>
        public Dictionary<string, int> AxisIndex { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The speed written beside every taught CNC position. The PLC's FB_Recipe treats a
        /// position with speed 0 as "not taught"; the real feed is in the CNC program.
        /// </summary>
        public double TaughtVelocity { get; set; } = 1.0;

        public bool IsMapped => PlcSymbolMap.Has(Channel);

        /// <summary>The full path of a member of one channel, or empty when it is not mapped.</summary>
        public string PathOf(int channel, string member, int axis = 0)
        {
            if (!PlcSymbolMap.Has(Channel) || !PlcSymbolMap.Has(member))
                return string.Empty;

            return (Channel.Trim() + "." + member.Trim())
                .Replace("{channel}", channel.ToString())
                .Replace("{axis}", axis.ToString());
        }

        public CncChannelSymbols ChannelMap(int channel)
        {
            CncChannelSymbols symbols;
            return Channels != null && Channels.TryGetValue(channel.ToString(), out symbols) ? symbols : null;
        }
    }

    public sealed class CncChannelSymbols
    {
        /// <summary>The channel's number in the PLC interface.</summary>
        public int PlcChannel { get; set; }

        /// <summary>Index of the FB_Recipe that holds this channel's positions; 0 = not written.</summary>
        public int RecipeGantry { get; set; }
    }

    /// <summary>Members of one channel's interface struct.</summary>
    public sealed class CncMemberSymbols
    {
        // HMI to PLC
        public string JogModeRequest { get; set; } = string.Empty;      // BOOL, rising edge selects jog (manual) mode
        public string JogType { get; set; } = string.Empty;             // UINT: 1 rapid, 2 user speed, 3 inching
        public string JogAxisIndex { get; set; } = string.Empty;        // UINT: axis to jog, 0 = none
        public string JogPlus { get; set; } = string.Empty;             // BOOL, held
        public string JogMinus { get; set; } = string.Empty;            // BOOL, held
        public string JogSpeed { get; set; } = string.Empty;            // UINT, mm/min
        public string JogInchingValue { get; set; } = string.Empty;     // UDINT, 1 = 0.001 mm
        public string Reset { get; set; } = string.Empty;               // BOOL, rising edge

        // HMI to PLC: the MDI block the 5 axis calibration moves with (Master.Sequences, TwincatFiveAxisMotion)
        public string MdiModeRequest { get; set; } = string.Empty;      // BOOL, rising edge selects MDI mode
        public string MdiProgram { get; set; } = string.Empty;          // STRING(299): the G-code block
        public string CycleStart { get; set; } = string.Empty;          // BOOL, rising edge starts the block
        public string CycleStop { get; set; } = string.Empty;           // BOOL, rising edge

        // HMI to PLC: Auto mode (FiveAxisSettings.CncMode = "Auto"): the channel runs the .nc program in ProgramPath
        public string AutoModeRequest { get; set; } = string.Empty;     // BOOL, rising edge selects Auto mode
        public string ProgramPath { get; set; } = string.Empty;         // STRING(299): the program to run

        // PLC to HMI
        public string AutoModeActive { get; set; } = string.Empty;      // BOOL
        public string MdiModeActive { get; set; } = string.Empty;       // BOOL
        public string JogModeActive { get; set; } = string.Empty;       // BOOL
        public string ErrorState { get; set; } = string.Empty;          // BOOL
        public string CycleRunning { get; set; } = string.Empty;        // BOOL
        public string AxisActualPosition { get; set; } = string.Empty;  // REAL, with {axis}
        public string AxisEnabled { get; set; } = string.Empty;         // BOOL, with {axis}
        public string AxisHomed { get; set; } = string.Empty;           // BOOL, with {axis}
        public string AxisMoving { get; set; } = string.Empty;          // BOOL, with {axis}
        public string AxisError { get; set; } = string.Empty;           // BOOL, with {axis}
    }

    public sealed class ProcessSymbols
    {
        /// <summary>INT the HMI writes: 0 none, rising with the glue / nozzle warning level.</summary>
        public string GlueAlertLevel { get; set; } = string.Empty;

        /// <summary>BOOL the HMI writes to silence the glue warning buzzer.</summary>
        public string GlueBuzzerMute { get; set; } = string.Empty;

        /// <summary>BOOL the HMI holds TRUE to stop dispensing after a short picture count.</summary>
        public string PictureDispensingStop { get; set; } = string.Empty;

        /// <summary>
        /// BOOL the HMI holds TRUE while HIVE is in a downtime the HMI raised itself (hash key
        /// error, HIVE upload not confirmed): the PLC finishes the unit in process and stops.
        /// </summary>
        public string HmiStopRequest { get; set; } = string.Empty;

        /// <summary>
        /// BOOL the HMI pulses when HIVE failed a unit or did not confirm it (S12): the unit
        /// must not go to the next station, the PLC diverts it to the NG conveyor.
        /// </summary>
        public string UnitUploadNg { get; set; } = string.Empty;

        /// <summary>STRING the HMI writes before the pulse: the serial number of that unit. Optional.</summary>
        public string UnitUploadNgSerial { get; set; } = string.Empty;

        /// <summary>BOOL the HMI holds TRUE while an early-warning pop-up is open (S17): tower light red.</summary>
        public string EarlyWarningLamp { get; set; } = string.Empty;

        /// <summary>BOOL the HMI holds TRUE while that pop-up is open and not muted: buzzer 2 s on / 1 s off, timed by the PLC.</summary>
        public string EarlyWarningBuzzer { get; set; } = string.Empty;

        /// <summary>BOOL the HMI pulses at a unit's first NG (T6): the PLC sounds the buzzer, the machine goes on.</summary>
        public string NgWarningBuzzer { get; set; } = string.Empty;
    }

    public sealed class DeviceSymbols
    {
        /// <summary>
        /// The PLC type of each device kind's HMI display struct (Servo, IoStepper, Cylinder,
        /// Vacuum, Vision, Scanner). The C# struct of the same name in
        /// Services\Twincat\HmiDisplay reads it.
        /// </summary>
        public Dictionary<string, string> HmiDisplayTypes { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string HmiDisplayType(string kind)
        {
            string type;
            return HmiDisplayTypes != null && HmiDisplayTypes.TryGetValue(kind, out type) ? PlcSymbolMap.Of(type) : string.Empty;
        }
    }

    /// <summary>A PLC variable with its type: LREAL, REAL, DINT, INT, UDINT, UINT, BOOL or TIME.</summary>
    public sealed class ParameterSymbol
    {
        public string Symbol { get; set; } = string.Empty;

        public string Type { get; set; } = "LREAL";
    }
}
