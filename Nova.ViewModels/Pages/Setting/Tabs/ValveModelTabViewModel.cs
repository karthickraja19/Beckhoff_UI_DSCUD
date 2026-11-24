using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One gantry on the Valve model tab.</summary>
    public partial class GantryValveRow : ObservableObject
    {
        public GantryValveRow(GantryConsumables gantry, IReadOnlyList<string> models)
        {
            Gantry = gantry.Gantry;
            Models = models;
            _valveModel = gantry.ValveModel;
            ValveSn = WithDate(gantry.ValveSn, gantry.ValveChanged);
            ValveUsage = string.IsNullOrWhiteSpace(gantry.ValveSn)
                ? "-"
                : gantry.ValveUnitsProduced + " units, " + gantry.ValveCycles + " cycles, " + gantry.ValveDispenseSeconds + " s dispensing (from MES at the change)";
            NozzleSn = WithDate(gantry.NozzleSn, gantry.NozzleChanged);
            GlueSn = WithDate(gantry.GlueSn, gantry.GlueChanged);
        }

        public string Gantry { get; }

        public IReadOnlyList<string> Models { get; }

        [ObservableProperty]
        private string _valveModel;

        /// <summary>The SN and when it was recorded, e.g. "EFD-PICO-743175_240610 (changed 2026/09/28 10:15)".</summary>
        public string ValveSn { get; }
        public string ValveUsage { get; }
        public string NozzleSn { get; }
        public string GlueSn { get; }

        private static string WithDate(string sn, DateTime? changed)
        {
            if (string.IsNullOrWhiteSpace(sn))
                return "not recorded yet";

            return changed.HasValue ? sn + "   (changed " + changed.Value.ToString("yyyy/MM/dd HH:mm") + ")" : sn;
        }
    }

    /// <summary>
    /// The Valve model tab of the Setting page (S3.1 PD-04 Valve Change, page 5): the
    /// valve model of each gantry, which the valve change checks the scanned SN against.
    /// </summary>
    /// <remarks>
    /// "For Level 1 and Level 2 users access, Gantry# and valve model will show as
    /// read-only. Change Gantry type and valve model will need Level 3 access. Any change
    /// should be selected through the menu bar. Manually key-in is NOT allowed." The SNs
    /// are recorded by the planned-downtime procedures and only shown here.
    /// </remarks>
    public partial class ValveModelTabViewModel : SettingsTabBase
    {
        private readonly IConsumableStore _store;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;
        private bool _loading;

        public ValveModelTabViewModel(IConsumableStore store, IDialogService dialog, IAppSession session, IAccessLog log)
        {
            _store = store;
            _dialog = dialog;
            _session = session;
            _log = log;

            SaveCommand = new RelayCommand(Save, () => CanEditCritical && IsDirty);
            RevertCommand = new RelayCommand(Load, () => IsDirty);

            if (_store != null)
                _store.Changed += (sender, e) => OnUi(() => { if (!IsDirty) Load(); });

            Load();
        }

        public override string Title => "Valve Model";

        public ObservableCollection<GantryValveRow> Gantries { get; } = new ObservableCollection<GantryValveRow>();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
        private bool _isDirty;

        public IRelayCommand SaveCommand { get; }
        public IRelayCommand RevertCommand { get; }

        public string AccessHint => CanEditCritical
            ? "Level 3: choose each gantry's valve model, then Save."
            : "Read only. Changing the valve model needs Level 3.";

        protected override void OnLevelChanged()
        {
            if (!CanEditCritical && IsDirty)
                Load();

            OnPropertyChanged(nameof(AccessHint));
            SaveCommand.NotifyCanExecuteChanged();
        }

        public override void OnShown()
        {
            if (!IsDirty)
                Load();
        }

        public override void OnHidden()
        {
            if (IsDirty)
                Load();
        }

        private void Load()
        {
            foreach (GantryValveRow row in Gantries)
                row.PropertyChanged -= OnRowChanged;

            Gantries.Clear();
            if (_store == null)
                return;

            _loading = true;
            try
            {
                List<string> models = _store.ValveModels.Select(m => m.Name).ToList();
                foreach (GantryConsumables gantry in _store.Gantries)
                {
                    var row = new GantryValveRow(gantry, models);
                    row.PropertyChanged += OnRowChanged;
                    Gantries.Add(row);
                }
            }
            finally
            {
                _loading = false;
            }

            IsDirty = false;
        }

        private void OnRowChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_loading && e.PropertyName == nameof(GantryValveRow.ValveModel))
                IsDirty = true;
        }

        private void Save()
        {
            if (!CanEditCritical || _store == null)
                return;

            foreach (GantryValveRow row in Gantries)
            {
                GantryConsumables gantry = _store.Gantries.FirstOrDefault(g => g.Gantry == row.Gantry);
                if (gantry == null || gantry.ValveModel == row.ValveModel)
                    continue;

                string before = gantry.ValveModel;
                gantry.ValveModel = row.ValveModel ?? string.Empty;
                string error;
                if (!_store.SaveGantry(gantry, out error))
                {
                    _dialog?.ShowError(error, "Valve Model");
                    return;
                }

                if (_log != null && _session != null)
                    _log.Action(_session.UserName, (int)_session.Level, row.Gantry + " valve model changed from " + before + " to " + row.ValveModel);
            }

            Load();
        }
    }
}
