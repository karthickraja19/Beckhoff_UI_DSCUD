using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One row of the Config Description grid.</summary>
    public partial class ConfigDescriptionRow : ObservableObject
    {
        public string Key { get; }
        public string Category { get; }
        public string Context { get; }

        [ObservableProperty]
        private string _english;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsDescribed))]
        private string _translation;

        /// <summary>False when the language being edited has no text for this row.</summary>
        public bool IsDescribed => !string.IsNullOrWhiteSpace(Translation);

        public ConfigDescriptionRow(string key, string category, string context, string english, string translation)
        {
            Key = key;
            Category = category;
            Context = context;
            _english = english;
            _translation = translation;
        }
    }

    /// <summary>
    /// Setting page &gt; Config Description (as Odyssey.Master_V3): the names the I/O
    /// Diagnostics tab shows for each input and output, and the Config page's Spec Limits
    /// panel for each parameter, in English, Chinese and Vietnamese.
    ///
    /// Written straight to the files the screens read (Data\Config.csv and
    /// Data\Settings\SpecLimitDescriptions.csv), so a saved name shows at once. Anyone
    /// signed in can look; only Level 3 can change them.
    /// </summary>
    public partial class ConfigDescriptionTabViewModel : SettingsTabBase
    {
        private const string AllTypes = "All";

        private readonly IDescriptionService _descriptions;
        private readonly ILocalizationService _localization;
        private readonly IDialogService _dialog;
        private readonly List<ConfigDescriptionRow> _all = new List<ConfigDescriptionRow>();

        public override string Title => "Config Description";

        public ObservableCollection<ConfigDescriptionRow> Rows { get; } = new ObservableCollection<ConfigDescriptionRow>();

        /// <summary>I/O Points, Spec Limits.</summary>
        public ObservableCollection<string> Sources { get; } = new ObservableCollection<string>();

        public ObservableCollection<LanguageOption> Languages { get; } = new ObservableCollection<LanguageOption>();

        /// <summary>All, then Input / Output (or the limit sets).</summary>
        public ObservableCollection<string> Types { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _selectedSource;

        [ObservableProperty]
        private LanguageOption _selectedLanguage;

        [ObservableProperty]
        private string _selectedType = AllTypes;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _showUndescribedOnly;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartEdit), nameof(IsReadOnlyMode))]
        private bool _isEditing;

        [ObservableProperty]
        private string _statusText = string.Empty;

        [ObservableProperty]
        private string _filePath = string.Empty;

        public bool IsAvailable => _descriptions != null;

        public bool CanStartEdit => !IsEditing && CanEditCritical && IsAvailable;

        public bool IsReadOnlyMode => !IsEditing;

        public ConfigDescriptionTabViewModel(IDescriptionService descriptions,
            ILocalizationService localization = null,
            IDialogService dialog = null)
        {
            _descriptions = descriptions;
            _localization = localization;
            _dialog = dialog;

            if (_descriptions != null)
            {
                foreach (string source in _descriptions.Sources)
                    Sources.Add(source);
            }
            _selectedSource = Sources.FirstOrDefault();

            if (_localization != null)
            {
                foreach (LanguageOption language in _localization.Available)
                    Languages.Add(language);
            }

            string current = localization != null ? localization.CurrentCulture : null;
            _selectedLanguage = Languages.FirstOrDefault(l =>
                                    string.Equals(l.Culture, current, StringComparison.OrdinalIgnoreCase))
                                ?? Languages.FirstOrDefault(l => !string.Equals(l.Culture, "en", StringComparison.OrdinalIgnoreCase))
                                ?? Languages.FirstOrDefault();

            Reload();
        }

        protected override void OnLevelChanged()
        {
            OnPropertyChanged(nameof(CanStartEdit));
            if (!CanEditCritical && IsEditing)
                Cancel();
        }

        public override void OnShown()
        {
            if (!IsEditing)
                Reload();
        }

        public override void OnHidden()
        {
            if (IsEditing)
                Cancel();
        }

        partial void OnSelectedSourceChanged(string value)
        {
            IsEditing = false;
            SelectedType = AllTypes;
            Reload();
        }

        partial void OnSelectedLanguageChanged(LanguageOption value)
        {
            IsEditing = false;
            Reload();
        }

        partial void OnSelectedTypeChanged(string value) => ApplyFilter();

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        partial void OnShowUndescribedOnlyChanged(bool value) => ApplyFilter();

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
        }

        [RelayCommand]
        private void Edit()
        {
            if (!CanEditCritical)
            {
                ShowError("Only a Level 3 user can change these descriptions.");
                return;
            }

            IsEditing = true;
        }

        [RelayCommand]
        private void Save()
        {
            if (!IsEditing || _descriptions == null)
                return;

            try
            {
                _descriptions.Save(SelectedSource, SelectedLanguage == null ? null : SelectedLanguage.Culture,
                    _all.Select(r => new DescriptionEntry
                    {
                        Key = r.Key,
                        Category = r.Category,
                        Context = r.Context,
                        English = r.English,
                        Translation = r.Translation,
                    }));
            }
            catch (Exception ex)
            {
                ShowError(string.Format("The descriptions could not be saved: {0}", ex.Message));
                return;
            }

            IsEditing = false;
            Reload();
            ShowInfo("Descriptions saved.");
        }

        [RelayCommand]
        private void Cancel()
        {
            IsEditing = false;
            Reload();
        }

        [RelayCommand]
        private void Reload()
        {
            _all.Clear();

            if (_descriptions != null && SelectedSource != null)
            {
                try
                {
                    FilePath = _descriptions.FilePathOf(SelectedSource);
                    foreach (DescriptionEntry entry in _descriptions.Load(SelectedSource, SelectedLanguage == null ? null : SelectedLanguage.Culture))
                        _all.Add(new ConfigDescriptionRow(entry.Key, entry.Category, entry.Context, entry.English, entry.Translation));
                }
                catch (Exception ex)
                {
                    ShowError(string.Format("The descriptions could not be read: {0}", ex.Message));
                }
            }

            string type = SelectedType;
            Types.Clear();
            Types.Add(AllTypes);
            foreach (string category in _all.Select(r => r.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase))
                Types.Add(category);
            _selectedType = Types.Contains(type) ? type : AllTypes;
            OnPropertyChanged(nameof(SelectedType));

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<ConfigDescriptionRow> rows = _all;

            if (!string.IsNullOrEmpty(SelectedType) && SelectedType != AllTypes)
                rows = rows.Where(r => string.Equals(r.Category, SelectedType, StringComparison.OrdinalIgnoreCase));

            if (ShowUndescribedOnly)
                rows = rows.Where(r => !r.IsDescribed);

            string search = (SearchText ?? string.Empty).Trim();
            if (search.Length > 0)
            {
                rows = rows.Where(r =>
                    Contains(r.Key, search) || Contains(r.Context, search) ||
                    Contains(r.English, search) || Contains(r.Translation, search));
            }

            var filtered = rows.ToList();
            Rows.Clear();
            foreach (ConfigDescriptionRow row in filtered)
                Rows.Add(row);

            StatusText = string.Format("{0} of {1} rows shown · {2} without a description",
                filtered.Count, _all.Count, _all.Count(r => !r.IsDescribed));
        }

        private static bool Contains(string haystack, string needle)
        {
            return !string.IsNullOrEmpty(haystack) &&
                   haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ShowError(string message)
        {
            if (_dialog != null)
                _dialog.ShowError(message, Title);
        }

        private void ShowInfo(string message)
        {
            if (_dialog != null)
                _dialog.ShowInfo(message, Title);
        }
    }
}
