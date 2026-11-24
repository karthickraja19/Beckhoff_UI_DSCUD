using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nova.Hive;
using Nova.Wpf.UI.Localization;

namespace Nova.Wpf.App.Views.Dialogs
{
    /// <summary>
    /// S17: the card window of the early warning. The permission "must be obtained from MES"
    /// - the card is read here and handed to the check; nothing is looked up locally.
    /// Background R255 G149 B202, as the spec defines for the permission request pop-up.
    /// </summary>
    public sealed class MesCardDialog : Window
    {
        /// <summary>S17: "background colour of the permission request pop-up: R=255, G=149, B=202".</summary>
        public static readonly Color PermissionBackground = Color.FromRgb(255, 149, 202);

        private readonly Func<string, Task<string>> _check;
        private readonly CardScan _scan = new CardScan();
        private readonly TextBox _cardBox;
        private readonly TextBlock _status;
        private bool _busy;

        /// <param name="check">Checks the card with MES; returns why it is refused, or null to accept it.</param>
        public MesCardDialog(string header, string prompt, Func<string, Task<string>> check)
        {
            _check = check;

            Title = Tr(header);
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.ToolWindow;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            Background = new SolidColorBrush(PermissionBackground);

            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            panel.Children.Add(new TextBlock { Text = Tr(header), FontWeight = FontWeights.Bold, FontSize = 16, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = Tr(prompt), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });

            // Slide 37: the card comes from the reader - no typing, no paste.
            _cardBox = new TextBox { IsReadOnly = true, Padding = new Thickness(6, 4, 6, 4) };
            _cardBox.PreviewTextInput += (sender, e) => { e.Handled = true; _scan.Add(e.Text); };
            _cardBox.PreviewKeyDown += CardBox_PreviewKeyDown;
            CardScan.BlockManualEntry(_cardBox);
            panel.Children.Add(_cardBox);

            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Text = Tr("Swipe the ID card.") };
            panel.Children.Add(_status);

            var cancel = new Button { Content = Tr("Cancel"), MinWidth = 92, Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsCancel = true };
            panel.Children.Add(cancel);

            Content = panel;
            Loaded += (sender, e) => _cardBox.Focus();
        }

        /// <summary>The card MES accepted.</summary>
        public string CardUid { get; private set; }

        private async void CardBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            e.Handled = true;
            if (_busy)
                return;

            _cardBox.Text = string.Empty;
            string refused;
            string card = _scan.Finish(out refused);
            if (card == null)
            {
                _status.Text = Tr(refused ?? "Swipe the ID card.");
                return;
            }

            _busy = true;
            _status.Text = Tr("Checking the card with MES...");
            string failed;
            try
            {
                failed = _check != null ? await _check(card) : null;
            }
            catch (Exception ex)
            {
                failed = "The card could not be checked: " + ex.Message;
            }

            _busy = false;
            if (!IsLoaded)
                return;

            if (failed != null)
            {
                _status.Text = Tr(failed) + " " + Tr("Please swipe again.");
                return;
            }

            CardUid = card;
            DialogResult = true;
        }

        private static string Tr(string english)
        {
            return LocalizationSource.Instance.Translate(english);
        }
    }

    /// <summary>
    /// S17: the early-warning pop-up. Always on top of every window, with MES's own text; the
    /// machine keeps running. Mute silences the buzzer. On an AE / HIVE station Confirm asks
    /// for the CM's card, checked with MES ("UID,Close"); on a URP / ME station Confirm closes it.
    /// </summary>
    public sealed class EarlyWarningWindow : Window
    {
        private readonly bool _needsCard;
        private readonly Func<string, Task<string>> _checkCard;
        private readonly Button _mute;
        private readonly TextBlock _status;
        private bool _closing;

        public EarlyWarningWindow(string serialNumber, string message, bool needsCard, Func<string, Task<string>> checkCard)
        {
            _needsCard = needsCard;
            _checkCard = checkCard;

            Title = Tr("Early warning");
            Width = 520;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 14;
            Background = new SolidColorBrush(MesCardDialog.PermissionBackground);

            var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            panel.Children.Add(new TextBlock
            {
                Text = Tr("Early warning: common defect factor"),
                FontWeight = FontWeights.Bold,
                FontSize = 17,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(new TextBlock { Text = Tr("Start time:") + " " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss.fff"), Margin = new Thickness(0, 12, 0, 0) });
            panel.Children.Add(new TextBlock { Text = Tr("Unit SN:") + " " + serialNumber, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(new TextBlock { Text = Tr("Detail Msg:"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) });

            // MES's own text, as it came (it is written in the factory's language).
            panel.Children.Add(new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(8),
                Child = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            });

            panel.Children.Add(new TextBlock
            {
                Text = Tr(needsCard
                    ? "The machine keeps running. Confirm, then the CM DRI swipes their ID card to close this window."
                    : "The machine keeps running. Confirm closes this window."),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0),
            });

            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            panel.Children.Add(_status);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            _mute = new Button { Content = Tr("Mute"), MinWidth = 92, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 10, 0) };
            _mute.Click += (sender, e) =>
            {
                _mute.IsEnabled = false;
                _status.Text = Tr("Buzzer muted.");
                Muted?.Invoke(this, EventArgs.Empty);
            };
            var confirm = new Button { Content = Tr("Confirm"), MinWidth = 92, Padding = new Thickness(14, 6, 14, 6) };
            confirm.Click += Confirm_Click;
            buttons.Children.Add(_mute);
            buttons.Children.Add(confirm);
            panel.Children.Add(buttons);

            Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x40, 0x80)), BorderThickness = new Thickness(2), Child = panel };

            // "Must always stay on top and not be covered by any application."
            Deactivated += (sender, e) =>
            {
                if (!_closing)
                {
                    Topmost = false;
                    Topmost = true;
                }
            };
        }

        /// <summary>Raised when the operator mutes the buzzer.</summary>
        public event EventHandler Muted;

        /// <summary>The card that closed the window; empty where no card is needed.</summary>
        public string ClosedBy { get; private set; } = string.Empty;

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (_needsCard)
            {
                var card = new MesCardDialog("Close the early warning", "The CM DRI swipes their ID card to close the warning.", _checkCard)
                {
                    Owner = this,
                };
                if (card.ShowDialog() != true)
                    return;

                ClosedBy = card.CardUid ?? string.Empty;
            }

            _closing = true;
            Close();
        }

        /// <summary>Only Confirm closes the warning.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = !_closing;
            base.OnClosing(e);
        }

        private static string Tr(string english)
        {
            return LocalizationSource.Instance.Translate(english);
        }
    }
}
