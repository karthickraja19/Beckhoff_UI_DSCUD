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
using Nova.ViewModels.Pages.Calibration;
using Nova.ViewModels.Pages.Manual;
using Nova.ViewModels.Pages.Recipe;
using Nova.ViewModels.Pages.Settings;
using Nova.ViewModels.Shells;
using Nova.Wpf.App.Services;
using Nova.Wpf.App.Services.Twincat;
using Nova.Wpf.App.Services.Twincat.HmiDisplay;
using Nova.Wpf.App.Services.Twincat.Mapping;
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

        // TEAL_Master, kept for Master.Scheduler, which is started once the window is up.
        private static Master.Repository.MasterDatabase _database;

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

                    // The log files' names carry ${gdc:ShiftName}: "Default" until Master.Scheduler
                    // (ScheduleJobs.Start) reads the shift and GlobalClass.CurrentShift sets it.
                    Master.SharedUtilities.GlobalClass.CurrentShift = null;
                }
                catch (Exception logEx)
                {
                    System.Diagnostics.Debug.WriteLine("NLog configuration failed: " + logEx.Message);
                }

                var services = new ServiceCollection();
                ConfigureServices(services);
                _serviceProvider = services.BuildServiceProvider();

                // Every {loc:Tr} and {loc:TrBinding} in XAML reads through this. Attached
                // before the first window is built, so no screen opens in the wrong language.
                Nova.Wpf.UI.Localization.LocalizationSource.Instance.Attach(
                    _serviceProvider.GetRequiredService<ILocalizationService>());
                // Chart names and the HIVE alarm pop-up reach it through UiLanguage.
                UiLanguage.Service = _serviceProvider.GetRequiredService<ILocalizationService>();
                UiLanguage.Descriptions = _serviceProvider.GetRequiredService<IDescriptionService>();

                // Without the language files every screen stays English whatever is chosen;
                // say where they were looked for rather than failing silently.
                if (Teal.Library.Localization.LanguageManager.Available.Count == 0)
                {
                    _startupWarnings.Add("No language files were found, so the application can only show English. "
                        + "Expected Strings.resx, Strings.zh-CN.resx and Strings.vi.resx in: "
                        + Teal.Library.Localization.LocalizationPaths.ResourceDirectory
                        + ". Rebuild Nova.Wpf.App, which copies them from its Localization folder.");
                }

                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();

                // The live status, as ADS notifications. What is always needed - the HIVE
                // machine state, the TCP devices' connections - is subscribed from here on; a
                // device's own status only while a screen showing it follows it.
                _serviceProvider.GetRequiredService<PlcSubscriptions>().Start();

                // Every PLC variable of Data\PlcSymbols.json is looked up once the PLC is
                // live; the ones the PLC does not have are reported, not silently ignored.
                var symbolCheck = _serviceProvider.GetRequiredService<PlcSymbolCheck>();
                symbolCheck.Completed += (sender, args) =>
                {
                    string summary = symbolCheck.Summary();
                    if (summary != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                            _serviceProvider.GetRequiredService<IDialogService>().ShowWarning(summary, "PLC symbol map")));
                    }
                };
                symbolCheck.Start();

                _serviceProvider.GetRequiredService<PlcSequenceLog>().Start();
                _serviceProvider.GetRequiredService<PlcCustomLogBridge>().Start();
                _serviceProvider.GetRequiredService<TwincatRecipeService>().Start();
                _serviceProvider.GetRequiredService<IAxisMonitorService>().Start();
                _serviceProvider.GetRequiredService<IDigitalIOService>().Start();
                _serviceProvider.GetRequiredService<ILoadCellMonitorService>().Start();
                _serviceProvider.GetRequiredService<IRunningLogService>().Start();
                _serviceProvider.GetRequiredService<IActiveAlarmService>().Start();
                _serviceProvider.GetRequiredService<IConnectionStatusService>().Start();
                _serviceProvider.GetRequiredService<IHomingService>().Start();
                _serviceProvider.GetRequiredService<TwincatMachineOrchestrator>().Start();
                _serviceProvider.GetRequiredService<TwincatMachineModeLink>().Start();

                // HIVE reporting and the alarm pop-up. HIVE Control first: it applies the
                // HIVE Control tab's saved on/off choice before anything can be sent.
                _serviceProvider.GetRequiredService<IHiveControl>();
                HiveService hiveService = _serviceProvider.GetRequiredService<HiveService>();
                hiveService.Start();

                // HIVE is off while a screen drives the machine by hand (Manual, Calibration,
                // Recipe > Gantry / CNC): no state change, no red bar, no stop request.
                _serviceProvider.GetRequiredService<TwincatMachineOrchestrator>().ManualModeHeldChanged +=
                    hiveService.SetManualScreenActive;

                _serviceProvider.GetRequiredService<UnitUploadNgSignal>().Start();
                _serviceProvider.GetRequiredService<HashKeyGuard>().Start();
                _serviceProvider.GetRequiredService<NozzleHealthGuard>().Start();
                _serviceProvider.GetRequiredService<ValveSerialMonitor>().Start();
                _serviceProvider.GetRequiredService<MesStateReporter>().Start();
                _serviceProvider.GetRequiredService<AlarmPopupHost>().Start();
                _serviceProvider.GetRequiredService<NgStatistics>().Start();
                _serviceProvider.GetRequiredService<AuditResultWatcher>().Start();
                _serviceProvider.GetRequiredService<PlcUnitDoneWatcher>().Start();

                // Slide 39's five-minute idle sign-out for Levels 2 and 3. Started
                // after the window exists: it watches InputManager, which needs a
                // running dispatcher.
                _serviceProvider.GetRequiredService<IdleLogoutService>().Start();

                // Glue and nozzle warnings (S2.4.4.4); idle when there is no glue data source.
                _serviceProvider.GetRequiredService<GlueWarningMonitor>().Start();
                _serviceProvider.GetService<TwincatDispenserDataService>()?.Start();

                // Master.Scheduler (Quartz): the CSV clean-up under LogFilePath, the shift
                // status / OEE refresh and the nightly database purge. Without a database only
                // the CSV clean-up does anything.
                var schedulerDataAccess = _database != null && _database.IsAvailable ? _database.DataAccess : null;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await Master.Scheduler.ScheduleJobs.Start(schedulerDataAccess);

                        // After the scheduler: it gives ShiftStatusHelper its data access and shift.
                        if (schedulerDataAccess != null)
                            _serviceProvider.GetRequiredService<MachineHealthRecorder>().Start();
                    }
                    catch (Exception ex)
                    {
                        Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Error, "Scheduler", "Start",
                            "The scheduled jobs could not be started: " + ex.Message, ex.ToString());
                    }
                });

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
        /// <summary>
        /// Every spec limit parameter of the AE limit files (Production, CPK, GRR), for the
        /// Config Description tab to offer. Read with Newtonsoft rather than through the
        /// settings service so a malformed file only shortens the list.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, string>> SpecLimitParameters(string dataDirectory)
        {
            var found = new List<KeyValuePair<string, string>>();
            foreach (string kind in new[] { "Production", "CPK", "GRR" })
            {
                try
                {
                    string path = Path.Combine(dataDirectory, "Settings", "AeLimits_" + kind + ".json");
                    if (!File.Exists(path))
                        continue;

                    var document = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                    var rows = document["Rows"] as Newtonsoft.Json.Linq.JArray;
                    if (rows == null)
                        continue;

                    foreach (var row in rows)
                    {
                        string parameter = (string)row["Parameter"];
                        if (!string.IsNullOrWhiteSpace(parameter))
                            found.Add(new KeyValuePair<string, string>(parameter.Trim(), kind));
                    }
                }
                catch (Exception)
                {
                    // Skip a file that cannot be read.
                }
            }

            return found;
        }

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

        /// <summary>Where the machine's data lives when App.config TealDataDirectory is blank - V3's folder.</summary>
        private const string DefaultDataDirectory = @"C:\TEAL\Data";

        /// <summary>
        /// The folder the machine's configuration, recipes and settings are read from and saved
        /// to: App.config TealDataDirectory, C:\TEAL\Data when blank (as V3). The Data folder
        /// next to the exe holds the shipped defaults: any of them the folder does not have yet
        /// is copied in, never over a file that is already there, so what was saved on the
        /// machine survives a new build or install. A folder that cannot be created or written
        /// is reported at startup, and the exe's Data folder is used instead.
        /// </summary>
        private static string ResolveDataDirectory()
        {
            string shipped = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
            string configured = ConfigurationManager.AppSettings["TealDataDirectory"];
            string target = string.IsNullOrWhiteSpace(configured)
                ? DefaultDataDirectory
                : Environment.ExpandEnvironmentVariables(configured.Trim());

            try
            {
                target = Path.GetFullPath(target);
                if (string.Equals(target.TrimEnd('\\'), shipped.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return shipped;

                Directory.CreateDirectory(target);
                var copied = new List<string>();
                if (Directory.Exists(shipped))
                {
                    foreach (string file in Directory.GetFiles(shipped, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(shipped.Length).TrimStart('\\');
                        string destination = Path.Combine(target, relative);
                        if (File.Exists(destination))
                            continue;

                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(file, destination);
                        copied.Add(relative);
                    }
                }

                if (copied.Count > 0)
                {
                    Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Info, "Data", "Seed",
                        "Copied the shipped defaults " + target + " did not have: " + string.Join(", ", copied), string.Empty);
                }

                return target;
            }
            catch (Exception ex)
            {
                _startupWarnings.Add("The data folder " + target + " (App.config TealDataDirectory) cannot be used: "
                    + ex.Message + ". The machine's settings are read from and saved to " + shipped + " instead.");
                return shipped;
            }
        }

        /// <summary>
        /// Builds the container. Sections: 1 core services, 2 data access, 3 machine
        /// control and devices, 4 shells, 5 pages, 6 page factory, 7 the window.
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            string dataDirectory = ResolveDataDirectory();

            // Manual > Axis point-to-point limits. Nova.ViewModels is netstandard and cannot
            // reach ConfigurationManager, so the host hands them over.
            ApplyPositiveIntSetting("PtpIterationCount", v => AxisControlViewModel.MaxIterations = v);
            ApplyPositiveIntSetting("PPCIterationLen", v => AxisControlViewModel.MaxIterationDigits = v);
            ApplyPositiveIntSetting("PtpWaitingTime", v => AxisControlViewModel.MaxWaitSeconds = v);
            ApplyPositiveIntSetting("ManualPositionLimit", v => AxisControlViewModel.MaxPosition = v);

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
            var mesClient = new MesClient(SettingOrDefault("MesUrl", string.Empty), terminalName,
                SettingOrDefault("MesStateUrl", string.Empty));
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
            // Recipe > CNC: starts Beckhoff's CNC HMI from where it is installed on this machine.
            services.AddSingleton<ICncHmiLauncher>(sp => new CncHmiLauncher(ConfigurationManager.AppSettings["CncHmiExe"], dataDirectory));
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
            // Machine name / version for the title bar, read from Data\config.txt. Built now,
            // not on first use: it also sets NLog's MachineName for the log rows.
            services.AddSingleton<IMachineIdentityService>(new ConfigFileMachineIdentityService(dataDirectory));
            // Site / line / station / product type for the Audio indicator bar, station
            // card and Settings page (Audio SW DFM v5.4 slides 42, 43, 62). Writable,
            // unlike the read-only identity profile above, so it has its own JSON file.
            services.AddSingleton<IStationProfileService>(sp => new JsonStationProfileService(dataDirectory));

            // Dual language - English, Chinese, Vietnamese. The language is the station
            // profile's, chosen on Setting > General; the strings are Localization\Strings*.resx
            // beside the exe, which Setting > Localization Editor maintains.
            services.AddSingleton<ILocalizationService>(sp => new WpfLocalizationService(sp.GetRequiredService<IStationProfileService>()));
            services.AddSingleton<ILocalizationEditorService, WpfLocalizationEditorService>();

            // Setting > Config Description: I/O point names (Data\Config.csv) and spec limit
            // names (Data\Settings\SpecLimitDescriptions.csv) per language, as Odyssey.Master_V3.
            services.AddSingleton<IDescriptionService>(sp => new WpfDescriptionService(
                dataDirectory,
                sp.GetRequiredService<ILocalizationService>(),
                () => SpecLimitParameters(dataDirectory)));

            // What each gantry is fitted with (valve model and SNs, nozzle, glue, material),
            // recorded by the planned-downtime procedures; and the hash keys of the PD-15
            // page, which HIVE gets with every machine_state payload.
            services.AddSingleton<IConsumableStore>(sp => new JsonConsumableStore(dataDirectory));

            // S11.3 Maintenance Tracking: the maintenance items of each fixture (front / rear gantry).
            services.AddSingleton<IMaintenanceStore>(sp => new JsonMaintenanceStore(dataDirectory));

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

            // 2. TEAL_Master. Every database operation is in Master.Repository; the rest of
            //    the HMI sees only the Nova.Abstractions repository contracts. Without a
            //    connection string there is no database: nothing is registered, and every
            //    page that reads one says its data is not available.
            var database = new Master.Repository.MasterDatabase(ConfigurationManager.AppSettings["connectionString"]);
            if (database.IsAvailable)
            {
                services.AddSingleton(database.ShiftSettings);
                services.AddSingleton(database.AlarmLog);
                services.AddSingleton(database.Production);
                services.AddSingleton(database.ProductionRecorder);
                services.AddSingleton(database.Reports);
            }
            else
            {
                _startupWarnings.Add("No connectionString in App.config, so there is no database: alarms are shown but not "
                    + "saved, and the Home, Alarm and Data pages have no history.");
            }

            // The machine's event records kept as files (glue weighings, measured coordinates).
            string recordFolder = Path.Combine(SettingOrDefault("LogFilePath", @"C:\TEAL_Log_Data"), "MachineData");
            var weighingRecords = new MachineRecordStore(recordFolder, TwincatDispenserDataService.WeighingKind,
                TwincatDispenserDataService.WeighingColumns);
            var coordinateRecords = new MachineRecordStore(recordFolder, PlcUnitDoneWatcher.CoordinateKind,
                PlcUnitDoneWatcher.CoordinateColumns);
            services.AddSingleton(new CurrentShift(database.ShiftSettings));

            // Old records are deleted by Master.Scheduler (started in OnStartup): these CSV
            // files with every other CSV under LogFilePath, the database rows by RetentionDaysFor*.
            _database = database;

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

            // Every PLC variable name the HMI uses is in Data\PlcSymbols.json; each service
            // below is handed the part of the map it needs. Nothing else names a PLC variable.
            PlcSymbolMap plcMap = PlcSymbolMap.Load(Path.Combine(dataDirectory, PlcSymbolMap.FileName), _startupWarnings);
            services.AddSingleton(plcMap);

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

            bool IsIoPoint(string name, bool isOutput)
            {
                foreach (var point in isOutput ? outputPoints : inputPoints)
                    if (string.Equals(point.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }

            // The load cells are wired as digital inputs until their analog values are.
            services.AddSingleton<ILoadCellMonitorService>(sp =>
                new DigitalInputLoadCellMonitorService(sp.GetRequiredService<IDigitalIOService>()));

            var stepperConfigs = ConfigGlobalClass.StepperConfigurations;
            var plc = ConnectPlc();
            services.AddSingleton(plc);

            // Every device status the screens show is an ADS notification on the shared
            // connection: each adapter registers its symbols here, and nothing polls the PLC.
            var subscriptions = new PlcSubscriptions(plc);
            services.AddSingleton(subscriptions);
            services.AddSingleton(new PlcSymbolCheck(plc, subscriptions, plcMap));

            // [HealthData]'s run, down and idle time, from E_MachineState.
            services.AddSingleton(new MachineHealthRecorder(subscriptions, PlcSymbolMap.Of(plcMap.Status.MachineState)));

            // Master.Configuration creates the library object for every device in the
            // Data\*.json files, on the same card as the connection above; the adapters
            // below put them behind the Nova contracts.
            _startupWarnings.AddRange(DeviceObjects.Create());

            // When a screen opens a device the library reads its current status; after that
            // its live status is its HMI display struct: the library says where it is, and
            // App.config names the struct's PLC type for each kind of device - the C#
            // struct of the same name reads it.
            Type servoDisplay = DisplayType<IAxisHmiDisplay>(plcMap, "Servo");
            Type ioStepperDisplay = DisplayType<IAxisHmiDisplay>(plcMap, "IoStepper");
            Type cylinderDisplay = DisplayType<ICylinderHmiDisplay>(plcMap, "Cylinder");
            Type vacuumDisplay = DisplayType<IVacuumHmiDisplay>(plcMap, "Vacuum");
            Type visionDisplay = DisplayType<INetworkHmiDisplay>(plcMap, "Vision");
            Type scannerDisplay = DisplayType<INetworkHmiDisplay>(plcMap, "Scanner");

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
                IsIoPoint,
                _startupWarnings);
            services.AddSingleton<ICylinderService>(new CylinderService(cylinders));

            var vacuums = TwincatDevices.CreateVacuums(
                plc,
                subscriptions,
                ConfigGlobalClass.VacuumConfigurations,
                VacuumObjects.VacuumObjs,
                vacuumDisplay,
                ResolveAddress,
                IsIoPoint,
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
            // The CNC channels' axes (the two dispensing stations): jogged and taught on the
            // Recipe page's CNC tab, through the channel interface named in the symbol map.
            var cncAxes = new TwincatCncAxisProvider(plc, subscriptions, plcMap.Cnc, Nova.Abstractions.Data.Models.CncChannelRecipe.AxisNames);
            services.AddSingleton<ICncAxisProvider>(cncAxes);

            services.AddSingleton<IAxisMonitorService>(new TwincatAxisMonitorService(subscriptions, axes.Concat(cncAxes.Axes), steppers));

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

            // Manual > Dispenser, Weighing and Vibration: the third-party devices the PLC drives
            // over RS-232 / RS-485, each a PLC data block named in the symbol map.
            services.AddSingleton<IThirdPartyDevices>(new TwincatThirdPartyDevices(plc, subscriptions, plcMap.ThirdPartyDevices));

            // The Home page's Homing button: sets the PLC's home command bit TRUE and
            // waits for the PLC's homing-done bit. The PLC owns the homing sequence.
            services.AddSingleton<IHomingService>(new TwincatHomingService(
                plc,
                subscriptions,
                plcMap.Commands.HomingStart,
                plcMap.Status.HomingDone));

            // The Config page's Calibration section: the HMI keeps the taught positions of every
            // calibration (Data\Calibration\*.json) and writes them into the PLC's teach tables;
            // the PLC's calibration manager runs each calibration on Start - except the 5 axis
            // calibration, which the HMI runs itself (Master.Sequences\FiveAxisCalibration) on the
            // machine's servo and CNC axes, with its own Start / Pause / Resume / Stop.
            services.AddSingleton(sp => new TwincatCalibrationService(
                plc,
                subscriptions,
                plcMap.Calibration,
                dataDirectory,
                sp.GetRequiredService<IAxisService>(),
                sp.GetRequiredService<ICncAxisProvider>(),
                plcMap.Cnc));
            services.AddSingleton<ICalibrationService>(sp => sp.GetRequiredService<TwincatCalibrationService>());

            // Calibration > Static & Dynamic: the PLC's Static_Seq / Dynamic_Seq (GVL_StaticDynamic). The
            // HMI picks the mode, camera and iteration count and presses Start / End / Stop.
            services.AddSingleton<IStaticDynamicService>(new TwincatStaticDynamicService(
                plc,
                subscriptions,
                plcMap.Calibration));

            // The Recipe page's gantry recipes, robot values and CNC positions: the PLC drives every
            // taught move from its own copy, one per gantry, and each robot with the values in its
            // FB_RobotSD block. Written on Save, and again each time the PLC comes back.
            services.AddSingleton(sp => new TwincatRecipeService(
                plc,
                subscriptions,
                sp.GetRequiredService<IRecipeService>(),
                PlcSymbolMap.Of(plcMap.Recipe.Gantry),
                MachineConfiguration.Configured<Robots, RobotConfiguration>(ConfigGlobalClass.RobotConfigurations, c => c.EnumName)
                    .Select(r => r.Value.Id)
                    .ToList(),
                PlcSymbolMap.Of(plcMap.Recipe.Robot),
                plcMap.Cnc));
            services.AddSingleton<IPlcRecipeService>(sp => sp.GetRequiredService<TwincatRecipeService>());

            // The indicator bar's Start / Pause / Stop pulse the PLC's mode buttons and follow
            // its machine state; the Setting page's Manual tab holds the PLC in manual mode.
            services.AddSingleton(sp => new TwincatMachineOrchestrator(
                plc,
                subscriptions,
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IHomingService>(),
                plcMap.Status.MachineState,
                plcMap.Commands));
            services.AddSingleton<IMachineOrchestrator>(sp => sp.GetRequiredService<TwincatMachineOrchestrator>());

            // Config > Device Settings: the PLC's run mode, bypass selections and maintenance
            // stations. Saved to the PLC and read back; the tab shows what the PLC reports.
            var machineModeLink = new TwincatMachineModeLink(plc, subscriptions, plcMap.RunMode, plcMap.Bypass, _startupWarnings);
            services.AddSingleton(machineModeLink);
            services.AddSingleton<IMachineModeLink>(machineModeLink);

            // The PLC sequences' logs, into the running log - watched whatever is on screen.
            services.AddSingleton(new PlcSequenceLog(
                subscriptions,
                plcMap.SequenceLog.Entries));

            // The PLC's custom logs: each row the PLC logs is written by its CustomLogs entry.
            // The production log's rows are also saved in [ProductionData] (ProductionLogRecorder);
            // without a database the rows only go to the CSV.
            bool productionLogFeedsDatabase = plcMap.CustomLog.Logs != null
                && plcMap.CustomLog.Logs.Any(l => string.Equals(l.Key, ProductionLogRecorder.LogKey, StringComparison.OrdinalIgnoreCase)
                    && PlcSymbolMap.Has(l.Value));
            services.AddSingleton(sp =>
            {
                IProductionRecorder recorder = sp.GetService<IProductionRecorder>();
                Action<string, object> rowHandler = recorder == null
                    ? (Action<string, object>)null
                    : new ProductionLogRecorder(recorder).OnRow;
                return new PlcCustomLogBridge(plc, subscriptions, CustomLogs.All, plcMap.CustomLog.Logs, rowHandler);
            });

            // Connected = subscribed with the PLC in Run, which the subscriptions track
            // from the PLC's own state notification rather than by asking.
            services.AddSingleton<IConnectionStatusService>(sp => new ConnectionStatusService(
                sp.GetRequiredService<ICommDeviceService>(), "TwinCAT " + plc.Address, () => subscriptions.IsLive));

            // Alarms: read from the PLC's alarm list and saved to the [Alarms] table.
            services.AddSingleton(sp => new AlarmMonitor(new PlcAlarmReader(plc, plcMap.Alarms.List), database.AlarmHistory));
            services.AddSingleton<IActiveAlarmService>(sp => sp.GetRequiredService<AlarmMonitor>());

            // HIVE: the alarms above decide the HIVE machine state, and the alarm
            // pop-up asks the operator to badge in before it closes.
            services.AddSingleton(sp =>
            {
                HiveSettings hiveSettings = BuildHiveSettings(dataDirectory, sp.GetRequiredService<IStationProfileService>(), plcMap);

                // Every hash key of the PD-15 page, read live on each state change
                // (slides 12 and 43), and the remote qualification log while the
                // Setting page has it ON (S2.4).
                IHashKeyService hashes = sp.GetRequiredService<IHashKeyService>();
                IStationProfileService stationProfile = sp.GetRequiredService<IStationProfileService>();
                hiveSettings.ReadHashKeys = () => hashes.ReadAll();
                hiveSettings.RemoteQualification = () => stationProfile.Current.RemoteQualification;
                return hiveSettings;
            });
            services.AddSingleton(sp =>
            {
                var hive = new HiveService(
                    sp.GetRequiredService<HiveSettings>(),
                    sp.GetRequiredService<AlarmMonitor>(),
                    subscriptions);

                // PD-17 follows PD-10 only after a manual weighing: asked of the PLC when chosen.
                if (PlcSymbolMap.Has(plcMap.Glue.Weighing.IsManual?.Symbol))
                    hive.LastGlueWeighingManual = () => PlcValues.Flag(plc, plcMap.Glue.Weighing.IsManual);

                // A downtime the HMI raised itself (hash key error, HIVE upload not confirmed)
                // is also a stop request to the PLC, held until HIVE leaves downtime.
                Action<bool> stopRequest = PlcBoolWriter(plc, plcMap.Process.HmiStopRequest);
                if (stopRequest != null)
                {
                    hive.HmiStopChanged += (s, on) =>
                    {
                        try
                        {
                            stopRequest(on);
                        }
                        catch (Exception ex)
                        {
                            Teal.Library.Log.Logger.WriteLog(Teal.Library.Log.LogType.Error, "Hive", "Stop",
                                "The stop request could not be written to the PLC: " + ex.Message, ex.ToString());
                        }
                    };
                }
                else
                {
                    _startupWarnings.Add("Process.HmiStopRequest is blank in Data\\" + PlcSymbolMap.FileName
                        + ", so a downtime the HMI raises itself (hash key error, HIVE upload not confirmed) does not stop the machine.");
                }

                return hive;
            });
            // S12: a unit HIVE failed or did not confirm goes to the NG conveyor - the PLC is told.
            services.AddSingleton(sp => new UnitUploadNgSignal(
                plc, sp.GetRequiredService<HiveService>(), plcMap.Process, plcMap.Commands.PulseMilliseconds));
            if (!PlcSymbolMap.Has(plcMap.Process.UnitUploadNg))
            {
                _startupWarnings.Add("Process.UnitUploadNg is blank in Data\\" + PlcSymbolMap.FileName
                    + ", so a unit HIVE fails or does not confirm is not sent to the NG conveyor.");
            }

            // Every switch to Running asks MES whether the hash keys are still the approved
            // ones (S3.6); a false answer puts HIVE into downtime.
            services.AddSingleton(sp => new HashKeyGuard(
                sp.GetRequiredService<HiveService>(),
                sp.GetRequiredService<IHashKeyService>(),
                sp.GetRequiredService<IMesClient>(),
                !string.Equals(SettingOrDefault("HiveHashCheckOnMesError", "downtime"), "ignore", StringComparison.OrdinalIgnoreCase)));

            // Every HIVE state change is also reported to MES (S1.1.2).
            services.AddSingleton(sp =>
            {
                IStationProfileService profile = sp.GetRequiredService<IStationProfileService>();
                string machineName = SettingOrDefault("MesMachineName", string.Empty);
                string line = SettingOrDefault("MesLineName", string.Empty);
                return new MesStateReporter(
                    sp.GetRequiredService<HiveService>(),
                    sp.GetRequiredService<IMesClient>(),
                    () => machineName.Length > 0 ? machineName : profile.Current.Station,
                    () => profile.Current.Vendor,
                    () => line.Length > 0 ? line : profile.Current.Line);
            });

            // Finished units, from the PLC's counter: the production row, the measured
            // coordinates, and HIVE's debug-unit and audit counts.
            // Each finished unit's machine_data payload for HIVE (DFM S1.2): what the PLC
            // holds for the unit, the process parameters, the consumables and the limits.
            services.AddSingleton(sp =>
            {
                var unitData = new HiveUnitDataBuilder(
                    plc, plcMap, sp.GetRequiredService<HiveSettings>(),
                    sp.GetRequiredService<IMachineSettingsService>(),
                    sp.GetRequiredService<IConsumableStore>(),
                    sp.GetService<IDispenserDataService>(),
                    terminalName,
                    SettingOrDefault("HiveLimitsVersion", string.Empty));

                // With no finished-unit counter at all, the warning below already says so.
                if (unitData.UnavailableReason != null)
                {
                    if (PlcSymbolMap.Has(plcMap.Status.UnitDoneCount))
                        _startupWarnings.Add(unitData.UnavailableReason);
                }
                else
                    _startupWarnings.AddRange(unitData.SettingWarnings());

                return unitData;
            });
            // T6: 3 NG in a row or 5 in total puts the machine down; an NG below that is a
            // pop-up and the buzzer. Per gantry for single-glue dispensing (App.config HiveNgScope).
            services.AddSingleton(sp =>
            {
                var ng = new NgStatistics(
                    sp.GetRequiredService<HiveService>(),
                    Path.Combine(dataDirectory, "Settings", "NgStatistics.json"),
                    !string.Equals(SettingOrDefault("HiveNgScope", "gantry"), "machine", StringComparison.OrdinalIgnoreCase),
                    IntSetting("HiveNgConsecutiveLimit"),
                    IntSetting("HiveNgTotalLimit"));

                var buzzer = new PlcPulse(plc, plcMap.Process.NgWarningBuzzer, plcMap.Commands.PulseMilliseconds, "The NG warning buzzer");
                ng.Warning += (s, message) =>
                {
                    buzzer.Fire();
                    Current?.Dispatcher.BeginInvoke(new Action(() =>
                        sp.GetRequiredService<IDialogService>().ShowWarning(message, "NG unit")));
                };
                return ng;
            });

            // T5: a finished unit's AOI images are counted and zipped for Insight.
            services.AddSingleton(sp => new UnitImagePackager(
                SettingOrDefault("UnitImageFolder", string.Empty),
                SettingOrDefault("UnitImageZipFolder", string.Empty),
                sp.GetRequiredService<IPictureQuantityCheck>(),
                sp.GetRequiredService<IStationProfileService>(),
                () => sp.GetRequiredService<IAccessControlAdmin>().Settings.ProjectCode));

            // S17: after each unit MES is asked for a common-defect early warning.
            services.AddSingleton(sp => new EarlyWarningService(
                sp.GetRequiredService<IMesClient>(),
                sp.GetRequiredService<INavigationService>(),
                !string.Equals(SettingOrDefault("EarlyWarningClose", "card"), "confirm", StringComparison.OrdinalIgnoreCase),
                PlcBoolWriter(plc, plcMap.Process.EarlyWarningLamp),
                PlcBoolWriter(plc, plcMap.Process.EarlyWarningBuzzer)));
            services.AddSingleton<IEarlyWarning>(sp => sp.GetRequiredService<EarlyWarningService>());

            services.AddSingleton(sp =>
            {
                UnitImagePackager images = sp.GetRequiredService<UnitImagePackager>();
                if (images.UnavailableReason != null && PlcSymbolMap.Has(plcMap.Status.UnitDoneCount))
                    _startupWarnings.Add(images.UnavailableReason);

                EarlyWarningService earlyWarning = sp.GetRequiredService<EarlyWarningService>();
                return new PlcUnitDoneWatcher(
                    plc, subscriptions, sp.GetRequiredService<HiveService>(), plcMap,
                    // The production custom log already saves each unit's row: not twice.
                    productionLogFeedsDatabase ? null : sp.GetService<IProductionRecorder>(), coordinateRecords,
                    sp.GetRequiredService<HiveUnitDataBuilder>(),
                    sp.GetRequiredService<NgStatistics>(),
                    images.Attach,
                    (serial, pass) => earlyWarning.UnitPassed(serial));
            });
            // PD-13 capture deltas and PD-14 calibration results, uploaded while their audit is on.
            services.AddSingleton(sp =>
            {
                var audits = new AuditResultWatcher(plc, subscriptions, sp.GetRequiredService<HiveService>(),
                    sp.GetRequiredService<HiveUnitDataBuilder>(), plcMap);
                audits.Message += (s, message) => Current?.Dispatcher.BeginInvoke(new Action(() =>
                    sp.GetRequiredService<IDialogService>().ShowWarning(message, "Dispensing Pose Calibration (PD-14)")));
                return audits;
            });
            if (!plcMap.Calibration.Repeatability.IsMapped)
            {
                _startupWarnings.Add("Calibration.Repeatability is blank in Data\\" + PlcSymbolMap.FileName
                    + ", so CCD Repeatability Testing (PD-13) only changes the HIVE state: no capture is read and no delta is uploaded.");
            }
            if (!plcMap.Calibration.Pose.IsMapped)
            {
                _startupWarnings.Add("Calibration.Pose is blank in Data\\" + PlcSymbolMap.FileName
                    + ", so Dispensing Pose Calibration (PD-14) only changes the HIVE state: no result is checked or uploaded.");
            }

            if (!PlcSymbolMap.Has(plcMap.Status.UnitDoneCount))
            {
                _startupWarnings.Add("The PLC reports no finished units (Status.UnitDoneCount is blank in Data\\" + PlcSymbolMap.FileName
                    + "), so no production rows or coordinates are recorded, no unit data (machine_data) is sent to HIVE, "
                    + "and HIVE's debug-unit limits and audit rounds do not count.");
            }

            // Data > Axis: the coordinates recorded above, when the PLC measures any.
            if (plcMap.Coordinates.IsMapped)
                services.AddSingleton<ICoordinateDataSource>(new RecordedCoordinateDataSource(coordinateRecords));

            // Data > Glue and Health, Home's glue panel and the glue warnings, when the PLC
            // gives any glue figure. With none, those panels say they are not connected.
            if (plcMap.Glue.IsMapped)
            {
                services.AddSingleton(sp => new TwincatDispenserDataService(
                    plc, subscriptions, plcMap.Glue, sp.GetRequiredService<IConsumableStore>(), weighingRecords, dataDirectory,
                    sp.GetRequiredService<CurrentShift>().Name));
                services.AddSingleton<IDispenserDataService>(sp => sp.GetRequiredService<TwincatDispenserDataService>());
            }

            services.AddSingleton(sp => new AlarmPopupHost(
                sp.GetRequiredService<HiveService>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IAccessControlService>(),
                SettingOrDefault("AlarmPopupCloseLevel", "1") == "2" ? AccessLevel.Level2
                    : SettingOrDefault("AlarmPopupCloseLevel", "1") == "3" ? AccessLevel.Level3
                    : AccessLevel.Level1,
                string.Equals(SettingOrDefault("AlarmPopupClosable", "false"), "true", StringComparison.OrdinalIgnoreCase)));

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
            // S3.1: a valve with an internal SN is read back from its controller every 5 minutes
            // and compared with the recorded one. The controller's SN is the dispenser block's
            // SerialNumber member; the gantries are the dispensers in the same order.
            services.AddSingleton(sp =>
            {
                DeviceBlockSymbols dispensers = plcMap.ThirdPartyDevices.Dispenser ?? new DeviceBlockSymbols();
                DeviceMemberSymbol serialMember = dispensers.Member("SerialNumber");
                List<DeviceInstanceSymbol> instances = dispensers.MappedInstances.ToList();

                Func<int, string> readSerial = null;
                if (serialMember != null && instances.Count > 0)
                {
                    readSerial = gantryIndex =>
                    {
                        if (gantryIndex < 0 || gantryIndex >= instances.Count)
                            throw new InvalidOperationException("no dispenser is mapped for gantry " + (gantryIndex + 1));

                        object value = plc.ReadSymbol(instances[gantryIndex].Path.Trim() + "." + serialMember.Symbol.Trim());
                        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    };
                }

                var monitor = new ValveSerialMonitor(
                    sp.GetRequiredService<HiveService>(),
                    sp.GetRequiredService<IConsumableStore>(),
                    readSerial,
                    IntSetting("ValveSnCheckMinutes"));

                if (readSerial == null && monitor.CheckedGantries.Count > 0)
                {
                    _startupWarnings.Add("The valve SN check every 5 minutes (PD-04) is not available: the dispenser block has no "
                        + "SerialNumber member in Data\\" + PlcSymbolMap.FileName + " (ThirdPartyDevices.Dispenser.Members), but the "
                        + string.Join(" and ", monitor.CheckedGantries) + " valve model has an internal SN.");
                }

                return monitor;
            });

            // S3.3: the first unit after a nozzle change runs only after MES passed the nozzle's health.
            services.AddSingleton(sp => new NozzleHealthGuard(
                sp.GetRequiredService<HiveService>(),
                sp.GetRequiredService<PlannedDowntimeProcedures>()));
            services.AddSingleton<IHiveStatePrompt>(sp => new HiveStatePrompt(
                sp.GetRequiredService<HiveService>(),
                sp.GetRequiredService<IAccessControlService>(),
                sp.GetRequiredService<PlannedDowntimeProcedures>(),
                sp.GetRequiredService<IAppSession>()));

            // Home page: the State Changes chart reads the run / idle / down time from
            // [HealthData] (MachineHealthRecorder), with the segment under way up to now.
            // Without a database it is not registered and the chart says so.
            if (_database != null && _database.IsAvailable)
                services.AddSingleton(_database.CreateStateHistory(Master.SharedUtilities.ShiftStatusHelper.OpenHealthSegment));
            // Home: the Parameter Dashboard's live values and the Sensor Connection
            // Dashboard, from the PLC (Data\Settings\ParameterSymbols.json, Sensors.json).
            services.AddSingleton<IParameterValueSource>(new TwincatParameterValueSource(
                subscriptions, plcMap.Parameters, _startupWarnings));
            string sensorFile = Path.Combine(dataDirectory, "Settings", "Sensors.json");
            if (File.Exists(sensorFile))
            {
                services.AddSingleton<ISensorService>(sp =>
                {
                    try
                    {
                        return new TwincatSensorService(sp.GetRequiredService<IDigitalIOService>(), subscriptions, plc, sensorFile, plcMap.SensorDisable);
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
                PlcBoolWriter(plc, plcMap.Process.PictureDispensingStop)));

            // 4. Shell ViewModels. AudioShellViewModel is the shell the window shows -
            //    this application drives one Audio machine and has one GUI, the DFM's.
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<AuthShellViewModel>();
            services.AddSingleton<AudioShellViewModel>();
            services.AddSingleton<LoginViewModel>();

            // 5. Page ViewModels - the six Audio tabs of the indicator bar. Every figure is
            //    what the machine recorded; a source that is not there is passed as null and
            //    its panel says so.
            services.AddSingleton<AudioAlarmViewModel>();
            services.AddSingleton<AudioDataViewModel>();
            // Vision page: the newest images the vision software saved, from one folder
            // (VisionImageFolder; no sub-folder per camera is needed), newest first.
            // VisionImageCount is how many are shown.
            string visionFolder = SettingOrDefault("VisionImageFolder", string.Empty);
            if (visionFolder.Length > 0)
                services.AddSingleton<ICcdImageSource>(new FolderCcdImageSource(visionFolder));
            services.AddSingleton(sp => new AudioVisionViewModel(
                sp.GetRequiredService<IAppSession>(), sp.GetService<ICcdImageSource>(),
                IntSetting("VisionImageCount") > 0 ? IntSetting("VisionImageCount") : AudioVisionViewModel.DefaultTileCount));
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

            //    Home: the manual GUI is on the Setting page's Manual tab.
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
                errorCodes: sp.GetService<IErrorCodeCatalog>(),
                consumables: sp.GetService<IConsumableStore>(),
                pictures: sp.GetService<IPictureQuantityCheck>()));

            //    Config hosts the machine-configuration tree, the taught recipe and the
            //    calibration positions as the sections of its Machine Configs area (slide 58).
            services.AddSingleton(sp => new AudioConfigViewModel(
                sp.GetRequiredService<IAppSession>(),
                sp.GetRequiredService<IMachineSettingsService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<SettingsViewModel>(),
                sp.GetRequiredService<RecipeViewModel>(),
                sp.GetRequiredService<CalibrationViewModel>()));

            //    The hosted screens themselves. None is routable; each is reached
            //    through the page that hosts it.
            services.AddSingleton<ManualViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<RecipeViewModel>();
            services.AddSingleton(sp => new CalibrationViewModel(
                sp.GetRequiredService<ICalibrationService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetService<IMachineOrchestrator>(),
                sp.GetService<IConnectionStatusService>(),
                sp.GetService<IRobotIOService>(),
                sp.GetService<IStaticDynamicService>()));

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
                            "Not a navigable page. Manual, MachineConfig, Recipe and Calibration are hosted "
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
                    PlcIntWriter(plcConnection, plcMap.Process.GlueAlertLevel),
                    PlcBoolWriter(plcConnection, plcMap.Process.GlueBuzzerMute));
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
        private static Type DisplayType<TDisplay>(PlcSymbolMap map, string kind)
        {
            return HmiDisplays.Resolve<TDisplay>("Devices.HmiDisplayTypes." + kind, map.Devices.HmiDisplayType(kind), _startupWarnings);
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
        private static HiveSettings BuildHiveSettings(string dataDirectory, IStationProfileService profile, PlcSymbolMap plcMap)
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
                MachineStateSymbol = PlcSymbolMap.Of(plcMap.Status.MachineState),
                UnitEnteredSymbol = PlcSymbolMap.Of(plcMap.Status.UnitEntered),
                UnitEnteredIsCounter = plcMap.Status.UnitEnteredIsCounter,
                PendingFile = SettingOrDefault("HivePendingFile", ""),
                UnitTimeout = TimeSpan.FromMilliseconds(Math.Max(500, IntSetting("HiveUnitTimeoutMs") > 0 ? IntSetting("HiveUnitTimeoutMs") : 2000)),
                UnitAttempts = IntSetting("HiveUnitAttempts") > 0 ? IntSetting("HiveUnitAttempts") : 3,
                // S4.1: the SI code UUID.dll and UMSID.dll are called with in the 24-hour dry run.
                DryRunSiCode = SettingOrDefault("HiveDryRunSiCode", ""),
                SoftwareVersion = profile.Current.SoftwareVersion ?? "",
                MainSoftwareHash = profile.ComputeMainSoftwareHash() ?? "",
                LogFolder = string.IsNullOrWhiteSpace(profile.Current.HiveLogPath)
                    ? @"C:\TEAL_Log_Data\HIVE"
                    : profile.Current.HiveLogPath,
            };
        }

        /// <summary>
        /// Writes an HMI-owned BOOL to a PLC variable of the symbol map, or null when the map
        /// leaves it blank (nothing is written then).
        /// </summary>
        private static Action<bool> PlcBoolWriter(TwincatPlcConnection plc, string mappedSymbol)
        {
            string symbol = PlcSymbolMap.Of(mappedSymbol);
            if (symbol.Length == 0 || plc == null)
                return null;

            return value => plc.WriteSymbol(symbol, value);
        }

        /// <summary>As <see cref="PlcBoolWriter"/>, for an HMI-owned INT (16 bit).</summary>
        private static Action<int> PlcIntWriter(TwincatPlcConnection plc, string mappedSymbol)
        {
            string symbol = PlcSymbolMap.Of(mappedSymbol);
            if (symbol.Length == 0 || plc == null)
                return null;

            return value => plc.WriteSymbol(symbol, (short)value);
        }

        /// <summary>A whole-number App.config setting; 0 when blank or not a number.</summary>
        private static int IntSetting(string key)
        {
            int value;
            return int.TryParse(SettingOrDefault(key, "0"), out value) ? value : 0;
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
            _serviceProvider.GetService<PlcCustomLogBridge>()?.Stop();
            _serviceProvider.GetService<IRunningLogService>()?.Stop();
            _serviceProvider.GetService<AlarmPopupHost>()?.Stop();
            _serviceProvider.GetService<HiveService>()?.Stop();
            _serviceProvider.GetService<IActiveAlarmService>()?.Stop();
            _serviceProvider.GetService<IConnectionStatusService>()?.Stop();
            _serviceProvider.GetService<IdleLogoutService>()?.Dispose();
            _serviceProvider.GetService<GlueWarningMonitor>()?.Stop();
            _serviceProvider.GetService<ValveSerialMonitor>()?.Dispose();
            _serviceProvider.GetService<MachineHealthRecorder>()?.Stop();   // writes the segment under way
            Master.Scheduler.ScheduleJobs.Shutdown();

            var stepperService = _serviceProvider.GetService<IStepperService>();
            if (stepperService != null)
            {
                foreach (var stepper in stepperService.GetAllSteppers().OfType<TwincatStepperAdapter>())
                    stepper.StopOnExit();
            }

            // A 5 axis calibration under way is stopped, its axes halted, before the PLC link goes.
            _serviceProvider.GetService<TwincatCalibrationService>()?.StopOnExit();

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
