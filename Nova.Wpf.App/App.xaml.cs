using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Core.Comm;
using Nova.Hive;
using Nova.Core.Config;
using Nova.Core.Hardware;
using Nova.Core.Services;
using Nova.ViewModels.Auth;
using Nova.ViewModels.Common;
using Nova.ViewModels.Pages.Audio;
using Nova.ViewModels.Pages.Manual;
using Nova.ViewModels.Pages.Recipe;
using Nova.ViewModels.Pages.Settings;
using Nova.ViewModels.Shells;
using Nova.Wpf.App.Services;
using Nova.Wpf.App.Services.Twincat;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Nova.Wpf.App
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;

        /// <summary>
        /// Config problems found while composing services, shown once after the shell is
        /// up. ConfigureServices runs before the container exists, so it cannot reach
        /// IDialogService itself.
        /// </summary>
        private static readonly List<string> _startupWarnings = new List<string>();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Pin the UI culture. WPF's Calendar/DatePicker take their month header and
            // day-name abbreviations from CultureInfo.CurrentCulture, so on a machine whose
            // regional format is not English the report date pickers rendered in that
            // language. The HMI is English-only and every date it formats or parses uses
            // dd-MM-yyyy, which is en-GB.
            ApplyUiCulture();

            // Culture alone does not reach the calendar popups: HandyControl's theme sets
            // Language="zh-cn" on Calendar as a Style setter, which outranks the default
            // ApplyUiCulture establishes above. Overridden with a Style setter of our own.
            Services.CalendarLanguageFix.Apply();

            // The confirm button and AM/PM labels come from HandyControl's own resource
            // table, which ships in Chinese only. Must run before any HandyControl control
            // is built.
            Services.HandyControlEnglishStrings.Apply();

            // LiveCharts2 ships a PublicKeyToken that doesn't match between
            // .NET Standard 2.0 and .NET 4.8. Load its DLLs from the exe folder.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (args.Name.StartsWith("LiveChartsCore") || args.Name.StartsWith("SkiaSharp"))
                {
                    string assemblyName = new System.Reflection.AssemblyName(args.Name).Name;
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, assemblyName + ".dll");
                    if (File.Exists(path))
                    {
                        return System.Reflection.Assembly.LoadFrom(path);
                    }
                }
                return null;
            };

            // An unhandled exception tears the process down and OnExit never runs, so both
            // crash routes go through the same RunShutdown as a normal exit.
            //
            // Both routes also RECORD the exception first. This used to write only to
            // Debug.WriteLine and then call Shutdown(), so on a production machine the
            // HMI vanished mid-shift leaving nothing behind to diagnose it with - the
            // operator saw the application close and that was the entire error report.
            DispatcherUnhandledException += (sender, args) =>
            {
                RecordCrash("UI thread", args.Exception);
                RunShutdown("unhandled UI exception");

                args.Handled = true;
                Shutdown();
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                RecordCrash("background thread", args.ExceptionObject as Exception);
                RunShutdown("unhandled background exception");
            };

            try
            {
                // NLog targets beside the exe, for Teal.Library.Log.Logger. Logging must
                // never take the app down, so a bad/missing config is reported to Debug.
                try
                {
                    NLog.LogManager.LoadConfiguration("NLog.config");
                }
                catch (Exception logEx)
                {
                    System.Diagnostics.Debug.WriteLine("NLog configuration failed: " + logEx.Message);
                }

                var services = new ServiceCollection();
                ConfigureServices(services);
                _serviceProvider = services.BuildServiceProvider();

                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();

                _serviceProvider.GetRequiredService<IMachineTelemetryService>().Start();
                _serviceProvider.GetRequiredService<IAxisMonitorService>().Start();
                _serviceProvider.GetRequiredService<IDigitalIOService>().Start();
                _serviceProvider.GetRequiredService<ILoadCellMonitorService>().Start();
                _serviceProvider.GetRequiredService<IRunningLogService>().Start();
                _serviceProvider.GetRequiredService<IActiveAlarmService>().Start();
                _serviceProvider.GetRequiredService<IConnectionStatusService>().Start();

                // HIVE reporting and the alarm pop-up. Only registered with a PLC
                // (HardwareMode=Twincat), so both are null when running simulated.
                _serviceProvider.GetService<HiveService>()?.Start();
                _serviceProvider.GetService<AlarmPopupHost>()?.Start();

                // Slide 39's five-minute idle sign-out for Levels 2 and 3. Started
                // after the window exists: it watches InputManager, which needs a
                // running dispatcher.
                _serviceProvider.GetRequiredService<IdleLogoutService>().Start();

                if (_startupWarnings.Count > 0)
                {
                    _serviceProvider.GetRequiredService<IDialogService>().ShowWarning(
                        string.Join(Environment.NewLine, _startupWarnings),
                        "Startup");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fatal Error during Application Startup:\n{ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// Writes a crash to Crash.log beside the exe and tells the operator where it
        /// went. Never throws: this runs while the application is already failing, and
        /// a fault in the reporter would replace the real exception with its own.
        /// </summary>
        private static void RecordCrash(string where, Exception ex)
        {
            string detail = ex == null ? "(no exception object)" : ex.ToString();
            System.Diagnostics.Debug.WriteLine("Unhandled " + where + " exception: " + detail);

            string path = null;
            try
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Crash.log");
                File.AppendAllText(path,
                    "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - unhandled "
                    + where + " exception ===" + Environment.NewLine + detail
                    + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception writeEx)
            {
                System.Diagnostics.Debug.WriteLine("Crash log could not be written: " + writeEx.Message);
                path = null;
            }

            // MessageBox, not IDialogService: the container is being torn down and the
            // dialog host belongs to a window that may already be gone.
            try
            {
                MessageBox.Show(
                    "The application hit an unrecoverable error and has to close." + Environment.NewLine
                    + Environment.NewLine
                    + (ex == null ? "Unknown error." : ex.Message) + Environment.NewLine
                    + Environment.NewLine
                    + (path == null
                        ? "The details could not be written to a log file."
                        : "Details written to:" + Environment.NewLine + path),
                    "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception uiEx)
            {
                System.Diagnostics.Debug.WriteLine("Crash dialog could not be shown: " + uiEx.Message);
            }
        }

        /// <summary>
        /// Forces the whole app onto en-GB - the culture whose short date is dd-MM-yyyy,
        /// the format every screen already formats and parses with. Sets it three ways
        /// because three different layers read three different sources:
        ///   * Thread/DefaultThread culture - what CultureInfo.CurrentCulture returns,
        ///     which is where Calendar and DatePicker get their month/day names;
        ///   * FrameworkElement.Language - what a control uses when it is set explicitly,
        ///     and what StringFormat bindings resolve against;
        ///   * both Current* and DefaultThreadCurrent*, so background threads (the axis
        ///     and IO monitors, the report queries) parse and format identically.
        /// </summary>
        private static void ApplyUiCulture()
        {
            try
            {
                var culture = new System.Globalization.CultureInfo("en-GB");
                System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;

                FrameworkElement.LanguageProperty.OverrideMetadata(
                    typeof(FrameworkElement),
                    new FrameworkPropertyMetadata(
                        System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
            }
            catch (Exception cultureEx)
            {
                // A culture that will not load must never stop the machine starting.
                System.Diagnostics.Debug.WriteLine("UI culture setup failed: " + cultureEx.Message);
            }
        }

        /// <summary>
        /// Hands one positive-integer App.config value to a view-model setting, leaving
        /// the existing default alone when the key is absent, blank or not a positive
        /// number. Limits must fail open: a typo in App.config should not be able to set
        /// a ceiling of zero and lock the operator out of a control.
        /// </summary>
        private static void ApplyPositiveIntSetting(string key, Action<int> apply)
        {
            int value;
            if (int.TryParse(ConfigurationManager.AppSettings[key], out value) && value > 0)
                apply(value);
        }

        /// <summary>
        /// Builds the container. Sections: 1 core services, 2 data access, 3 machine
        /// control and devices, 4 shells, 5 pages, 6 page factory, 7 the window.
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

            // Manual > Axis point-to-point limits. Nova.ViewModels is netstandard and cannot
            // reach ConfigurationManager, so the host hands them over.
            ApplyPositiveIntSetting("PtpIterationCount", v => AxisControlViewModel.MaxIterations = v);
            ApplyPositiveIntSetting("PPCIterationLen", v => AxisControlViewModel.MaxIterationDigits = v);

            // Commissioning only: opens Config and Settings to everyone as Level 3. Set
            // before any view model is built, because the pages read it once.
            bool bypassAccessLevels;
            AccessBypass.ConfigAndSettings =
                bool.TryParse(ConfigurationManager.AppSettings["BypassAccessLevels"], out bypassAccessLevels)
                && bypassAccessLevels;

            // 1. Core services
            services.AddSingleton<IAppSession, AppSession>();

            // Access control - DFM slides 44, 45 and 52. The badge check, the audit
            // trail it writes, and the idle timeout from slide 39.
            //
            // JsonAccessControlService reads a local copy of the access database. The
            // DFM specifies this check as a call to MES; swapping in a real client is
            // a change to this one line, because everything downstream depends on
            // IAccessControlService rather than on where the answer came from.
            services.AddSingleton<IAccessControlService>(sp => new JsonAccessControlService(
                dataDirectory,
                SettingOrDefault("TerminalName", string.Empty),
                SettingOrDefault("SoftwareType", "Main")));
            services.AddSingleton<IAccessLog, FileAccessLog>();
            services.AddSingleton<IdleLogoutService>();

            services.AddSingleton<IDialogService, WpfDialogService>();
            services.AddSingleton<INavigationService, WpfNavigationService>();
            services.AddSingleton<ILogFolderService, WindowsLogFolderService>();
            // Save on the Alarm page (DFM slide 56) and Export on the Data page
            // (slides 59/60). Both view models take this as an optional dependency and
            // disable their button when it is absent, so leaving it unregistered left
            // two buttons on screen that silently did nothing.
            services.AddSingleton<ICsvExportService, CsvExportService>();
            // Running-log feed for the Home screen, drained from Teal.Library.Log's queue.
            services.AddSingleton<IRunningLogService, RunningLogService>();
            services.AddSingleton<IRecipeService>(sp =>
                new JsonRecipeService(dataDirectory, sp.GetRequiredService<IAppSession>()));
            services.AddSingleton<IMachineSettingsService>(sp =>
                new JsonMachineSettingsService(dataDirectory, sp.GetRequiredService<IAppSession>()));
            // Machine name / version for the title bar, read from Data\config.txt.
            services.AddSingleton<IMachineIdentityService, ConfigFileMachineIdentityService>();
            // Site / line / station / product type for the Audio indicator bar, station
            // card and Settings page (Audio SW DFM v5.4 slides 42, 43, 62). Writable,
            // unlike the read-only identity profile above, so it has its own JSON file.
            services.AddSingleton<IStationProfileService>(sp => new JsonStationProfileService(dataDirectory));
            services.AddSingleton<IMachineContext>(sp =>
            {
                var context = new MachineContext();
                string cardType = ConfigurationManager.AppSettings["MasterCardType"];
                if (!string.IsNullOrWhiteSpace(cardType))
                {
                    context.MasterCardType = cardType;
                }

                return context;
            });

            // 2. Data access to TEAL_Master: SQL when a connection string is configured,
            //    otherwise the CSV fallback.
            string connectionString = ConfigurationManager.AppSettings["connectionString"];
            Master.Repository.IDataAccess dataAccess = string.IsNullOrWhiteSpace(connectionString)
                ? (Master.Repository.IDataAccess)new Master.Repository.CsvDataAccess(dataDirectory)
                : new Master.Repository.SQLDataAccess(connectionString);
            services.AddSingleton(dataAccess);
            services.AddSingleton<IShiftSettingsRepository>(new LegacyShiftSettingsRepository(dataAccess));
            services.AddSingleton<IAlarmLogRepository>(new LegacyAlarmLogRepository(dataAccess));
            services.AddSingleton<IProductionRepository>(new LegacyProductionRepository(dataAccess));
            // Machine Status / Machine Health report tabs (SP_GetMachineStatusReport /
            // SP_GetMachineHealthReport).
            services.AddSingleton<IReportRepository>(new LegacyReportRepository(dataAccess));

            // 3. Machine control, alarms and devices. HardwareMode=Twincat drives the steppers
            //    through the Beckhoff library over ADS and reads the PLC's alarm list
            //    (Nova.Alarm). Everything else is simulated until its TwinCAT adapter is
            //    written: the orchestrator will drive the PLC's machine command bits.
            string hardwareMode = ConfigurationManager.AppSettings["HardwareMode"];
            bool twincat = string.Equals(hardwareMode, "Twincat", StringComparison.OrdinalIgnoreCase);
            if (!twincat && !string.IsNullOrWhiteSpace(hardwareMode) &&
                !string.Equals(hardwareMode, "Simulated", StringComparison.OrdinalIgnoreCase))
            {
                _startupWarnings.Add("HardwareMode \"" + hardwareMode + "\" is not recognised - use Simulated or "
                    + "Twincat. The application is running simulated.");
            }

            services.AddSingleton<IAlarmService, SimulatedAlarmService>();
            services.AddSingleton<IMachineOrchestrator, SimulatedMachineOrchestrator>();
            var resolveAddress = RegisterSimulatedDevices(services, dataDirectory);

            var stepperConfigs = ReadJson<List<StepperConfiguration>>(dataDirectory, "Steppers.json");
            if (twincat)
            {
                var plc = ConnectPlc();
                services.AddSingleton(plc);
                services.AddSingleton<IStepperService>(new StepperService(
                    TwincatDevices.CreateSteppers(plc, stepperConfigs, _startupWarnings)));

                // Real cylinders replace the simulated ones registered above: the container
                // resolves the last registration, so this wins for ICylinderService.
                services.AddSingleton<ICylinderService>(new CylinderService(
                    TwincatDevices.CreateCylinders(
                        plc,
                        ReadJson<List<CylinderConfiguration>>(dataDirectory, "Cylinders.json"),
                        SettingOrDefault("PlcIoGvl", ""),
                        resolveAddress,
                        _startupWarnings)));
                services.AddSingleton<IConnectionStatusService>(sp => new ConnectionStatusService(
                    sp.GetRequiredService<ICommDeviceService>(), "TwinCAT " + plc.Address, () => plc.IsConnected));

                // Alarms: read from the PLC's alarm list and saved to the [Alarms] table.
                AlarmDatabase alarmDatabase = null;
                if (string.IsNullOrWhiteSpace(connectionString))
                    _startupWarnings.Add("No connectionString in App.config - alarms are shown but not saved.");
                else
                    alarmDatabase = new AlarmDatabase(connectionString, () => dataAccess.GetCurrentShift()?.ShiftName);

                services.AddSingleton(sp => new AlarmMonitor(new PlcAlarmReader(plc.AmsNetId, plc.Port), alarmDatabase));
                services.AddSingleton<IActiveAlarmService>(sp => sp.GetRequiredService<AlarmMonitor>());

                // HIVE: the alarms above decide the HIVE machine state, and the alarm
                // pop-up asks the operator to badge in before it closes.
                services.AddSingleton(sp => new HiveService(
                    BuildHiveSettings(dataDirectory, sp.GetRequiredService<IStationProfileService>()),
                    sp.GetRequiredService<AlarmMonitor>()));
                services.AddSingleton(sp => new AlarmPopupHost(
                    sp.GetRequiredService<AlarmMonitor>(),
                    sp.GetRequiredService<HiveService>(),
                    sp.GetRequiredService<INavigationService>()));

                // The HIVE chip's state-change pop-ups (DFM slides 47 and 49).
                services.AddSingleton<IHiveStatePrompt>(sp => new HiveStatePrompt(sp.GetRequiredService<HiveService>()));
            }
            else
            {
                services.AddSingleton<IActiveAlarmService, SimulatedActiveAlarmService>();

                var steppers = new List<IStepper>();
                foreach (var cfg in stepperConfigs)
                    steppers.Add(new SimulatedStepper(cfg.EnumName, cfg.IsAxisEnabled));
                services.AddSingleton<IStepperService>(new StepperService(steppers));
                services.AddSingleton<IConnectionStatusService>(sp => new ConnectionStatusService(
                    sp.GetRequiredService<ICommDeviceService>(), "TwinCAT (simulated)", () => false));
            }
            services.AddSingleton<IAxisMonitorService>(sp =>
                new AxisMonitorService(
                    sp.GetRequiredService<IAxisService>(),
                    sp.GetRequiredService<IStepperService>()));

            // 4. Shell ViewModels. AudioShellViewModel is the shell the window shows -
            //    this application drives one Audio machine and has one GUI, the DFM's.
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<AuthShellViewModel>();
            services.AddSingleton<AudioShellViewModel>();
            services.AddSingleton<AppTitleBarViewModel>();
            services.AddSingleton<LoginViewModel>();

            // 5. Page ViewModels - the six Audio tabs of the indicator bar.
            services.AddSingleton<AudioAlarmViewModel>();
            services.AddSingleton<AudioDataViewModel>();
            services.AddSingleton<AudioVisionViewModel>();
            services.AddSingleton<AudioSettingsViewModel>();

            //    Home hosts the manual-operation GUI in its Vendor pane (slide 57) and
            //    the running log beside it.
            services.AddSingleton(sp => new AudioHomeViewModel(
                sp.GetRequiredService<IAppSession>(),
                sp.GetRequiredService<IMachineTelemetryService>(),
                sp.GetRequiredService<IMachineOrchestrator>(),
                sp.GetRequiredService<IStationProfileService>(),
                sp.GetRequiredService<IConnectionStatusService>(),
                sp.GetRequiredService<ILogFolderService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<ManualViewModel>(),
                sp.GetRequiredService<IRunningLogService>()));

            //    Config hosts the machine-configuration tree and the taught recipe as
            //    the two sections of its Machine Configs area (slide 58).
            services.AddSingleton(sp => new AudioConfigViewModel(
                sp.GetRequiredService<IAppSession>(),
                sp.GetRequiredService<IMachineSettingsService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<SettingsViewModel>(),
                sp.GetRequiredService<RecipeViewModel>()));

            //    The hosted screens themselves. None is routable; each is reached
            //    through the page that hosts it.
            services.AddSingleton<ManualViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<RecipeViewModel>();

            // 6. Compile-time safe page factory - the six Audio tabs, in indicator-bar
            //    order (DFM v5.4 slide 42). Nothing else is navigable.
            services.AddSingleton<Func<AppPageNames, PageViewModel>>(sp => pageName =>
            {
                switch (pageName)
                {
                    case AppPageNames.Home:
                        return sp.GetRequiredService<AudioHomeViewModel>();
                    case AppPageNames.Alarm:
                        return sp.GetRequiredService<AudioAlarmViewModel>();
                    case AppPageNames.Config:
                        return sp.GetRequiredService<AudioConfigViewModel>();
                    case AppPageNames.Data:
                        return sp.GetRequiredService<AudioDataViewModel>();
                    case AppPageNames.Vision:
                        return sp.GetRequiredService<AudioVisionViewModel>();
                    case AppPageNames.Settings:
                        return sp.GetRequiredService<AudioSettingsViewModel>();

                    default:
                        throw new ArgumentOutOfRangeException(nameof(pageName), pageName,
                            "Not a navigable page. Manual, MachineConfig and Recipe are hosted "
                            + "inside Home and Config rather than routed to.");
                }
            });

            // 7. Main window
            services.AddSingleton<MainWindow>();
        }

        /// <summary>
        /// Simulated devices built from the machine profile under Data\, so the Manual
        /// screens show the real device names and addresses with no PLC connected.
        /// Steppers are registered by ConfigureServices, per HardwareMode.
        /// Returns the Data\Config.csv name-to-wiring-address lookup, so a device that
        /// ConfigureServices replaces with a TwinCAT one shows the same addresses.
        /// </summary>
        private static Func<string, bool, string> RegisterSimulatedDevices(IServiceCollection services, string dataDirectory)
        {
            var axes = new List<IAxis>();
            foreach (var name in Enum.GetNames(typeof(Nova.Abstractions.Hardware.Enums.Servos)))
                axes.Add(new SimulatedAxis(name));
            services.AddSingleton<IAxisService>(new AxisService(axes));

            var inputs = new List<IOPointSnapshot>();
            var outputs = new List<IOPointSnapshot>();
            try
            {
                IoConfigCsvLoader.Load(Path.Combine(dataDirectory, "Config.csv"), inputs, outputs);
            }
            catch (Exception ex)
            {
                _startupWarnings.Add("Data\\Config.csv could not be read: " + ex.Message);
            }
            services.AddSingleton<IDigitalIOService>(new SimulatedDigitalIOService(inputs, outputs));
            services.AddSingleton<ILoadCellMonitorService, SimulatedLoadCellMonitorService>();
            services.AddSingleton<IMachineTelemetryService, SimulatedMachineTelemetryService>();

            string ResolveAddress(string enumName, bool isOutput)
            {
                foreach (var point in isOutput ? outputs : inputs)
                    if (string.Equals(point.Name, enumName, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrEmpty(point.Address) ? enumName : point.Address;
                return enumName;
            }

            var cylinders = new List<ICylinder>();
            foreach (var cfg in ReadJson<List<CylinderConfiguration>>(dataDirectory, "Cylinders.json"))
                cylinders.Add(new SimulatedCylinder(
                    cfg.EnumName, cfg.Name,
                    cfg.ExtendValves.Count > 0 ? ResolveAddress(cfg.ExtendValves[0], true) : "",
                    cfg.RetractValves.Count > 0 ? ResolveAddress(cfg.RetractValves[0], true) : "",
                    cfg.ExtendSenses.Select(n => ResolveAddress(n, false)).ToList(),
                    cfg.RetractSenses.Select(n => ResolveAddress(n, false)).ToList()));
            services.AddSingleton<ICylinderService>(new CylinderService(cylinders));

            var vacuums = new List<IVacuum>();
            foreach (var cfg in ReadJson<List<VacuumConfiguration>>(dataDirectory, "Vacuums.json"))
                vacuums.Add(new SimulatedVacuum(
                    cfg.EnumName, cfg.Name,
                    string.IsNullOrEmpty(cfg.VacuumSense) ? "" : ResolveAddress(cfg.VacuumSense, false)));
            services.AddSingleton<IVacuumService>(new VacuumService(vacuums));

            // Comm devices + light: simulated transports so the console tabs work with no
            // network, read from the same CCDs/Scanners/Robots/SFC.json profile files.
            var commDevices = new List<KeyValuePair<CommDeviceKind, CommDevice>>();

            void AddFlat(CommDeviceKind kind, string fileName)
            {
                if (!File.Exists(Path.Combine(dataDirectory, fileName)))
                    return;
                foreach (var cfg in ReadJson<List<SocketClientConfiguration>>(dataDirectory, fileName))
                    commDevices.Add(new KeyValuePair<CommDeviceKind, CommDevice>(kind,
                        new CommDevice(cfg.EnumName, $"{cfg.IPAddress}:{cfg.Port}", "", new SimulatedNetworkProtocol(cfg.EnumName))));
            }

            AddFlat(CommDeviceKind.Ccd, "CCDs.json");
            AddFlat(CommDeviceKind.Scanner, "Scanners.json");
            AddFlat(CommDeviceKind.Sfc, "SFC.json");

            if (File.Exists(Path.Combine(dataDirectory, "Robots.json")))
            {
                foreach (var cfg in ReadJson<List<RobotConfigurations>>(dataDirectory, "Robots.json"))
                    commDevices.Add(new KeyValuePair<CommDeviceKind, CommDevice>(CommDeviceKind.Robot,
                        new CommDevice(cfg.EnumName, $"{cfg.IPAddress}:{cfg.Ports?.Communication}", "", new SimulatedNetworkProtocol(cfg.EnumName))));
            }

            services.AddSingleton<ICommDeviceService>(new CommDeviceService(commDevices));
            services.AddSingleton<ILightService>(new LightService(
                new List<ILightController> { new SimulatedLightController("OPT Light") }));

            return ResolveAddress;
        }

        /// <summary>
        /// Opens the ADS connection from App.config's Plc* keys. A PLC that is unreachable or
        /// not in Run is reported once at startup; the connection keeps retrying, so the
        /// steppers come alive when the runtime does.
        /// </summary>
        private static TwincatPlcConnection ConnectPlc()
        {
            int port;
            if (!int.TryParse(ConfigurationManager.AppSettings["PlcAdsPort"], out port) || port <= 0)
                port = 851;

            var plc = new TwincatPlcConnection(
                SettingOrDefault("PlcAmsNetId", "127.0.0.1.1.1"),
                port,
                SettingOrDefault("PlcStepperGvl", "GLOBAL_STEPPER"),
                SettingOrDefault("PlcStepperArray", "gStepper"));

            if (!plc.Connect())
            {
                _startupWarnings.Add("The PLC at " + plc.Address + " is not reachable or not in Run (" + plc.LastError
                    + "). The steppers show \"PLC not connected\" until it is; the connection keeps retrying.");
            }

            return plc;
        }

        /// <summary>
        /// HIVE reporting settings: the gateway and machine type come from App.config, the
        /// software version, its hash and the log folder from the station profile.
        /// </summary>
        private static HiveSettings BuildHiveSettings(string dataDirectory, IStationProfileService profile)
        {
            int sequence;
            if (!int.TryParse(ConfigurationManager.AppSettings["HiveSequence"], out sequence) || sequence < 0)
                sequence = 0;

            bool enabled = string.Equals(ConfigurationManager.AppSettings["HiveEnabled"], "true",
                StringComparison.OrdinalIgnoreCase);

            if (!enabled)
            {
                _startupWarnings.Add("HiveEnabled is not \"true\" in App.config, so nothing is sent to HIVE. "
                    + "Alarms, the alarm pop-up and the HIVE log still work. HIVE must be switched on before "
                    + "the machine runs on the line.");
            }

            return new HiveSettings
            {
                Enabled = enabled,
                GatewayUrl = SettingOrDefault("HiveGatewayUrl", "http://10.0.0.2:5008"),
                MachineType = SettingOrDefault("HiveMachineType", ""),
                Sequence = sequence,
                StateFile = SettingOrDefault("HiveStateFile", @"C:\Hive\LatestHiveState.ini"),
                ErrorCodeFile = Path.Combine(dataDirectory, "HiveErrorCodes.csv"),
                // The PLC says what state the machine is in; the HIVE state is worked
                // out from it. Same ADS target as the rest of the HMI.
                PlcAmsNetId = SettingOrDefault("PlcAmsNetId", "127.0.0.1.1.1"),
                PlcAdsPort = PlcAdsPort(),
                MachineStateSymbol = SettingOrDefault("PlcMachineStateSymbol", "GVL_Module.eStationState"),
                SoftwareVersion = profile.Current.SoftwareVersion ?? "",
                MainSoftwareHash = profile.ComputeMainSoftwareHash() ?? "",
                LogFolder = string.IsNullOrWhiteSpace(profile.Current.HiveLogPath)
                    ? @"C:\TEAL_Log_Data\HIVE"
                    : profile.Current.HiveLogPath,
            };
        }

        /// <summary>The PLC's ADS port from App.config, 851 when it is missing or invalid.</summary>
        private static int PlcAdsPort()
        {
            int port;
            if (!int.TryParse(ConfigurationManager.AppSettings["PlcAdsPort"], out port) || port <= 0)
                port = 851;

            return port;
        }

        private static string SettingOrDefault(string key, string fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static T ReadJson<T>(string dataDirectory, string fileName)
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(Path.Combine(dataDirectory, fileName)));
        }

        /// <summary>
        /// Set to 1 by the first caller into <see cref="RunShutdown"/>. Shutdown can be
        /// reached from the normal exit, a UI-thread exception and a background-thread
        /// exception, and the last two can race, so the first caller wins.
        /// </summary>
        private int _shutdownStarted;

        protected override void OnExit(ExitEventArgs e)
        {
            RunShutdown("application exit");
            base.OnExit(e);
        }

        /// <summary>
        /// The single close-down path for the application. Runs at most once. Stops the
        /// TwinCAT steppers so a manual move started from this HMI does not outlive it;
        /// everything else on the machine is the PLC's.
        /// </summary>
        /// <param name="reason">Why shutdown was triggered; recorded for diagnostics.</param>
        private void RunShutdown(string reason)
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
                return;

            System.Diagnostics.Debug.WriteLine("Shutdown started: " + reason);

            if (_serviceProvider == null)
                return;

            _serviceProvider.GetService<IMachineTelemetryService>()?.Stop();
            _serviceProvider.GetService<IAxisMonitorService>()?.Stop();
            _serviceProvider.GetService<ILoadCellMonitorService>()?.Stop();
            _serviceProvider.GetService<IDigitalIOService>()?.Stop();
            _serviceProvider.GetService<IRunningLogService>()?.Stop();
            _serviceProvider.GetService<AlarmPopupHost>()?.Stop();
            _serviceProvider.GetService<HiveService>()?.Stop();
            _serviceProvider.GetService<IActiveAlarmService>()?.Stop();
            _serviceProvider.GetService<IConnectionStatusService>()?.Stop();
            _serviceProvider.GetService<IdleLogoutService>()?.Dispose();

            var stepperService = _serviceProvider.GetService<IStepperService>();
            if (stepperService != null)
            {
                foreach (var stepper in stepperService.GetAllSteppers().OfType<TwincatStepperAdapter>())
                    stepper.StopOnExit();
            }

            // Registered as an instance, so the container does not dispose it.
            _serviceProvider.GetService<TwincatPlcConnection>()?.Dispose();

            _serviceProvider.Dispose();
        }
    }
}
