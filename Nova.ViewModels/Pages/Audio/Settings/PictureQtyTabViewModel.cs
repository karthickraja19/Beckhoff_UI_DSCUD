using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One line of Pic_Upload_Record on the tab.</summary>
    public sealed class PictureRecordRow
    {
        public PictureRecordRow(PictureUploadRecord record)
        {
            Time = record.Time.ToString("yyyy/MM/dd HH:mm:ss");
            UnitSn = record.UnitSn;
            Quantity = record.Quantity.ToString();
            Limits = (record.Lsl.HasValue ? record.Lsl.Value.ToString("0.##") : "-") + " / "
                     + (record.Usl.HasValue ? record.Usl.Value.ToString("0.##") : "-");
            Match = record.Match ? "Y" : "N";
            IsMismatch = !record.Match;
        }

        public string Time { get; }
        public string UnitSn { get; }
        public string Quantity { get; }
        public string Limits { get; }
        public string Match { get; }
        public bool IsMismatch { get; }
    }

    /// <summary>
    /// The Picture Qty. Check tab of the Setting page (T5 Audio Picture Upload Quantity
    /// Check, X7).
    /// </summary>
    /// <remarks>
    /// Shown to Level 2 and 3 only. "Dispensing control" drops to 0 by itself when a unit
    /// has fewer pictures than the target; once the image problem is solved, AME/FE sets it
    /// back to 1: Edit needs two-step verification, and Save signs the authorized user out.
    /// A HIVE state change never changes the value.
    /// </remarks>
    public partial class PictureQtyTabViewModel : SettingsTabBase
    {
        private const int RowsShown = 50;

        private readonly IPictureQuantityCheck _check;
        private readonly IBadgePrompt _badges;
        private readonly IDialogService _dialog;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;

        private string _approver;

        public PictureQtyTabViewModel(IPictureQuantityCheck check, IBadgePrompt badges, IDialogService dialog, IAppSession session, IAccessLog log)
        {
            _check = check;
            _badges = badges;
            _dialog = dialog;
            _session = session;
            _log = log;

            EditCommand = new RelayCommand(Edit, () => CanEdit && IsAvailable && !IsEditing && !DispensingEnabled);
            SaveCommand = new RelayCommand(Save, () => IsEditing && EditValue == 1);
            CancelCommand = new RelayCommand(StopEditing, () => IsEditing);

            if (_check != null)
                _check.Changed += (sender, e) => OnUi(Refresh);

            Refresh();
        }

        public override string Title => "Picture Qty. Check";

        public bool IsAvailable => _check != null;

        public bool DispensingEnabled => _check != null && _check.DispensingEnabled;

        /// <summary>The value as T5 shows it: 1 lets the SCUD dispense, 0 stops it.</summary>
        public int DispensingValue => DispensingEnabled ? 1 : 0;

        public string DispensingText => DispensingEnabled
            ? "1 - the SCUD can dispense."
            : "0 - the SCUD will not dispense: a unit had the wrong number of pictures. Solve the image problem, then set it back to 1.";

        public string LastChange => _check != null ? _check.LastChange : string.Empty;

        public string LimitsText => _check != null ? _check.LimitsText : "The picture check is not available on this machine.";

        public string RecordFolder => _check != null ? _check.RecordFolder : string.Empty;

        public ObservableCollection<PictureRecordRow> Records { get; } = new ObservableCollection<PictureRecordRow>();

        public bool HasRecords => Records.Count > 0;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(SaveCommand), nameof(CancelCommand))]
        private bool _isEditing;

        /// <summary>The value being edited; only 1 can be saved (0 is set by the check itself).</summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private int _editValue;

        public int[] EditValues { get; } = { 0, 1 };

        public string VerifiedBy => _approver == null ? string.Empty : "Two-step verification by " + _approver + ".";

        public IRelayCommand EditCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand CancelCommand { get; }

        protected override void OnLevelChanged()
        {
            if (!CanEdit && IsEditing)
                StopEditing();

            EditCommand.NotifyCanExecuteChanged();
        }

        public override void OnShown()
        {
            Refresh();
        }

        public override void OnHidden()
        {
            if (IsEditing)
                StopEditing();
        }

        private void Refresh()
        {
            Records.Clear();
            if (_check != null)
            {
                foreach (PictureUploadRecord record in _check.Recent(RowsShown))
                    Records.Add(new PictureRecordRow(record));
            }

            OnPropertyChanged(nameof(DispensingEnabled));
            OnPropertyChanged(nameof(DispensingValue));
            OnPropertyChanged(nameof(DispensingText));
            OnPropertyChanged(nameof(LastChange));
            OnPropertyChanged(nameof(LimitsText));
            OnPropertyChanged(nameof(HasRecords));
            EditCommand.NotifyCanExecuteChanged();
        }

        private void Edit()
        {
            AccessCheckResult approver = _badges != null ? _badges.RequestSecondApproval("Picture Qty. Check - Edit") : null;
            if (approver == null)
                return;

            _approver = approver.Name;
            OnPropertyChanged(nameof(VerifiedBy));
            EditValue = DispensingValue;
            IsEditing = true;
        }

        private void Save()
        {
            if (_check == null || EditValue != 1 || _approver == null)
                return;

            string operatorName = _session != null ? _session.UserName : string.Empty;
            string error;
            if (!_check.EnableDispensing(operatorName, _approver, out error))
            {
                _dialog?.ShowError(error, "Picture Qty. Check");
                return;
            }

            if (_log != null && _session != null)
                _log.Action(operatorName, (int)_session.Level, "set picture check dispensing control to 1, approved by " + _approver);

            StopEditing();

            // T5: "Click the 'Save' button to save the settings, and Authorized user will
            // automatically log out from the machine software system."
            if (_session != null && _session.IsLoggedIn)
            {
                if (_log != null)
                    _log.Logout(_session.UserName, (int)_session.Level, "automatic - after saving the picture check");
                _session.Logout();
            }
        }

        private void StopEditing()
        {
            IsEditing = false;
            _approver = null;
            OnPropertyChanged(nameof(VerifiedBy));
        }
    }
}
