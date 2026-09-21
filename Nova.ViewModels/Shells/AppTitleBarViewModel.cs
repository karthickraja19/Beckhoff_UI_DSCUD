using System;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Shells
{
    public partial class AppTitleBarViewModel : ViewModelBase
    {
        /// <summary>
        /// Machine name shown in the title bar. Populated from the machine profile via
        /// <see cref="IMachineIdentityService"/>; it used to be the literal "AAA", which
        /// named the wrong machine on every install that was not AAA.
        /// </summary>
        [ObservableProperty]
        private string _machineName;

        /// <summary>
        /// Version shown beside the machine name, also from the profile. The title bar
        /// previously carried a hardcoded "Version 5.4" in XAML that no build ever updated.
        /// </summary>
        [ObservableProperty]
        private string _version;

        [ObservableProperty]
        private string _systemTime = string.Empty;

        [ObservableProperty]
        private bool _isModernLayout = true;

        public string LayoutModeText => IsModernLayout ? "Modern Layout" : "Legacy Layout";

        partial void OnIsModernLayoutChanged(bool value)
        {
            OnPropertyChanged(nameof(LayoutModeText));
        }

        private readonly IAppSession _session;
        private readonly IDialogService _dialog;
        private readonly IMachineIdentityService _identity;
        private Timer _timer;

        public string UserName => _session.UserName;

        public AppTitleBarViewModel(IAppSession session, IDialogService dialog = null,
            IMachineIdentityService identity = null)
        {
            _session = session;
            _dialog = dialog;
            _identity = identity;

            // Optional so the designer and any test that news this up without a profile
            // still get something displayable rather than a blank bar.
            _machineName = identity != null ? identity.MachineName : "Unknown";
            _version = identity != null ? identity.DisplayVersion : string.Empty;
            _session.PropertyChanged += (s, e) => 
            {
                if (e.PropertyName == nameof(IAppSession.UserName))
                {
                    OnPropertyChanged(nameof(UserName));
                }
            };

            _timer = new Timer(1000);
            _timer.Elapsed += (s, e) => SystemTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _timer.Start();
            SystemTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        [RelayCommand]
        private void Logout()
        {
            _session.Logout();
        }

        /// <summary>
        /// Title-bar "?" button. It had a tooltip and no command, so it looked pressable
        /// and did nothing. Shows the on-screen quick reference: who is signed in, the
        /// build, and where the toolbar controls live.
        /// </summary>
        [RelayCommand]
        private void ShowHelp()
        {
            if (_dialog == null)
                return;

            string user = string.IsNullOrWhiteSpace(_session.UserName) ? "—" : _session.UserName;
            string role = string.IsNullOrWhiteSpace(_session.Role) ? "—" : _session.Role;

            // Was a hardcoded "Version 5.4"; now the same profile-driven string the title
            // bar shows, so the two can never disagree.
            string build = string.IsNullOrWhiteSpace(Version) ? "Version unknown" : Version;

            _dialog.ShowInfo(
                "TEAL Master — " + build + Environment.NewLine +
                "Machine      : " + MachineName + Environment.NewLine + Environment.NewLine +
                "Signed in as : " + user + "  (" + role + ")" + Environment.NewLine + Environment.NewLine +
                "Toolbar" + Environment.NewLine +
                "  Reset    — clear active alarms and ready the machine." + Environment.NewLine +
                "  Start    — run the auto sequence, or resume when paused." + Environment.NewLine +
                "  Pause    — hold the sequence at the current step." + Environment.NewLine +
                "  Stop     — stop the sequence." + Environment.NewLine +
                "  User     — sign out and switch to another operator." + Environment.NewLine + Environment.NewLine +
                "Pages" + Environment.NewLine +
                "  Home / Alarm / Manual / Settings / Recipe from the pills on the left," + Environment.NewLine +
                "  or the full tree from the sidebar handle at the screen edge." + Environment.NewLine +
                "  Log opens the running-log folder in Windows Explorer." + Environment.NewLine + Environment.NewLine +
                "For machine faults see Alarm -> Alarm; the code shown there is the one" + Environment.NewLine +
                "to quote to maintenance.",
                "Help");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                    _timer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
