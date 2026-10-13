using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Views.Dialogs
{
    /// <summary>
    /// One glue or nozzle alert (S2.4.4.4). Production goes on while it is open; Mute
    /// silences the buzzer, and only a Level 2 or 3 badge closes it.
    /// </summary>
    public partial class GlueAlertWindow : Window
    {
        private readonly IBadgePrompt _badges;
        private bool _closing;

        /// <param name="critical">True for empty glue, expired pot life and critical nozzle health (red).</param>
        public GlueAlertWindow(string title, string message, string detail, bool critical, IBadgePrompt badges)
        {
            InitializeComponent();

            _badges = badges;
            TitleText.Text = title;
            MessageText.Text = message;
            DetailText.Text = detail ?? string.Empty;
            HeaderBand.Background = new SolidColorBrush(critical ? Color.FromRgb(0xEB, 0x73, 0x73) : Color.FromRgb(0xF3, 0xC9, 0x69));
        }

        /// <summary>Raised when the operator mutes the buzzer.</summary>
        public event EventHandler Muted;

        /// <summary>Who closed the alert, for the machine log.</summary>
        public string ClosedBy { get; private set; }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            MuteButton.IsEnabled = false;
            StatusText.Text = "Buzzer muted.";
            Muted?.Invoke(this, EventArgs.Empty);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            AccessCheckResult badge = _badges != null ? _badges.RequestBadge("Close glue alert", AccessLevel.Level2) : null;
            if (_badges != null && badge == null)
                return;

            ClosedBy = badge != null ? badge.Name : string.Empty;
            _closing = true;
            Close();
        }

        /// <summary>Only the Close button, with its badge, closes the alert.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = !_closing;
            base.OnClosing(e);
        }
    }
}
