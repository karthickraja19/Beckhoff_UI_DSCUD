
using Nova.Abstractions.Services;
using Nova.ViewModels.Shells;
using System.ComponentModel;

namespace Nova.Wpf.App
{
    public partial class MainWindow : HandyControl.Controls.Window
    {
        private readonly IAppSession _session;
        private readonly IDialogService _dialog;

        public MainWindow(
            MainWindowViewModel viewModel,
            IAppSession session,
            IDialogService dialog)
        {
            InitializeComponent();

            DataContext = viewModel;
            _session = session;
            _dialog = dialog;
        }

        private void Window_Closing(
            object sender,
            CancelEventArgs e)
        {
            // If no user is logged in,
            // close the application normally.
            if (!_session.IsLoggedIn)
            {
                return;
            }

            // Show the project's existing themed confirmation dialog.
            bool logout = _dialog.Confirm(
                "Do you want to logout?",
                "Logout Confirmation");

            if (logout)
            {
                // Logout the current user.
                _session.Logout();

                // Allow the application to close.
                e.Cancel = true;
            }
            else
            {
                // User selected No.
                // Keep the application open.
                e.Cancel = true;
            }
        }
    }
}

