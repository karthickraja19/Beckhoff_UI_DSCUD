using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Config page - Audio SW DFM v5.4 slide 58.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two thirds of the width is machine configuration, one third is the Spec Limits
    /// table with its Edit and Save buttons. Access, verbatim from the slide:
    /// "Level 1 OSS - view only", "Level 2 OSS - edit authority for non-critical parts
    /// of machine config only", "Level 3 OSS - Can access Edit/Save buttons for
    /// changing machine config".
    /// </para>
    /// <para>
    /// The slide labels the 2/3 area "Machine Configs" and leaves it blank - what a
    /// machine has to be configured with is per-machine. For this machine it is the
    /// machine configuration tree (General, Device, I/O Diagnostics, Axis Configuration,
    /// Vision, AE limits), the taught recipe (gantry positions and robot points) and the
    /// calibration positions. This page only chooses which of them is in front;
    /// nothing about either changes.
    /// </para>
    /// <para>
    /// The Manual screens are NOT here - the manual-operation GUI is the Setting page's
    /// Manual tab.
    /// </para>
    /// </remarks>
    public partial class AudioConfigViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Config;

        private readonly IMachineSettingsService _settings;
        private readonly IDialogService _dialog;


        // ==================================================================
        // Machine configuration (2/3 width)
        // ==================================================================

        /// <summary>
        /// The machine-configuration sections. Each entry carries an existing page view
        /// model; DataTemplates already maps each of those to its view, so the host is a
        /// plain ContentControl.
        /// </summary>
        public ObservableCollection<ConfigSection> Sections { get; } =
            new ObservableCollection<ConfigSection>();

        [ObservableProperty]
        private ConfigSection _selectedSection;

        /// <summary>True while this page is the one on screen.</summary>
        private bool _isOnScreen;

        /// <summary>
        /// Abandons an unsaved edit on the section being left. These view models are DI
        /// singletons, so without this a grid left unlocked - with whatever was typed
        /// into it - was still unlocked on the way back. The section in front is told it
        /// is on screen, so the Gantry tab follows its axes only while it can be seen.
        /// </summary>
        partial void OnSelectedSectionChanged(ConfigSection oldValue, ConfigSection newValue)
        {
            oldValue?.Tab.CancelEdits();

            if (_isOnScreen)
            {
                oldValue?.Page?.OnNavigatedFrom();
                newValue?.Page?.OnNavigatedTo();
            }
        }

        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();

            _isOnScreen = true;
            SelectedSection?.Page?.OnNavigatedTo();
        }

        /// <summary>
        /// Leaving the Config page cancels edits across every section, not just the one
        /// in front - a section switched away from earlier is still holding its own
        /// unsaved state.
        /// </summary>
        public override void OnNavigatedFrom()
        {
            _isOnScreen = false;

            foreach (ConfigSection section in Sections)
            {
                section?.Tab.CancelEdits();
                if (section?.Page != null)
                    section.Page.OnNavigatedFrom();
            }

            base.OnNavigatedFrom();
        }


        // ==================================================================
        // Spec Limits (1/3 width)
        // ==================================================================

        public ObservableCollection<SpecLimitRow> SpecLimits { get; } =
            new ObservableCollection<SpecLimitRow>();

        /// <summary>
        /// The limits table as loaded. Save writes the edited LSL/USL back into a copy
        /// of it, so every other column in the file - serial number, unit, anything
        /// added later - goes back exactly as it came.
        /// </summary>
        private DynamicTableDocument _specLimitDocument;

        /// <summary>
        /// True once Edit has been pressed and until Save or Cancel. Separate from
        /// <see cref="AudioPageViewModel.CanEditCritical"/>, which is only whether the
        /// operator is ALLOWED to edit.
        /// </summary>
        [ObservableProperty]
        private bool _isEditing;

        /// <summary>
        /// Slide 58 puts Edit and Save behind Level 3. Level 2's "non-critical parts of
        /// machine config" is the 2/3 area, not the spec limits - a spec limit is by
        /// definition a critical parameter, so it stays Level 3 here.
        /// </summary>
        public bool CanEditSpecLimits => CanEditCritical && !IsEditing && _specLimitDocument != null;

        public bool CanSaveSpecLimits => CanEditCritical && IsEditing;

        partial void OnIsEditingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanEditSpecLimits));
            OnPropertyChanged(nameof(CanSaveSpecLimits));
            EditCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        protected override void OnAccessLevelChanged(AccessLevel level)
        {
            // Dropping below Level 3 mid-edit abandons the edit rather than leaving an
            // editable panel open to an operator who may no longer change it.
            if (!CanEditCritical && IsEditing)
            {
                IsEditing = false;
                LoadSpecLimits();
            }

            // Same reasoning for the hosted sections: a level drop must not leave an
            // unlocked grid behind in one the operator can no longer edit.
            foreach (ConfigSection section in Sections)
            {
                if (level < section.RequiredLevel)
                    section.Tab.CancelEdits();
            }

            ApplySectionAccess();

            OnPropertyChanged(nameof(CanEditSpecLimits));
            OnPropertyChanged(nameof(CanSaveSpecLimits));
            EditCommand.NotifyCanExecuteChanged();
            SaveCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        public IRelayCommand EditCommand { get; }

        public IRelayCommand SaveCommand { get; }

        public IRelayCommand CancelCommand { get; }

        /// <summary>
        /// What the Spec Limits panel is showing, or why it is empty.
        /// </summary>
        [ObservableProperty]
        private string _specLimitsStatus = string.Empty;

        /// <summary>
        /// Reads the limits from the machine settings service.
        /// </summary>
        /// <remarks>
        /// Sourced from the AE production limits file. This panel is the only place
        /// Production limits are edited; Machine Configs keeps the CPK and GRR sets.
        /// </remarks>
        private void LoadSpecLimits()
        {
            SpecLimits.Clear();
            _specLimitDocument = null;

            try
            {
                DynamicTableDocument document = _settings != null
                    ? _settings.LoadAeLimitsDynamic(AeLimitKind.Production)
                    : null;

                if (document == null || document.Rows == null || document.Rows.Count == 0)
                {
                    SpecLimitsStatus = "No spec limits are configured on this machine.";
                    return;
                }

                _specLimitDocument = document;
                for (int i = 0; i < document.Rows.Count; i++)
                {
                    Dictionary<string, string> row = document.Rows[i];
                    SpecLimits.Add(new SpecLimitRow(
                        i, Cell(row, "Parameter"), Cell(row, "Unit"), Cell(row, "Min"), Cell(row, "Max")));
                }

                SpecLimitsStatus = string.Empty;
            }
            catch (Exception ex)
            {
                SpecLimitsStatus = "Spec limits could not be read: " + ex.Message;
            }
            finally
            {
                OnPropertyChanged(nameof(CanEditSpecLimits));
                EditCommand?.NotifyCanExecuteChanged();
            }
        }

        private static string Cell(Dictionary<string, string> row, string key)
        {
            string value;
            return row != null && row.TryGetValue(key, out value) ? value : string.Empty;
        }

        private void BeginEdit()
        {
            foreach (SpecLimitRow row in SpecLimits)
                row.Error = null;

            IsEditing = true;
        }

        /// <summary>
        /// Slide 58's Save: validates every row, then writes LSL/USL back into the AE
        /// production limits file. The settings service logs each changed value with
        /// the operator's name (slide 39: Level 3 "actions must be recorded by log").
        /// </summary>
        /// <remarks>
        /// A row that fails validation keeps the panel in edit mode with the reason
        /// under the row, so nothing typed is lost. So does a failed write.
        /// </remarks>
        private void SaveEdit()
        {
            // Gate the write, not just the button: a level drop between Edit and Save
            // must not let an edit through.
            if (!CanEditCritical || _settings == null || _specLimitDocument == null)
            {
                CancelEdit();
                return;
            }

            List<SpecLimitRow> invalid = SpecLimits.Where(r => !r.Validate()).ToList();
            if (invalid.Count > 0)
            {
                if (_dialog != null)
                {
                    const int listed = 10;
                    string message = "Correct these limits before saving:" + Environment.NewLine
                        + string.Join(Environment.NewLine, invalid.Take(listed).Select(r => r.KeyName + ": " + r.Error));
                    if (invalid.Count > listed)
                        message += Environment.NewLine + "... and " + (invalid.Count - listed) + " more.";

                    _dialog.ShowError(message, "Spec Limits");
                }
                return;
            }

            if (_dialog != null && !_dialog.Confirm("Save the spec limits?", "Spec Limits"))
                return;

            var rows = new List<Dictionary<string, string>>(_specLimitDocument.Rows.Count);
            foreach (Dictionary<string, string> original in _specLimitDocument.Rows)
                rows.Add(new Dictionary<string, string>(original, StringComparer.OrdinalIgnoreCase));

            foreach (SpecLimitRow row in SpecLimits)
            {
                rows[row.Index]["Min"] = SpecLimitRow.ToStored(row.LowerLimit);
                rows[row.Index]["Max"] = SpecLimitRow.ToStored(row.UpperLimit);
            }

            RecipeSaveResult result;
            try
            {
                result = _settings.SaveAeLimitsDynamic(AeLimitKind.Production, new DynamicTableDocument
                {
                    Title = _specLimitDocument.Title,
                    Columns = _specLimitDocument.Columns,
                    Rows = rows
                });
            }
            catch (Exception ex)
            {
                if (_dialog != null)
                    _dialog.ShowError("The spec limits could not be saved: " + ex.Message, "Spec Limits");
                return;
            }

            IsEditing = false;
            LoadSpecLimits();

            if (_dialog != null)
                _dialog.ShowInfo(result.IsNoChanges ? "No limits were changed." : "Spec limits saved.", "Spec Limits");
        }

        private void CancelEdit()
        {
            IsEditing = false;
            LoadSpecLimits();
        }

        public AudioConfigViewModel(
            IAppSession session,
            IMachineSettingsService settings = null,
            IDialogService dialog = null,
            PageViewModel machineConfig = null,
            PageViewModel recipe = null,
            PageViewModel calibration = null)
            : base(session, AccessBypass.ConfigAndSettings)
        {
            _settings = settings;
            _dialog = dialog;

            EditCommand = new RelayCommand(BeginEdit, () => CanEditSpecLimits);
            SaveCommand = new RelayCommand(SaveEdit, () => CanSaveSpecLimits);
            CancelCommand = new RelayCommand(CancelEdit, () => IsEditing);

            // Slide 58 splits edit authority: Level 2 gets "non-critical parts of machine
            // config only", Level 3 gets the rest. The machine-configuration tree is the
            // non-critical half - shift times, I/O diagnostics, device selection. Recipe
            // is taught positions, where a wrong number drives the gantry into something,
            // so it is Level 3 alongside the spec limits.
            if (machineConfig != null)
                Sections.Add(new ConfigSection("Machine Configs", "Settings2", machineConfig, AccessLevel.Level2));
            if (recipe != null)
                Sections.Add(new ConfigSection("Recipe", "FileText", recipe, AccessLevel.Level3));
            // Calibration is taught positions too, and its Start moves the machine.
            if (calibration != null)
                Sections.Add(new ConfigSection("Calibration", "Target", calibration, AccessLevel.Level3));

            _selectedSection = Sections.FirstOrDefault();

            // Spec limit names follow the selected language and Setting > Config Description.
            UiLanguage.DescriptionsChanged += (sender, e) =>
            {
                if (!IsEditing && SpecLimits.Count > 0)
                    LoadSpecLimits();
            };
            ApplySectionAccess();

            LoadSpecLimits();
        }

        /// <summary>
        /// Pushes the current operator level onto every section, so each one can grey
        /// itself out without knowing anything about the session.
        /// </summary>
        private void ApplySectionAccess()
        {
            foreach (ConfigSection section in Sections)
                section.IsEditable = AccessLevel >= section.RequiredLevel;
        }
    }
}
