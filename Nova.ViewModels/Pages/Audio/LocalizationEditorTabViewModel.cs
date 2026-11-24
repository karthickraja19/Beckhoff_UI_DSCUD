using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One editable row of the Localization Editor grid.</summary>
    public partial class LocalizationRow : ObservableObject
    {
        public string Key { get; }

        [ObservableProperty]
        private string _defaultText;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsTranslated))]
        private string _translatedText;

        /// <summary>
        /// False when this key has no text in the language being edited, so it falls back
        /// to English on screen - the translator's to-do list.
        /// </summary>
        public bool IsTranslated => !string.IsNullOrWhiteSpace(TranslatedText);

        public LocalizationRow(string key, string defaultText, string translatedText)
        {
            Key = key;
            _defaultText = defaultText;
            _translatedText = translatedText;
        }
    }

    /// <summary>
    /// Setting page &gt; Localization Editor: the English, Chinese and Vietnamese wording of
    /// every string on screen, maintained on the machine without Visual Studio.
    ///
    /// It writes the loose Strings*.resx files the application reads at runtime, so a
    /// saved correction shows on the screens straight away. Anyone signed in can look;
    /// only Level 3 can change the wording - a wrong word on a safety-relevant button is a
    /// real hazard.
    /// </summary>
    public partial class LocalizationEditorTabViewModel : SettingsTabBase
    {
        private readonly ILocalizationEditorService _editor;
        private readonly ILocalizationService _localization;
        private readonly IDialogService _dialog;

        private readonly List<LocalizationRow> _all = new List<LocalizationRow>();

        public override string Title => "Localization Editor";

        public ObservableCollection<LocalizationRow> Rows { get; } = new ObservableCollection<LocalizationRow>();

        /// <summary>Languages offered in the "editing" combo.</summary>
        public ObservableCollection<LanguageOption> Languages { get; } = new ObservableCollection<LanguageOption>();

        [ObservableProperty]
        private LanguageOption _selectedLanguage;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private bool _showUntranslatedOnly;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanStartEdit), nameof(IsReadOnlyMode))]
        private bool _isEditing;

        [ObservableProperty]
        private string _statusText = string.Empty;

        /// <summary>True when the editor service exists (the language files were found).</summary>
        public bool IsAvailable => _editor != null;

        public bool CanStartEdit => !IsEditing && CanEditCritical && IsAvailable;

        public bool IsReadOnlyMode => !IsEditing;

        public LocalizationEditorTabViewModel(ILocalizationEditorService editor,
            ILocalizationService localization = null,
            IDialogService dialog = null)
        {
            _editor = editor;
            _localization = localization;
            _dialog = dialog;

            if (_editor != null)
            {
                foreach (var language in _editor.EditableLanguages)
                    Languages.Add(language);
            }

            // Open on the language on screen - the one the operator will want to correct.
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
            // Pick up edits made to the files outside the application.
            if (!IsEditing)
                Reload();
        }

        public override void OnHidden()
        {
            if (IsEditing)
                Cancel();
        }

        partial void OnSelectedLanguageChanged(LanguageOption value)
        {
            // Switching the language being edited abandons unsaved work on the previous one.
            if (IsEditing)
                IsEditing = false;

            Reload();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        partial void OnShowUntranslatedOnlyChanged(bool value) => ApplyFilter();

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
                ShowError("Only a Level 3 user can change the wording shown on screen.");
                return;
            }

            IsEditing = true;
        }

        [RelayCommand]
        private void Save()
        {
            if (!IsEditing || _editor == null)
                return;

            try
            {
                _editor.Save(SelectedLanguage == null ? null : SelectedLanguage.Culture, _all
                    .Select(r => new LocalizationEntry
                    {
                        Key = r.Key,
                        DefaultText = r.DefaultText,
                        TranslatedText = r.TranslatedText
                    }));
            }
            catch (Exception ex)
            {
                ShowError(string.Format("The translations could not be saved: {0}", ex.Message));
                return;
            }

            IsEditing = false;
            Reload();
            ShowInfo("Translations saved.");
        }

        /// <summary>Discards unsaved edits by re-reading the files.</summary>
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

            if (_editor != null)
            {
                string culture = SelectedLanguage == null ? null : SelectedLanguage.Culture;

                try
                {
                    foreach (var entry in _editor.Load(culture))
                        _all.Add(new LocalizationRow(entry.Key, entry.DefaultText, entry.TranslatedText));
                }
                catch (Exception ex)
                {
                    ShowError(string.Format("The translation files could not be read: {0}", ex.Message));
                }
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<LocalizationRow> rows = _all;

            if (ShowUntranslatedOnly)
                rows = rows.Where(r => !r.IsTranslated);

            string search = (SearchText ?? string.Empty).Trim();
            if (search.Length > 0)
            {
                // Key and both texts, so a row can be found by the English seen on screen.
                rows = rows.Where(r =>
                    Contains(r.Key, search) ||
                    Contains(r.DefaultText, search) ||
                    Contains(r.TranslatedText, search));
            }

            var filtered = rows.ToList();

            Rows.Clear();
            foreach (var row in filtered)
                Rows.Add(row);

            int untranslated = _all.Count(r => !r.IsTranslated);
            StatusText = string.Format("{0} of {1} keys shown · {2} untranslated",
                filtered.Count, _all.Count, untranslated);
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
