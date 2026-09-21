using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;

namespace Nova.ViewModels.Shells
{
    /// <summary>
    /// One of the six menu buttons on the left of the indicator bar (Audio SW DFM
    /// v5.4 slide 42), and the rules that decide which of its four artwork states
    /// shows.
    /// </summary>
    /// <remarks>
    /// The state is computed here rather than in XAML triggers because the DFM's rules
    /// are access-control rules, not visual-tree facts: slide 39 says Config and
    /// Setting are "No login: Disabled / L1, 2, 3: Enabled" while Home, Alarm, Data
    /// and Vision are "Always be enabled / Read only", and slide 42 repeats it as
    /// "Home, alert, data and vision buttons are enabled. The rest of the buttons are
    /// disabled before login".
    /// </remarks>
    public partial class AudioNavItem : ObservableObject
    {
        /// <summary>Page this button opens.</summary>
        public AppPageNames Key { get; }

        /// <summary>
        /// Artwork family - the file-name stem of the DFM icon set (Home, Alarm,
        /// Config, Data, Vision, Setting).
        /// </summary>
        public string Glyph { get; }

        /// <summary>Accessible name and tooltip. Never drawn as a label: the bar is icon-only.</summary>
        public string Label { get; }

        /// <summary>
        /// True for the two buttons the DFM disables before login (Config, Setting).
        /// </summary>
        public bool RequiresLogin { get; }

        /// <summary>This is the page currently showing.</summary>
        [ObservableProperty]
        private bool _isActive;

        /// <summary>
        /// An operator is signed in. Only consulted when <see cref="RequiresLogin"/>
        /// is set.
        /// </summary>
        [ObservableProperty]
        private bool _isLoggedIn;

        /// <summary>
        /// At least one error is standing. Alarm button only - slide 39: "Turn to
        /// Alarmed when an error occurs".
        /// </summary>
        [ObservableProperty]
        private bool _isAlerted;

        public AudioNavItem(AppPageNames key, string glyph, string label, bool requiresLogin)
        {
            Key = key;
            Glyph = glyph;
            Label = label;
            RequiresLogin = requiresLogin;
        }

        /// <summary>Whether the button can be pressed at the current access level.</summary>
        public bool IsPermitted => !RequiresLogin || IsLoggedIn;

        /// <summary>Which of the four DFM appearances to draw.</summary>
        public AudioIconState IconState
        {
            get
            {
                if (!IsPermitted)
                    return AudioIconState.Disabled;

                // Alerted outranks energized on purpose. The bell has to read as
                // alarmed whenever a fault is standing, including while the operator
                // is already looking at the alarm page - otherwise opening the page
                // would make the one urgent signal on the bar disappear.
                if (IsAlerted)
                    return AudioIconState.Alerted;

                return IsActive ? AudioIconState.Energized : AudioIconState.DeEnergized;
            }
        }

        partial void OnIsActiveChanged(bool value) => RaiseDerived();
        partial void OnIsAlertedChanged(bool value) => RaiseDerived();

        partial void OnIsLoggedInChanged(bool value)
        {
            OnPropertyChanged(nameof(IsPermitted));
            RaiseDerived();
        }

        private void RaiseDerived() => OnPropertyChanged(nameof(IconState));
    }
}
