using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>The health of one maintenance item, or of a whole fixture (its worst item).</summary>
    public enum MaintenanceHealth
    {
        /// <summary>No item, or none maintained yet: grey.</summary>
        Disabled,

        /// <summary>Countdown above the threshold: green.</summary>
        Good,

        /// <summary>Countdown at or below the threshold: yellow.</summary>
        NeedToMaintain,

        /// <summary>Countdown below 0: red.</summary>
        Overdue,
    }

    /// <summary>One fixture on the Maintenance overview.</summary>
    public sealed class MaintenanceFixtureTile
    {
        public MaintenanceFixtureTile(string name, MaintenanceHealth health, int items)
        {
            Name = name;
            Health = health;
            Summary = items == 0 ? "No maintenance item"
                : items + (items == 1 ? " item - " : " items - ") + MaintenanceRow.HealthText(health);
        }

        public string Name { get; }

        public MaintenanceHealth Health { get; }

        public string Summary { get; }

        public bool IsDisabled => Health == MaintenanceHealth.Disabled;
        public bool IsGood => Health == MaintenanceHealth.Good;
        public bool IsWarning => Health == MaintenanceHealth.NeedToMaintain;
        public bool IsOverdue => Health == MaintenanceHealth.Overdue;
    }

    /// <summary>One line of a fixture's maintenance table (S11.3.2).</summary>
    public partial class MaintenanceRow : ObservableObject
    {
        private const string TimeFormat = "yyyy/MM/dd HH:mm";

        private readonly Action _changed;

        public MaintenanceRow(MaintenanceItem item, bool isNew, Action changed)
        {
            _changed = changed;
            IsNew = isNew;
            _project = item.Project ?? string.Empty;
            _line = item.Line ?? string.Empty;
            HeadId = item.HeadId ?? string.Empty;
            _item = item.Item ?? string.Empty;
            _periodText = item.PeriodHours > 0 ? item.PeriodHours.ToString(CultureInfo.InvariantCulture) : string.Empty;
            _thresholdText = item.ThresholdHours > 0 ? item.ThresholdHours.ToString(CultureInfo.InvariantCulture) : string.Empty;
            LatestRecord = item.LatestRecord;
            Refresh(DateTime.Now);
        }

        /// <summary>
        /// True for a line added with Add New Item and not yet accepted by MES. Once MES said
        /// true the line is locked: "to correct an input error, Remove it and create it again".
        /// </summary>
        public bool IsNew { get; private set; }

        public bool IsLocked => !IsNew;

        /// <summary>The "Active" column: the one line the operator is working on.</summary>
        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private string _project;

        [ObservableProperty]
        private string _line;

        /// <summary>The fixture: fixed by the tile the table was opened from.</summary>
        public string HeadId { get; }

        [ObservableProperty]
        private string _item;

        [ObservableProperty]
        private string _periodText;

        [ObservableProperty]
        private string _thresholdText;

        /// <summary>Fixture + item is already in the list: both are shown red and the line cannot be sent.</summary>
        [ObservableProperty]
        private bool _isDuplicate;

        public DateTime? LatestRecord { get; private set; }

        public string LatestRecordText => LatestRecord.HasValue ? LatestRecord.Value.ToString(TimeFormat) : string.Empty;

        public string ScheduledText { get; private set; } = string.Empty;

        /// <summary>Hours to the scheduled time; negative when overdue.</summary>
        public string CountdownText { get; private set; } = string.Empty;

        public MaintenanceHealth Health { get; private set; }

        public string HealthLabel => Health == MaintenanceHealth.Disabled ? string.Empty : HealthText(Health);

        public bool IsOverdue => Health == MaintenanceHealth.Overdue;
        public bool IsWarning => Health == MaintenanceHealth.NeedToMaintain;
        public bool IsGood => Health == MaintenanceHealth.Good;

        public int PeriodHours => Whole(PeriodText);

        public int ThresholdHours => Whole(ThresholdText);

        /// <summary>
        /// Every field a line needs: project, line, item, and a period and threshold that are
        /// positive whole numbers of hours.
        /// </summary>
        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(Project) && !string.IsNullOrWhiteSpace(Line) && !string.IsNullOrWhiteSpace(Item)
            && PeriodHours > 0 && ThresholdHours > 0;

        public static string HealthText(MaintenanceHealth health)
        {
            switch (health)
            {
                case MaintenanceHealth.Overdue: return "Overdue maintenance";
                case MaintenanceHealth.NeedToMaintain: return "Need to maintain";
                case MaintenanceHealth.Good: return "Good health";
                default: return "Not maintained yet";
            }
        }

        /// <summary>The countdown and health as of <paramref name="now"/> (start-up, and every hour on the hour).</summary>
        public void Refresh(DateTime now)
        {
            int period = PeriodHours;
            if (!LatestRecord.HasValue || period <= 0)
            {
                ScheduledText = string.Empty;
                CountdownText = string.Empty;
                Health = MaintenanceHealth.Disabled;
            }
            else
            {
                DateTime scheduled = LatestRecord.Value.AddHours(period);
                double countdown = Math.Floor((scheduled - now).TotalHours);
                ScheduledText = scheduled.ToString(TimeFormat);
                CountdownText = countdown.ToString("0", CultureInfo.InvariantCulture);

                // 5.1 countdown < 0 overdue; 5.2 up to the threshold need to maintain; 5.3 above it good.
                Health = countdown < 0 ? MaintenanceHealth.Overdue
                    : countdown <= ThresholdHours ? MaintenanceHealth.NeedToMaintain
                    : MaintenanceHealth.Good;
            }

            OnPropertyChanged(nameof(LatestRecordText));
            OnPropertyChanged(nameof(ScheduledText));
            OnPropertyChanged(nameof(CountdownText));
            OnPropertyChanged(nameof(Health));
            OnPropertyChanged(nameof(HealthLabel));
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(IsWarning));
            OnPropertyChanged(nameof(IsGood));
        }

        /// <summary>MES accepted the line: it is recorded now, to the hour, and locked.</summary>
        public void Maintained(DateTime now)
        {
            LatestRecord = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            IsNew = false;
            OnPropertyChanged(nameof(IsNew));
            OnPropertyChanged(nameof(IsLocked));
            Refresh(now);
        }

        public MaintenanceItem ToItem()
        {
            return new MaintenanceItem
            {
                Project = (Project ?? string.Empty).Trim(),
                Line = (Line ?? string.Empty).Trim(),
                HeadId = HeadId,
                Item = (Item ?? string.Empty).Trim(),
                PeriodHours = PeriodHours,
                ThresholdHours = ThresholdHours,
                LatestRecord = LatestRecord,
            };
        }

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            switch (e.PropertyName)
            {
                case nameof(Project):
                case nameof(Line):
                case nameof(Item):
                case nameof(PeriodText):
                case nameof(ThresholdText):
                case nameof(IsSelected):
                    _changed?.Invoke();
                    break;
            }
        }

        private static int Whole(string text)
        {
            int value;
            return int.TryParse((text ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
    }

    /// <summary>
    /// The Maintenance tab of the Setting page (S11.3 Maintenance Tracking V1.2, S11.3.2
    /// "for Others": dispensing machines track their front and rear gantry).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Level 2 / 3. The overview has one tile per fixture: grey (nothing tracked), green
    /// (all in date), yellow (an item at or below its threshold), red (an item overdue). A
    /// tile opens its table.</item>
    /// <item>Edit shows only while HIVE is in Daily Maintenance (PD-01) or Weekly Maintenance
    /// (PD-09). One line at a time; "Maint. / Add" and Remove ask MES (<c>EQUIPMENT_MT</c>),
    /// and only MES's true updates the configuration file.</item>
    /// <item>Countdown = scheduled time - now, refreshed at start and every hour on the hour.</item>
    /// <item>Export writes the table as CSV.</item>
    /// </list>
    /// With no MES address configured the MES step is skipped and logged, as the other
    /// procedures do, so a machine on a desk can still be set up.
    /// </remarks>
    public partial class MaintenanceTabViewModel : SettingsTabBase, IDisposable
    {
        public const string Command = "EQUIPMENT_MT";

        private static readonly string[] MaintenanceCodes = { "PD-01", "PD-09" };

        private readonly IMaintenanceStore _store;
        private readonly IMesClient _mes;
        private readonly IHiveControl _hive;
        private readonly ICsvExportService _export;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;
        private readonly IStationProfileService _profile;
        private readonly Func<string> _projectCode;
        private Timer _hourly;

        public MaintenanceTabViewModel(
            IMaintenanceStore store, IMesClient mes, IHiveControl hive, ICsvExportService export,
            IDialogService dialog, IAppSession session, IAccessLog log,
            IStationProfileService profile = null, Func<string> projectCode = null)
        {
            _store = store;
            _mes = mes;
            _hive = hive;
            _export = export;
            _dialog = dialog;
            _session = session;
            _log = log;
            _profile = profile;
            _projectCode = projectCode;

            OpenFixtureCommand = new RelayCommand<MaintenanceFixtureTile>(OpenFixture, tile => tile != null);
            BackCommand = new RelayCommand(Back, () => !IsEditing);
            EditCommand = new RelayCommand(() => IsEditing = true, () => CanEdit && IsAvailable && !IsOverview && !IsEditing && InMaintenanceDowntime);
            CancelCommand = new RelayCommand(Cancel, () => IsEditing);
            ExportCommand = new RelayCommand(Export, () => IsAvailable && !IsOverview && Rows.Count > 0);
            AddCommand = new RelayCommand(AddNewItem, () => IsEditing && !IsBusy);
            MaintainCommand = new AsyncRelayCommand(MaintainAsync, () => CanSubmit);
            RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => IsEditing && !IsBusy && SelectedRow != null);

            if (_hive != null)
                _hive.Changed += OnHiveChanged;
            if (_store != null)
                _store.Changed += OnStoreChanged;

            BuildTiles();
            StartHourlyRefresh();
        }

        public override string Title => "Maintenance";

        public bool IsAvailable => _store != null;

        public ObservableCollection<MaintenanceFixtureTile> Fixtures { get; } = new ObservableCollection<MaintenanceFixtureTile>();

        public ObservableCollection<MaintenanceRow> Rows { get; } = new ObservableCollection<MaintenanceRow>();

        /// <summary>True on the tiles; false inside one fixture's table.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTable))]
        [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ExportCommand))]
        private bool _isOverview = true;

        public bool IsTable => !IsOverview;

        [ObservableProperty]
        private string _selectedFixture = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowEdit), nameof(IsReadOnly))]
        [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(CancelCommand), nameof(AddCommand), nameof(MaintainCommand), nameof(RemoveCommand), nameof(BackCommand))]
        private bool _isEditing;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AddCommand), nameof(MaintainCommand), nameof(RemoveCommand))]
        private bool _isBusy;

        public bool IsReadOnly => !IsEditing;

        /// <summary>The line being worked on: one at a time.</summary>
        public MaintenanceRow SelectedRow => Rows.FirstOrDefault(r => r.IsSelected);

        /// <summary>"Maint. / Add" shows for the selected line once every field is valid and it is not a duplicate.</summary>
        public bool CanSubmit => IsEditing && !IsBusy && SelectedRow != null && SelectedRow.IsComplete && !SelectedRow.IsDuplicate;

        /// <summary>True while HIVE is in PD-01 or PD-09 - or the machine has no HIVE at all.</summary>
        public bool InMaintenanceDowntime
        {
            get
            {
                if (_hive == null || !_hive.IsAvailable)
                    return true;

                string code = _hive.CurrentDowntimeCode;
                return code != null && MaintenanceCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>The Edit button is on the GUI only in PD-01 / PD-09; hidden in every other state.</summary>
        public bool ShowEdit => CanEdit && !IsEditing && InMaintenanceDowntime;

        public string Note => !IsAvailable ? "Maintenance tracking is not available on this machine."
            : InMaintenanceDowntime ? MesNote
            : "Edit shows while HIVE is in Daily Maintenance (PD-01) or Weekly Maintenance (PD-09).";

        private string MesNote => _mes != null && _mes.IsConfigured
            ? "Maint. / Add and Remove are sent to MES (EQUIPMENT_MT); only MES's true is saved."
            : "MES is not configured on this machine (MesUrl in App.config): changes are saved without the MES step.";

        public IRelayCommand<MaintenanceFixtureTile> OpenFixtureCommand { get; }
        public IRelayCommand BackCommand { get; }
        public IRelayCommand EditCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public IRelayCommand ExportCommand { get; }
        public IRelayCommand AddCommand { get; }
        public IAsyncRelayCommand MaintainCommand { get; }
        public IAsyncRelayCommand RemoveCommand { get; }

        protected override void OnLevelChanged()
        {
            if (!CanEdit && IsEditing)
                Cancel();

            RaiseState();
        }

        public override void OnShown()
        {
            RefreshAll();
        }

        public override void OnHidden()
        {
            if (IsEditing)
                Cancel();
        }

        public void Dispose()
        {
            Timer timer = _hourly;
            _hourly = null;
            if (timer != null)
                timer.Dispose();

            if (_hive != null)
                _hive.Changed -= OnHiveChanged;
            if (_store != null)
                _store.Changed -= OnStoreChanged;
        }

        private void OnHiveChanged(object sender, EventArgs e)
        {
            OnUi(() =>
            {
                // Leaving PD-01 / PD-09 ends the edit: nothing unsent is kept.
                if (IsEditing && !InMaintenanceDowntime)
                    Cancel();

                RaiseState();
            });
        }

        private void OnStoreChanged(object sender, EventArgs e)
        {
            OnUi(BuildTiles);
        }

        private void RaiseState()
        {
            OnPropertyChanged(nameof(InMaintenanceDowntime));
            OnPropertyChanged(nameof(ShowEdit));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(SelectedRow));
            EditCommand.NotifyCanExecuteChanged();
            MaintainCommand.NotifyCanExecuteChanged();
            RemoveCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
        }

        // ------------------------------------------------------------------
        // Overview and table
        // ------------------------------------------------------------------

        private void BuildTiles()
        {
            Fixtures.Clear();
            if (_store == null)
                return;

            DateTime now = DateTime.Now;
            IReadOnlyList<MaintenanceItem> items = _store.Items;
            foreach (string fixture in _store.Fixtures)
            {
                List<MaintenanceRow> rows = items
                    .Where(i => string.Equals(i.HeadId, fixture, StringComparison.OrdinalIgnoreCase))
                    .Select(i => new MaintenanceRow(i, false, null))
                    .ToList();

                MaintenanceHealth worst = MaintenanceHealth.Disabled;
                foreach (MaintenanceRow row in rows)
                {
                    row.Refresh(now);
                    if (row.Health > worst)
                        worst = row.Health;
                }

                Fixtures.Add(new MaintenanceFixtureTile(fixture, worst, rows.Count));
            }
        }

        private void OpenFixture(MaintenanceFixtureTile tile)
        {
            if (tile == null)
                return;

            SelectedFixture = tile.Name;
            LoadRows();
            IsOverview = false;
            RaiseState();
        }

        private void Back()
        {
            IsOverview = true;
            Rows.Clear();
            BuildTiles();
            RaiseState();
        }

        private void LoadRows()
        {
            Rows.Clear();
            if (_store == null)
                return;

            foreach (MaintenanceItem item in _store.Items.Where(i => string.Equals(i.HeadId, SelectedFixture, StringComparison.OrdinalIgnoreCase)))
                Rows.Add(new MaintenanceRow(item, false, OnRowChanged));
        }

        /// <summary>Start-up and every hour on the hour: the countdowns, the health and the tiles.</summary>
        private void RefreshAll()
        {
            DateTime now = DateTime.Now;
            foreach (MaintenanceRow row in Rows)
                row.Refresh(now);

            if (IsOverview)
                BuildTiles();

            RaiseState();
        }

        private void StartHourlyRefresh()
        {
            DateTime now = DateTime.Now;
            DateTime nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1).AddSeconds(1);
            _hourly = new Timer(_ => OnUi(RefreshAll), null, nextHour - now, TimeSpan.FromHours(1));
        }

        // ------------------------------------------------------------------
        // Editing
        // ------------------------------------------------------------------

        private bool _selecting;

        /// <summary>A line was ticked or typed into: one selection only, and the duplicate check.</summary>
        private void OnRowChanged()
        {
            if (_selecting)
                return;

            _selecting = true;
            try
            {
                // "Only one maintenance item per operation; multiple selection is forbidden."
                MaintenanceRow newest = Rows.LastOrDefault(r => r.IsSelected && r != _lastSelected) ?? Rows.FirstOrDefault(r => r.IsSelected);
                foreach (MaintenanceRow row in Rows)
                {
                    if (row != newest && row.IsSelected)
                        row.IsSelected = false;
                }

                _lastSelected = newest;

                // 11.1: fixture + item has to be unique.
                foreach (MaintenanceRow row in Rows)
                {
                    string code = Code(row);
                    row.IsDuplicate = row.IsNew && code.Length > 1
                        && Rows.Any(other => other != row && string.Equals(Code(other), code, StringComparison.OrdinalIgnoreCase));
                }
            }
            finally
            {
                _selecting = false;
            }

            RaiseState();
        }

        private MaintenanceRow _lastSelected;

        private static string Code(MaintenanceRow row)
        {
            return (row.HeadId ?? string.Empty).Trim() + "_" + (row.Item ?? string.Empty).Trim();
        }

        /// <summary>A blank line for the fixture, with this machine's project and line filled in.</summary>
        private void AddNewItem()
        {
            StationProfile profile = _profile != null ? _profile.Current : null;
            var row = new MaintenanceRow(new MaintenanceItem
            {
                Project = _projectCode != null ? _projectCode() ?? string.Empty : string.Empty,
                Line = profile != null ? profile.Line ?? string.Empty : string.Empty,
                HeadId = SelectedFixture,
            }, true, OnRowChanged);

            Rows.Add(row);
            row.IsSelected = true;
        }

        /// <summary>"Cancel the operation and leave the edit state": unsent lines are dropped.</summary>
        private void Cancel()
        {
            IsEditing = false;
            _lastSelected = null;
            LoadRows();
            RaiseState();
        }

        private async Task MaintainAsync()
        {
            MaintenanceRow row = SelectedRow;
            if (row == null || !CanSubmit || _store == null)
                return;

            IsBusy = true;
            try
            {
                MaintenanceItem item = row.ToItem();
                MesResult answer = await AskMesAsync(item, true);
                if (answer != null && !ShowAnswer(answer))
                    return;

                // "Collect the system time when Maint. / Add is clicked", to the hour.
                DateTime now = DateTime.Now;
                item.LatestRecord = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

                string error;
                if (!_store.Save(item, out error))
                {
                    _dialog?.ShowError(error, Title);
                    return;
                }

                row.Maintained(now);
                row.IsSelected = false;
                Record((answer == null ? "recorded (no MES) maintenance of " : "recorded maintenance of ") + item.ItemCode);
            }
            finally
            {
                IsBusy = false;
                RaiseState();
            }
        }

        private async Task RemoveAsync()
        {
            MaintenanceRow row = SelectedRow;
            if (row == null || _store == null)
                return;

            // A line MES never had is simply dropped.
            if (row.IsNew)
            {
                Rows.Remove(row);
                OnRowChanged();
                return;
            }

            if (_dialog != null && !_dialog.Confirm("Remove the maintenance item \"" + row.Item + "\" of " + row.HeadId + "?", Title))
                return;

            IsBusy = true;
            try
            {
                MaintenanceItem item = row.ToItem();
                MesResult answer = await AskMesAsync(item, false);
                if (answer != null && !ShowAnswer(answer))
                    return;

                string error;
                if (!_store.Remove(item.HeadId, item.Item, out error))
                {
                    _dialog?.ShowError(error, Title);
                    return;
                }

                Rows.Remove(row);
                Record((answer == null ? "removed (no MES) the maintenance item " : "removed the maintenance item ") + item.ItemCode);
            }
            finally
            {
                IsBusy = false;
                OnRowChanged();
            }
        }

        /// <summary>
        /// EQUIPMENT_MT: serial_Number is fixture_item, empNo and VALUE the signed-in user's
        /// card, MACHINE_CODE project_line, PROCESS the process station id. Null when MES is
        /// not configured (skipped and logged).
        /// </summary>
        private async Task<MesResult> AskMesAsync(MaintenanceItem item, bool enable)
        {
            if (_mes == null || !_mes.IsConfigured)
                return null;

            string card = _session != null ? _session.CardUid ?? string.Empty : string.Empty;
            StationProfile profile = _profile != null ? _profile.Current : null;
            string testData = "MACHINE_CODE=" + item.Project + "_" + item.Line
                + "$PROCESS=" + (profile != null ? profile.Station ?? string.Empty : string.Empty)
                + "$ENABLE=" + (enable ? "Y" : "N")
                + "$VALUE=" + card
                + "$MAX_TIME=" + item.PeriodHours.ToString(CultureInfo.InvariantCulture)
                + "$ALERT_TIME=" + item.ThresholdHours.ToString(CultureInfo.InvariantCulture);

            return await _mes.GetCmdResultAsync(Command, item.ItemCode, testData, "", card);
        }

        /// <summary>The MES feedback dialog: green and saved on true; red and nothing saved on false.</summary>
        private bool ShowAnswer(MesResult answer)
        {
            string text = "MES feedback message\n\n" + (answer.Reached
                ? "{\"Result\":" + (answer.Ok ? "true" : "false") + ",\"RetMsg\":\"" + answer.Message + "\"}"
                : answer.Message);

            if (answer.Reached && answer.Ok)
            {
                _dialog?.ShowInfo(text, Title);
                return true;
            }

            _dialog?.ShowError(text, Title);
            return false;
        }

        private void Export()
        {
            if (_export == null)
            {
                _dialog?.ShowWarning("Export is not available on this machine.", Title);
                return;
            }

            var headers = new[]
            {
                "Active", "Project", "Line type", "Head ID", "Maint. Item", "Maint. Period (h)", "Maint. Countdown (h)",
                "Alarm-triggering Threshold (h)", "Latest maintenance record", "Scheduled Maint. Time", "Health Status",
            };
            List<IReadOnlyList<string>> rows = Rows.Select(r => (IReadOnlyList<string>)new[]
            {
                r.IsSelected ? "Y" : string.Empty, r.Project, r.Line, r.HeadId, r.Item, r.PeriodText, r.CountdownText,
                r.ThresholdText, r.LatestRecordText, r.ScheduledText, r.HealthLabel,
            }).ToList();

            if (_export.Export(headers, rows, "Maintenance_" + SelectedFixture.Replace(' ', '_') + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv"))
                Record("exported the maintenance table of " + SelectedFixture);
        }

        private void Record(string action)
        {
            if (_log != null && _session != null)
                _log.Action(_session.UserName, (int)_session.Level, action);
        }
    }
}
