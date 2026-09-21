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
    /// Vision, AE limits, Calibration) and the taught recipe (gantry positions, robot
    /// points, light control). This page only chooses which of the two is in front;
    /// nothing about either changes.
    /// </para>
    /// <para>
    /// The Manual screens are NOT here - slide 57 puts the manual-operation GUI in the
    /// Home page's Vendor tab, which is where it lives.
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

        /// <summary>
        /// Abandons an unsaved edit on the section being left. These view models are DI
        /// singletons, so without this a grid left unlocked - with whatever was typed
        /// into it - was still unlocked on the way back.
        /// </summary>
        partial void OnSelectedSectionChanged(ConfigSection oldValue, ConfigSection newValue)
        {
            oldValue?.Tab.CancelEdits();
        }

        /// <summary>
        /// Leaving the Config page cancels edits across every section, not just the one
        /// in front - a section switched away from earlier is still holding its own
        /// unsaved state.
        /// </summary>
        public override void OnNavigatedFrom()
        {
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

        public ObservableCollection<ParameterRow> SpecLimits { get; } =
            new ObservableCollection<ParameterRow>();

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
        public bool CanEditSpecLimits => CanEditCritical && !IsEditing;

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
        /// Sourced from the AE production limits, which is where this machine already
        /// keeps its per-parameter LSL/USL pairs - the same rows the existing AE limits
        /// screen edits. Reusing them means the Config page and that screen cannot show
        /// different limits for the same parameter.
        /// </remarks>
        private void LoadSpecLimits()
        {
            SpecLimits.Clear();

            if (_settings == null)
            {
                SpecLimitsStatus = "No spec limits are configured on this machine.";
                return;
            }

            try
            {
                IReadOnlyList<AeLimitRecord> records = _settings.LoadAeLimits(AeLimitKind.Production);

                if (records == null || records.Count == 0)
                {
                    SpecLimitsStatus = "No spec limits are configured on this machine.";
                    return;
                }

                foreach (AeLimitRecord record in records)
                    SpecLimits.Add(ToRow(record));

                SpecLimitsStatus = string.Empty;
            }
            catch (Exception ex)
            {
                SpecLimitsStatus = "Spec limits could not be read: " + ex.Message;
            }
        }

        /// <summary>
        /// Projects one AE limit record onto the page's Key Name / LSL / USL row.
        /// </summary>
        /// <remarks>
        /// <see cref="AeLimitRecord"/> stores Min and Max as strings, because the
        /// underlying profile allows a blank for "no limit on this side". A blank or
        /// unparseable bound becomes null, which <see cref="ParameterRow"/> renders as
        /// "N/A" and - importantly - never treats as a bound that a value can fall
        /// outside of.
        /// <para>
        /// The record carries no measured value, only the limits, so the Value column is
        /// blank here. That also means no row on this panel can be out of spec, which is
        /// correct: Spec Limits states the limits, the Home page's Parameter Dashboard
        /// is the one that compares readings against them.
        /// </para>
        /// </remarks>
        private static ParameterRow ToRow(AeLimitRecord record)
        {
            string name = string.IsNullOrWhiteSpace(record.Parameter) ? "(unnamed)" : record.Parameter.Trim();

            if (!string.IsNullOrWhiteSpace(record.Unit))
                name += " (" + record.Unit.Trim() + ")";

            return new ParameterRow(name, string.Empty, ParseLimit(record.Min), ParseLimit(record.Max));
        }

        private static double? ParseLimit(string text)
        {
            double parsed;
            return double.TryParse(text, out parsed) ? parsed : (double?)null;
        }

        private void BeginEdit()
        {
            IsEditing = true;
        }

        /// <summary>
        /// Slide 58's Save. Writing limits back is not wired: the AE limits screen owns
        /// that write path and duplicating it here would give the machine two places
        /// that persist the same rows with different validation. Rather than accept an
        /// edit and silently drop it, this says so and reloads.
        /// </summary>
        private void SaveEdit()
        {
            IsEditing = false;
            LoadSpecLimits();

            if (_dialog != null)
            {
                _dialog.ShowInfo(
                    "Editing spec limits from this panel is not wired up yet. Change them in "
                    + "Machine Configs > SFC & PDCA Settings > AE limits (Production), on the "
                    + "left of this page - that is the existing write path for these rows.",
                    "Spec Limits");
            }
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
            PageViewModel recipe = null)
            : base(session)
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

            _selectedSection = Sections.FirstOrDefault();
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
