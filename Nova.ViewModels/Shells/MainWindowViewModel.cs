using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Shells
{
    public partial class MainWindowViewModel : ViewModelBase, IRecipient<ShowLoginMessage>
    {
        private readonly IAppSession _session;
        private readonly AuthShellViewModel _authShell;
        private readonly AudioShellViewModel _audioShell;
        private readonly INavigationService _navigationService;
        private readonly IStationProfileService _profile;

        [ObservableProperty]
        private ViewModelBase? _currentView;

        /// <summary>
        /// Window caption. Audio SW DFM v5.4 slide 43 puts the SW version string there,
        /// in the form <c>AAA_X.X.X.X_YYMMDD_TYPE</c>, and slide 42 repeats it across
        /// every mockup. Read from the station profile so the title and the station
        /// card can never disagree.
        /// </summary>
        [ObservableProperty]
        private string _windowTitle = "Audio/Home";

        public MainWindowViewModel(
            IAppSession session,
            AuthShellViewModel authShell,
            AudioShellViewModel audioShell,
            INavigationService navigationService,
            IStationProfileService profile = null)
        {
            _session = session;
            _authShell = authShell;
            _audioShell = audioShell;
            _navigationService = navigationService;
            _profile = profile;

            ApplyWindowTitle();
            if (_profile != null)
                _profile.ProfileChanged += OnProfileChanged;

            _session.PropertyChanged += OnSessionPropertyChanged;

            // The Audio GUI is available before login, so the shell - not the login
            // screen - is what the window opens on. The login screen is now a detour
            // reached from the access-level chip, not the gate in front of everything.
            WeakReferenceMessenger.Default.Register(this);
            ShowShell();
        }

        /// <summary>
        /// Swaps to the login screen when the indicator bar's access-level chip is
        /// clicked while signed out (DFM slide 44).
        /// </summary>
        public void Receive(ShowLoginMessage message)
        {
            CurrentView = _authShell;
        }

        private void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IAppSession.IsLoggedIn))
            {
                // Both directions land on the shell. Signing in returns from the login
                // detour; signing out drops to the DFM's no-login state, which is still
                // a usable read-only GUI (slide 42: "Home, alert, data and vision
                // buttons are enabled") rather than a locked screen.
                ShowShell();
            }
        }

        /// <summary>
        /// Shows the indicator bar, and makes sure a page is loaded under it.
        /// </summary>
        /// <remarks>
        /// Navigation is only forced when nothing is loaded yet. Re-navigating to Home
        /// on every sign-in and sign-out would throw away the page the operator was on
        /// just because someone badged in, which the DFM never asks for - the access
        /// level changes what the page allows, not which page is open.
        /// </remarks>
        private void ShowShell()
        {
            CurrentView = _audioShell;

            if (_navigationService.CurrentPage == null)
                _navigationService.NavigateTo(AppPageNames.Home);
        }

        private void OnProfileChanged(object sender, EventArgs e) => ApplyWindowTitle();

        private void ApplyWindowTitle()
        {
            StationProfile profile = _profile != null ? _profile.Current : null;

            WindowTitle = profile != null && !string.IsNullOrWhiteSpace(profile.SoftwareVersion)
                ? profile.SoftwareVersion
                : "Audio/Home";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _session.PropertyChanged -= OnSessionPropertyChanged;
                WeakReferenceMessenger.Default.Unregister<ShowLoginMessage>(this);

                if (_profile != null)
                    _profile.ProfileChanged -= OnProfileChanged;
            }
            base.Dispose(disposing);
        }
    }
}
