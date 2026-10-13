using Microsoft.Extensions.DependencyInjection;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Repositories;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Master.Configuration;
using Master.Configuration.Model;
using Nova.Core.Comm;
using Nova.Hive;
using Nova.Core.Config;
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
using Nova.Wpf.App.Services.Twincat.HmiDisplay;
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

                // The live status, as ADS notifications. What is always needed - the HIVE
                // machine state, the TCP devices' connections - is subscribed from here on; a
                // device's own status only while a screen showing it follows it.
                _serviceProvider.GetRequiredService<PlcSubscriptions>().Start();
                _serviceProvider.GetRequiredService<PlcSequenceLog>().Start();
                _serviceProvider.GetRequiredService<TwincatRecipeService>().Start();
                _serviceProvider.GetRequiredService<IAxisMonitorService>().Start();
                _serviceProvider.GetRequiredService<IDigitalIOService>().Start();
                _serviceProvider.GetRequiredService<ILoadCellMonitorService>().Start();
                _serviceProvider.GetRequiredService<IRunningLogService>().Start();
                _serviceProvider.GetRequiredService<IActiveAlarmService>().Start();
                _serviceProvider.GetRequiredService<IConnectionStatusService>().Start();
                _serviceProvider.GetRequiredService<IHomingService>().Start();
                _serviceProvider.GetRequiredService<TwincatMachineOrchestrator>().Start();

                // HIVE reporting and the alarm pop-up. HIVE Control first: it applies the
                // HIVE Control tab's saved on/off choice before anything can be sent.
                _serviceProvider.GetRequiredService<IHiveControl>();
                _serviceProvider.GetRequiredService<HiveService>().Start();
                _serviceProvider.GetRequiredService<AlarmPopupHost>().Start();

                // Slide 39's five-minute idle sign-out for Levels 2 and 3. Started
                // after the window exists: it watches InputManager, which needs a
                // running dispatcher.
                _serviceProvider.GetRequiredService<IdleLogoutService>().Start();

                // Glue and nozzle warnings (S2.4.4.4); idle when there is no glue data source.
                _serviceProvider.GetRequiredService<GlueWarningMonitor>().Start();

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

            // Data > Yield target line. Blank or out of range draws no line.
            double targetYield;
            if (double.TryParse(ConfigurationManager.AppSettings["TargetYieldPercent"],
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out targetYield) && targetYield > 0 && targetYield <= 100)
            {
                AudioDataViewModel.TargetYieldPercent = targetYield;
            }

            // Commissioning only: opens Config and Settings to everyone as Level 3. Set
            // before any view model is built, because the pages read it once.
            bool bypassAccessLevels;
            AccessBypass.ConfigAndSettings =
                bool.TryParse(ConfigurationManager.AppSettings["BypassAccessLevels"], out bypassAccessLevels)
                && bypassAccessLevels;

            // Slide 37: a swiped badge of any other length is refused (login and HIVE dialogs).
            ApplyBadgeUidLength(SettingOrDefault("BadgeUidLength", string.Empty));

            // 1. Core services
            services.AddSingleton<IAppSession, AppSession>();

            // Access control - DFM slides 44, 45 and 52. The badge check, the audit
            // trail it writes, and the idle timeout from slide 39.
            //
            // Badges are checked against the local access list or with MES, as the
            // Setting page's Access Control tab says ("Access Control with MES",
            // Access Control V5.1). MES is only offered when MesUrl is set.
            string terminalName = SettingOrDefault("TerminalName", string.Empty);
            string softwareType = SettingOrDefault("SoftwareType", "Main");
            var mesClient = new MesClient(SettingOrDefault("MesUrl", string.Empty), terminalName);
            services.AddSingleton<IMesClient>(mesClient);
            services.AddSingleton<IAccessControlAdmin>(sp => new AccessControlAdmin(
                dataDirectory,
                mesClient.IsConfigured,
                CardScan.MinimumLength == CardScan.MaximumLength ? CardScan.MinimumLength : 0));
            services.AddSingleton(sp => new JsonAccessControlService(dataDirectory, terminalName, softwareType));
            services.AddSingleton<IAccessControlService>(sp => new RoutedAccessControlService(
                sp.GetRequiredService<JsonAccessControlService>(),
                new MesAccessControlService(
                    mesClient,
                    sp.GetRequiredService<IAccessControlAdmin>(),
                    softwareType,
                    () => sp.GetRequiredService<IStationProfileService>().Current.Station),
                sp.GetRequiredService<IAccessControlAdmin>(),
                sp.GetRequiredService<IAppSession>()));
            services.AddSingleton<IAccessLog, FileAccessLog>();
            services.AddSingleton(sp => new IdleLogoutService(
                sp.GetRequiredService<IAppSession>(),
                sp.GetRequiredService<IAccessLog>(),
                sp.GetRequiredService<IAccessControlAdmin>()));

            // Badge windows for the view models (two-step verification, planned-downtime
            // procedures) and the Windows file dialogs.
            services.AddSingleton<IBadgePrompt>(sp => new BadgePrompt(
                sp.GetRequiredService<IAccessControlService>(),
                sp.GetRequiredService<IAppSession>()));
            services.AddSingleton<IFileDialogService, WpfFileDialogService>();

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

            // What each gantry is fitted with (valve model and SNs, nozzle, glue, material),
            // recorded by the planned-downtime procedures; and the hash keys of the PD-15
            // page, which HIVE gets with every machine_state payload.
            services.AddSingleton<IConsumableStore>(sp => new JsonConsumableStore(dataDirectory));

            // PLC alarm codes to DFM codes (Data\HiveErrorCodes.csv) for the Alarm page and
            // Home's Error Statistics - the same codes HIVE is given.
            services.AddSingleton<IErrorCodeCatalog>(sp => new HiveErrorCodeCatalog(Path.Combine(dataDirectory, "HiveErrorCodes.csv")));
            services.AddSingleton<IHashKeyService>(sp => new HashKeyService(
                dataDirectory,
                sp.GetRequiredService<IMesClient>(),
                () => sp.GetRequiredService<IStationProfileService>().Current.Site,
                () => sp.GetRequiredService<IAccessControlAdmin>().Settings.ProjectCode,
                new Dictionary<string, string>
                {
                    // Until the PD-15 page sets the files: the main software and its access list.
                    { "MS_SHA1", System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty },
                    { "CD_SHA1", Path.Combine(dataDirectory, JsonAccessControlService.FileName) },
                }));
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
            services.AddSingleton<IShiftSettingsRepository>(new Master.Repository.ShiftSettingsRepository(dataAccess));
            services.AddSingleton<IAlarmLogRepository>(new Master.Repository.AlarmLogRepository(dataAccess));
            services.AddSingleton<IProductionRepository>(new Master.Repository.ProductionRepository(dataAccess));
            // Machine Status / Machine Health report tabs (SP_GetMachineStatusReport /
            // SP_GetMachineHealthReport).
            services.AddSingleton<IReportRepository>(new Master.Repository.ReportRepository(dataAccess));

            // 3. Machine control, alarms and devices, all on the PLC: the devices go through
            //    the Beckhoff library over ADS, and the alarms are the PLC's alarm list
            //    (Nova.Alarm).

            // The device configuration - Data\Servos.json, Steppers.json, Cylinders.json, the
            // TCP devices and the PLC card - is read through Master.Configuration, which reports
            // a missing or unreadable file instead of stopping the application. The card is the
            // exception: everything below is on its connection, so without it nothing can run.
            List<string> configurationProblems = MachineConfiguration.Load(dataDirectory);
            if (ConfigGlobalClass.CardConfiguration == null)
            {
                throw new InvalidOperationException("There is no usable PLC card in Data\\"
                    + MachineConfiguration.CardsFile + ", so the application cannot start."
                    + Environment.NewLine + string.Join(Environment.NewLine, configurationProblems));
            }
            _startupWarnings.AddRange(configurationProblems);

            // The run state behind the shell's Start / Pause / Stop buttons, held in the HMI
            // until the orchestrator that writes the PLC's machine command bits is written.

            // The IO points are the Data\Config.csv list; it also gives the cylinder and vacuum
            // boards the wiring address of each valve and sensor.
            var inputPoints = new List<IOPointSnapshot>();
            var outputPoints = new List<IOPointSnapshot>();
            try
            {
                IoConfigCsvLoader.Load(Path.Combine(dataDirectory, "Config.csv"), inputPoints, outputPoints);
            }
            catch (Exception ex)
            {
                _startupWarnings.Add("Data\\Config.csv could not be read: " + ex.Message);
            }

            string ResolveAddress(string enumName, bool isOutput)
            {
                foreach (var point in isOutput ? outputPoints : inputPoints)
                    if (string.Equals(point.Name, enumName, StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrEmpty(point.Address) ? enumName : point.Address;
                return enumName;
            }

            // The load cells are wired as digital inputs until their analog values are.
            services.AddSingleton<ILoadCellMonitorService>(sp =>
                new DigitalInputLoadCellMonitorService(sp.GetRequiredService<IDigitalIOService>()));

            // There is no light controller driver yet, so the Light Control tab lists none.
            services.AddSingleton<ILightService>(new LightService(new List<ILightController>()));

            var stepperConfigs = ConfigGlobalClass.StepperConfigurations;
            var plc = ConnectPlc();
            services.AddSingleton(plc);

            // Every device status the screens show is an ADS notification on the shared
            // connection: each adapter registers its symbols here, and nothing polls the PLC.
            var subscriptions = new PlcSubscriptions(plc);
            services.AddSingleton(subscriptions);

            // Master.Configuration creates the library object for every device in the
            // Data\*.json files, on the same card as the connection above; the adapters
            // below put them behind the Nova contracts.
            _startupWarnings.AddRange(DeviceObjects.Create(SettingOrDefault("PlcIoGvl", "")));

            // When a screen opens a device the library reads its current status; after that
            // its live status is its HMI display struct: the library says where it is, and
            // App.config names the struct's PLC type for each kind of device - the C#
            // struct of the same name reads it.
            Type servoDisplay = DisplayType<IAxisHmiDisplay>("ServoHmiDisplayType");
            Type ioStepperDisplay = DisplayType<IAxisHmiDisplay>("IOStepperHmiDisplayType");
            Type cylinderDisplay = DisplayType<ICylinderHmiDisplay>("CylinderHmiDisplayType");
            Type vacuumDisplay = DisplayType<IVacuumHmiDisplay>("VacuumHmiDisplayType");
            Type visionDisplay = DisplayType<INetworkHmiDisplay>("VisionHmiDisplayType");
            Type scannerDisplay = DisplayType<INetworkHmiDisplay>("ScannerHmiDisplayType");

            // The Stepper tab drives the steppers, all of them I/O steppers; a stepper driven as
            // an NC axis is a servo in Data\Servos.json, on the Axis tab.
            var steppers = TwincatDevices.CreateSteppers(
                plc, subscriptions, stepperConfigs, StepperObjects.StepperMotorObj, ioStepperDisplay, _startupWarnings);
            services.AddSingleton<IStepperService>(new StepperService(steppers));

            var cylinders = TwincatDevices.CreateCylinders(
                plc,
                subscriptions,
                ConfigGlobalClass.CylinderConfigurations,
                CylinderObjects.CylinderObjs,
                cylinderDisplay,
                ResolveAddress,
                _startupWarnings);
            services.AddSingleton<ICylinderService>(new CylinderService(cylinders));

            var vacuums = TwincatDevices.CreateVacuums(
                plc,
                subscriptions,
                ConfigGlobalClass.VacuumConfigurations,
                VacuumObjects.VacuumObjs,
                vacuumDisplay,
                ResolveAddress,
                _startupWarnings);
            services.AddSingleton<IVacuumService>(new VacuumService(vacuums));

            // The Axis tab: the servos.
            var axes = TwincatDevices.CreateAxes(
                plc,
                subscriptions,
                ConfigGlobalClass.ServoConfigurations,
                ServoObjects.ServoMotorObj,
                servoDisplay,
                _startupWarnings);
            services.AddSingleton<IAxisService>(new AxisService(axes));

            // Axes and steppers publish when the PLC pushes one of their values.
            services.AddSingleton<IAxisMonitorService>(new TwincatAxisMonitorService(subscriptions, axes, steppers));

            // The IO points are the Config.csv list; their state is read through the
            // library's card, only while the I/O Diagnostics page is on screen. The
            // cylinder and vacuum boards also refresh on IOChanged, raised when the PLC
            // pushes one of their HMI displays.
            services.AddSingleton<IDigitalIOService>(new TwincatDeviceIOService(
                inputPoints,
                outputPoints,
                plc,
                subscriptions,
                cylinders.OfType<ITwincatStatusSource>().Concat(vacuums.OfType<ITwincatStatusSource>())));

            // The robots are on EtherCAT, not in Config.csv: the Robot tab reads their I/O
            // through the library's robot, only while the tab is on screen.
            services.AddSingleton<IRobotIOService>(new TwincatRobotIOService(
                TwincatDevices.CreateRobots(plc, ConfigGlobalClass.RobotConfigurations, RobotObjects.Robots, _startupWarnings),
                plc));

            // CCDs and scanners go through their PLC function blocks. The library has no SFC
            // class yet, so the SFC & PDCA tab lists no server.
            var commDevices = new List<KeyValuePair<CommDeviceKind, CommDevice>>();
            AddTcpDevices(commDevices, plc, subscriptions, CommDeviceKind.Ccd, "CCDs",
                ConfigGlobalClass.CCDConfigurations, CCDObjects.CCDs, visionDisplay);
            AddTcpDevices(commDevices, plc, subscriptions, CommDeviceKind.Scanner, "scanners",
                ConfigGlobalClass.ScannerConfigurations, ScannerObjects.Scanners, scannerDisplay);
            services.AddSingleton<ICommDeviceService>(new CommDeviceService(commDevices));

            // The Home page's Homing button: sets the PLC's home command bit TRUE and
            // waits for the PLC's homing-done bit. The PLC owns the homing sequence.
            services.AddSingleton<IHomingService>(new TwincatHomingService(
                plc,
                subscriptions,
                SettingOrDefault("PlcHomingCommandSymbol", ""),
                SettingOrDefault("PlcHomingDoneSymbol", "")));

            // The Setting page's Calibration tab: the PLC runs each calibration routine; the
            // HMI keeps the parameters per station, writes them, and presses Start / Pause / Stop.
            services.AddSingleton<ICalibrationService>(new TwincatCalibrationService(
                plc,
                subscriptions,
                SettingOrDefault("PlcCalibrationCommandSymbol", ""),
                SettingOrDefault("PlcCalibrationStatusSymbol", ""),
                dataDirectory));

            // The Recipe page's gantry recipes: the PLC drives every taught move from its own
            // copy, one per gantry. Written on Save, and again each time the PLC comes back.
            services.AddSingleton(sp => new TwincatRecipeService(
                plc,
                subscriptions,
                sp.GetRequiredService<IRecipeService>(),
                SettingOrDefault("PlcRecipeSymbol", "")));
            services.AddSingleton<IPlcRecipeService>(sp => sp.GetRequiredService<TwincatRecipeService>());

            // The indicator bar's Start / Pause / Stop pulse the PLC's mode buttons and follow
            // its machine state; the Setting page's Manual tab holds the PLC in manual mode.
            services.AddSingleton(sp => new TwincatMachineOrchestrator(
                plc,
                subscriptions,
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IHomingService>(),
                SettingOrDefault("PlcMachineStateSymbol", ""),
                SettingOrDefault("PlcStartSymbol", ""),
                SettingOrDefault("PlcPauseSymbol", ""),
                SettingOrDefault("PlcStopSymbol", ""),
                SettingOrDefault("PlcManualModeSymbol", "")));
            services.AddSingleton<IMachineOrchestrator>(sp => sp.GetRequiredService<TwincatMachineOrchestrator>());

            // The PLC sequences' logs, into the running log - watched whatever is on screen.
            services.AddSingleton(new PlcSequenceLog(
                subscriptions,
                SettingOrDefault("PlcSequenceLogSymbols", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)));

            // Connected = subscribed with the PLC in Run, which the subscriptions track
            // from the PLC's own state notification rather than by asking.
            services.AddSingleton<IConnectionStatusService>(sp => new ConnectionStatusService(
                sp.GetRequiredService<ICommDeviceService>(), "TwinCAT " + plc.Address, () => subscriptions.IsLive));

            // Alarms: read from the PLC's alarm list and saved to the [Alarms] table.
            AlarmDatabase alarmDatabase = null;
            if (string.IsNullOrWhiteSpace(connectionString))
                _startupWarnings.Add("No connectionString in App.config - alarms are shown but not saved.");
            else
                alarmDatabase = new AlarmDatabase(dataAccess);

            services.AddSingleton(sp => new AlarmMonitor(new PlcAlarmReader(plc), alarmDatabase));
            services.AddSingleton<IActiveAlarmService>(sp => sp.GetRequiredService<AlarmMonitor>());

            // HIVE: the alarms above decide the HIVE machine state, and the alarm
            // pop-up asks the operator to badge in before it closes.
            services.AddSingleton(sp =>
            {
                HiveSettings hiveSettings = BuildHiveSettings(dataDirectory, sp.GetRequiredService<IStationProfileService>());

                // Every hash key of the PD-15 page, read live on each state change
                // (slides 12 and 43), and the remote qualification log while the
                // Setting page has it ON (S2.4).
                IHashKeyService hashes = sp.GetRequiredService<IHashKeyService>();
                IStationProfileService stationProfile = sp.GetRequiredService<IStationProfileService>();
                hiveSettings.ReadHashKeys = () => hashes.ReadAll();
                hiveSettings.RemoteQualification = () => stationProfile.Current.RemoteQualification;
                return hiveSettings;
            });
            services.AddSingleton(sp => new HiveService(
                sp.GetRequiredService<HiveSettings>(),
                sp.GetRequiredService<AlarmMonitor>(),
                subscriptions));
            services.AddSingleton(sp => new AlarmPopupHost(
                sp.GetRequiredService<AlarmMonitor>(),
                sp.GetRequiredService<HiveService>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IAccessControlService>(),
                SettingOrDefault("AlarmPopupCloseLevel", "1") == "2" ? AccessLevel.Level2
                    : SettingOrDefault("AlarmPopupCloseLevel", "1") == "3" ? AccessLevel.Level3
                    : AccessLevel.Level1,
                !string.Equals(SettingOrDefault("AlarmPopupCloseButton", "true"), "false", StringComparison.OrdinalIgnoreCase)));

            // The HIVE chip's state-change pop-ups (DFM slides 47 and 49).
            // The badge for a state change is checked against the access list: Level 2/3 only (slide 49).
            // The planned-downtime procedures (S1.4, S3): glue / material SN, valve and
            // nozzle change with MES, LAD / CCD repeatability / pose calibration audits,
            // and PD-15 for the hash key page.
            services.AddSingleton(sp => new PlannedDowntimeProcedures(
                sp.GetRequiredService<IAccessControlService>(),
                sp.GetRequiredService<IConsumableStore>(),
                sp.GetRequiredService<IMesClient>(),
                sp.GetRequiredService<HiveService>()));
            services.AddSingleton<IHiveStatePrompt>(sp => new HiveStatePrompt(
                sp.GetRequiredService<HiveService>(),
                sp.GetRequiredService<IAccessControlService>(),
                sp.GetRequiredService<PlannedDowntimeProcedures>(),
                sp.GetRequiredService<IAppSession>()));

            // Home page: the State Changes chart reads the HIVE log back, and the
            // station card's HIVE link checks the gateway (slide 43).
            services.AddSingleton<IHiveStateHistory>(sp => new HiveLogStateHistory(sp.GetRequiredService<HiveSettings>().LogFolder));
            // Home: the Parameter Dashboard's live values and the Sensor Connection
            // Dashboard, from the PLC (Data\Settings\ParameterSymbols.json, Sensors.json).
            services.AddSingleton<IParameterValueSource>(new TwincatParameterValueSource(
                subscriptions, Path.Combine(dataDirectory, "Settings", "ParameterSymbols.json"), _startupWarnings));
            string sensorFile = Path.Combine(dataDirectory, "Settings", "Sensors.json");
            if (File.Exists(sensorFile))
            {
                services.AddSingleton<ISensorService>(sp =>
                {
                    try
                    {
                        return new TwincatSensorService(sp.GetRequiredService<IDigitalIOService>(), subscriptions, plc, sensorFile);
                    }
                    catch (Exception ex)
                    {
                        Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Error, "Sensors", "Load",
                            sensorFile + " could not be read: " + ex.Message, ex.ToString());
                        return null;
                    }
                });
            }

            services.AddSingleton(sp => new HiveLinkStatus(sp.GetRequiredService<HiveSettings>()));
            services.AddSingleton<IServiceLinkStatus>(sp => sp.GetRequiredService<HiveLinkStatus>());

            // Setting page: HIVE Control tab (S2.4 page 5), and the picture check (T5),
            // which puts the machine down and stops dispensing on a short picture count.
            services.AddSingleton<IHiveControl>(sp => new HiveControl(
                sp.GetRequiredService<HiveService>(),
                sp.GetRequiredService<HiveLinkStatus>(),
                Path.Combine(dataDirectory, "Settings", "HiveControl.json")));
            services.AddSingleton<IPictureQuantityCheck>(sp => new PictureQuantityCheck(
                dataDirectory,
                SettingOrDefault("PictureRecordFolder", @"D:\Pic_Upload_Record"),
                sp.GetRequiredService<IMachineSettingsService>(),
                detail => sp.GetRequiredService<HiveService>().ReportFault(new HiveError
                {
                    Code = "V99VSOO-01",
                    Message = "Vision Error",
                    Detail = detail,
                    Severity = "error",
                    StopsMachine = true,
                }),
                PlcBoolWriter(plc, "PictureDispensingSymbol")));

            // 4. Shell ViewModels. AudioShellViewModel is the shell the window shows -
            //    this application drives one Audio machine and has one GUI, the DFM's.
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<AuthShellViewModel>();
            services.AddSingleton<AudioShellViewModel>();
            services.AddSingleton<LoginViewModel>();

            // 5. Page ViewModels - the six Audio tabs of the indicator bar.
            //    SampleData=true gives the Alarm and Data pages generated alarms, units, glue
            //    data and coordinates instead of what the machine recorded, to show the pages
            //    before the machine has a history. Both pages label it as sample data.
            bool sampleData;
            if (bool.TryParse(ConfigurationManager.AppSettings["SampleData"], out sampleData) && sampleData)
            {
                services.AddSingleton(sp => new AudioAlarmViewModel(
                    sp.GetRequiredService<IAppSession>(),
                    alarms: new SampleAlarmLogRepository(),
                    csv: sp.GetService<ICsvExportService>(),
                    dialog: sp.GetService<IDialogService>()));
                services.AddSingleton(sp => new AudioDataViewModel(
                    sp.GetRequiredService<IAppSession>(),
                    production: new SampleProductionRepository(),
                    csv: sp.GetService<ICsvExportService>(),
                    dialog: sp.GetService<IDialogService>(),
                    dispenser: new SampleDispenserDataService(dataDirectory),
                    accessLog: sp.GetService<IAccessLog>(),
                    coordinates: new SampleCoordinateDataSource()));
            }
            else
            {
                services.AddSingleton<AudioAlarmViewModel>();
                services.AddSingleton<AudioDataViewModel>();
            }
            // Vision page: the newest image of each camera, from the folder the vision
            // software saves to (VisionImageFolder, one sub-folder per camera).
            string visionFolder = SettingOrDefault("VisionImageFolder", string.Empty);
            if (visionFolder.Length > 0)
                services.AddSingleton<ICcdImageSource>(new FolderCcdImageSource(visionFolder, new[] { "CCD1", "CCD2", "CCD3" }));
            services.AddSingleton<AudioVisionViewModel>();
            // PDATA Mapping tab of the Setting page: the field catalogue, the machine
            // profiles and the query template under PDATA\ beside the exe.
            services.AddSingleton<IPdataMappingService>(sp => new JsonPdataMappingService(
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IFileDialogService>(),
                dataDirectory));
            //    The Setting page's Manual tab hosts the manual-operation GUI, with the
            //    Homing button and the running log beside it, and holds the machine in
            //    manual mode while it is on screen.
            services.AddSingleton<AudioSettingsViewModel>();

            //    Home: its Vendor pane is empty - the manual GUI is on the Setting page.
            //    A source that is not registered is passed as null, and its panel says
            //    it is not available rather than showing anything made up.
            services.AddSingleton(sp => new AudioHomeViewModel(
                sp.GetRequiredService<IAppSession>(),
                orchestrator: sp.GetRequiredService<IMachineOrchestrator>(),
                profile: sp.GetRequiredService<IStationProfileService>(),
                connections: sp.GetRequiredService<IConnectionStatusService>(),
                logFolders: sp.GetRequiredService<ILogFolderService>(),
                dialog: sp.GetRequiredService<IDialogService>(),
                navigation: sp.GetRequiredService<INavigationService>(),
                production: sp.GetService<IProductionRepository>(),
                alarms: sp.GetService<IAlarmLogRepository>(),
                stateHistory: sp.GetService<IHiveStateHistory>(),
                settings: sp.GetService<IMachineSettingsService>(),
                parameterValues: sp.GetService<IParameterValueSource>(),
                dispenser: sp.GetService<IDispenserDataService>(),
                links: sp.GetServices<IServiceLinkStatus>(),
                sensors: sp.GetService<ISensorService>(),
                accessLog: sp.GetService<IAccessLog>(),
                errorCodes: sp.GetService<IErrorCodeCatalog>()));

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
                            + "inside Setting and Config rather than routed to.");
                }
            });

            // 7. Glue and nozzle warnings while the machine runs (S2.4.4.4): pop-ups, the
            //    PLC's buzzer and lights, MES staging, and downtime when the pot life runs
            //    out. Idle until a glue data source is registered: the Data page's sample
            //    glue data is not one, so it raises no pop-ups.
            services.AddSingleton(sp =>
            {
                HiveService hive = sp.GetRequiredService<HiveService>();
                TwincatPlcConnection plcConnection = sp.GetRequiredService<TwincatPlcConnection>();
                return new GlueWarningMonitor(
                    sp.GetService<IDispenserDataService>(),
                    sp.GetService<IBadgePrompt>(),
                    sp.GetService<IMesClient>(),
                    () => sp.GetRequiredService<IStationProfileService>().Current.Station,
                    () => sp.GetRequiredService<IAccessControlAdmin>().Settings.ProjectCode,
                    detail => hive.ReportFault(new HiveError
                    {
                        Code = "G99DSOO-01",
                        Message = "Glue Error",
                        Detail = detail,
                        Severity = "error",
                        StopsMachine = true,
                    }),
                    PlcIntWriter(plcConnection, "GlueAlertLevelSymbol"),
                    PlcBoolWriter(plcConnection, "GlueBuzzerMuteSymbol"));
            });

            // 8. Main window
            services.AddSingleton<MainWindow>();
        }

        /// <summary>Adds one console device per device of one kind Master.Configuration created, driven through the PLC.</summary>
        /// <param name="kindName">What the devices are called in the startup warnings.</param>
        private static void AddTcpDevices<TEnum, TConfig>(
            List<KeyValuePair<CommDeviceKind, CommDevice>> commDevices,
            TwincatPlcConnection plc,
            PlcSubscriptions subscriptions,
            CommDeviceKind kind,
            string kindName,
            List<TConfig> configs,
            IReadOnlyDictionary<TEnum, Teal.Library.TcpIp.TcpDevice> created,
            Type displayType)
            where TEnum : struct, Enum
            where TConfig : NetworkDeviceConfiguration
        {
            foreach (var device in TwincatDevices.CreateTcpDevices(plc, subscriptions, kindName, configs, created, displayType, _startupWarnings))
                commDevices.Add(new KeyValuePair<CommDeviceKind, CommDevice>(kind,
                    new CommDevice(device.Key.EnumName, $"{device.Key.IPAddress}:{device.Key.Port}", "", device.Value)));
        }

        /// <summary>
        /// The C# struct that reads one kind of device's HMI display: the one named like the
        /// PLC type App.config gives under <paramref name="settingKey"/>. Null, reported in the
        /// startup warnings, when there is none - those devices then show no live status.
        /// </summary>
        private static Type DisplayType<TDisplay>(string settingKey)
        {
            return HmiDisplays.Resolve<TDisplay>(settingKey, SettingOrDefault(settingKey, ""), _startupWarnings);
        }

        /// <summary>
        /// Opens the ADS connection to the card in Data\CardsConfiguration.json, which
        /// Master.Configuration builds. A PLC that is unreachable or not in Run is reported
        /// once at startup; the connection keeps retrying, so the devices come alive when the
        /// runtime does.
        /// </summary>
        private static TwincatPlcConnection ConnectPlc()
        {
            var plc = new TwincatPlcConnection(ConfigGlobalClass.cardManager.Card);

            if (!plc.Connect())
            {
                _startupWarnings.Add("The PLC at " + plc.Address + " is not reachable or not in Run (" + plc.LastError
                    + "). The devices, alarms and HIVE machine state show nothing from the PLC until it is; "
                    + "the connection keeps retrying.");
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
                    + "Alarms and the HIVE log still work, but the alarm pop-up is shown only while HIVE is enabled. HIVE must be switched on before "
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
                // out from it. Read over the HMI's shared ADS connection.
                MachineStateSymbol = SettingOrDefault("PlcMachineStateSymbol", "GVL_Module.eStationState"),
                UnitEnteredSymbol = SettingOrDefault("PlcUnitEnteredSymbol", ""),
                UnitEnteredIsCounter = string.Equals(SettingOrDefault("PlcUnitEnteredIsCounter", "false"), "true",
                    StringComparison.OrdinalIgnoreCase),
                PendingFile = SettingOrDefault("HivePendingFile", ""),
                SoftwareVersion = profile.Current.SoftwareVersion ?? "",
                MainSoftwareHash = profile.ComputeMainSoftwareHash() ?? "",
                LogFolder = string.IsNullOrWhiteSpace(profile.Current.HiveLogPath)
                    ? @"C:\TEAL_Log_Data\HIVE"
                    : profile.Current.HiveLogPath,
            };
        }

        /// <summary>
        /// Writes an HMI-owned BOOL to the PLC variable named by an App.config key, or null
        /// when the key is blank (nothing is written then).
        /// </summary>
        private static Action<bool> PlcBoolWriter(TwincatPlcConnection plc, string settingKey)
        {
            string symbol = SettingOrDefault(settingKey, string.Empty);
            if (symbol.Length == 0 || plc == null)
                return null;

            return value => plc.WriteSymbol(symbol, value);
        }

        /// <summary>As <see cref="PlcBoolWriter"/>, for an HMI-owned INT (16 bit).</summary>
        private static Action<int> PlcIntWriter(TwincatPlcConnection plc, string settingKey)
        {
            string symbol = SettingOrDefault(settingKey, string.Empty);
            if (symbol.Length == 0 || plc == null)
                return null;

            return value => plc.WriteSymbol(symbol, (short)value);
        }

        private static string SettingOrDefault(string key, string fallback)
        {
            string value = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary>
        /// BadgeUidLength: "10" or "8-10". Blank keeps CardScan's defaults; a value that
        /// cannot be read is reported and also keeps them.
        /// </summary>
        private static void ApplyBadgeUidLength(string setting)
        {
            if (setting.Length == 0)
                return;

            string[] parts = setting.Split('-');
            int min = 0, max = 0;
            bool valid = parts.Length <= 2
                && int.TryParse(parts[0].Trim(), out min)
                && int.TryParse(parts[parts.Length - 1].Trim(), out max)
                && min > 0 && max >= min;

            if (!valid)
            {
                _startupWarnings.Add("BadgeUidLength \"" + setting + "\" in App.config is not a length (e.g. 10) "
                    + "or a range (e.g. 8-10), so badges of 3 to 32 characters are accepted.");
                return;
            }

            CardScan.MinimumLength = min;
            CardScan.MaximumLength = max;
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

            _serviceProvider.GetService<IAxisMonitorService>()?.Stop();
            _serviceProvider.GetService<ILoadCellMonitorService>()?.Stop();
            _serviceProvider.GetService<IDigitalIOService>()?.Stop();
            _serviceProvider.GetService<PlcSequenceLog>()?.Stop();
            _serviceProvider.GetService<IRunningLogService>()?.Stop();
            _serviceProvider.GetService<AlarmPopupHost>()?.Stop();
            _serviceProvider.GetService<HiveService>()?.Stop();
            _serviceProvider.GetService<IActiveAlarmService>()?.Stop();
            _serviceProvider.GetService<IConnectionStatusService>()?.Stop();
            _serviceProvider.GetService<IdleLogoutService>()?.Dispose();
            _serviceProvider.GetService<GlueWarningMonitor>()?.Stop();

            var stepperService = _serviceProvider.GetService<IStepperService>();
            if (stepperService != null)
            {
                foreach (var stepper in stepperService.GetAllSteppers().OfType<TwincatStepperAdapter>())
                    stepper.StopOnExit();
            }

            // The machine is not left in manual mode by an HMI closed on the Manual tab.
            _serviceProvider.GetService<TwincatMachineOrchestrator>()?.ReleaseManualModeOnExit();

            // Registered as instances, so the container does not dispose them. The notifications
            // go before the connection they are on.
            _serviceProvider.GetService<PlcSubscriptions>()?.Stop();
            _serviceProvider.GetService<TwincatPlcConnection>()?.Dispose();

            _serviceProvider.Dispose();
        }
    }
}
