using System;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace Nova.Hive
{
    /// <summary>
    /// The card-scan window of Audio SW DFM v5.4, slide 50. It opens when the operator
    /// presses Confirm on the alarm pop-up and reads the badge that is sent to HIVE as
    /// u_id. The badge must come from the reader: "Manually key in is NOT allowed".
    /// </summary>
    /// <remarks>
    /// Badge readers on this machine are keyboard-wedge devices: they type the UID as
    /// fast keystrokes and finish with Enter, the same as the login screen does.
    /// </remarks>
    public partial class OperatorDialog : Window
    {
        /// <summary>A gap longer than this means the characters are not from one swipe.</summary>
        private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// A reader delivers the whole badge in a few tens of milliseconds. The DFM asks
        /// for 200 ms; this is a little more so a slower reader still works, while typing
        /// by hand (seconds) is still refused.
        /// </summary>
        private static readonly TimeSpan ScanMustFinishIn = TimeSpan.FromMilliseconds(500);

        /// <summary>Guards against a stray keypress being taken for a badge.</summary>
        private const int MinimumBadgeLength = 3;

        private readonly StringBuilder _badge = new StringBuilder();
        private DateTime _scanStarted = DateTime.MinValue;
        private DateTime _lastKey = DateTime.MinValue;

        /// <param name="header">
        /// The red line at the top: "Processing Downtime" when an alarm is being
        /// confirmed, otherwise the state the operator is applying.
        /// </param>
        public OperatorDialog(string header = "Processing Downtime")
        {
            InitializeComponent();

            HeaderText.Text = header;

            Loaded += (sender, e) => OperatorBox.Focus();
        }

        /// <summary>The badge that was swiped.</summary>
        public string OperatorId { get; private set; }

        /// <summary>Collects the characters the reader types.</summary>
        private void OperatorBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;

            DateTime now = DateTime.Now;
            if (now - _lastKey > ScanTimeout)
            {
                // Too long since the last character: this is the start of a new swipe.
                _badge.Clear();
                _scanStarted = now;
            }

            _lastKey = now;

            foreach (char c in e.Text)
            {
                if (char.IsLetterOrDigit(c))
                    _badge.Append(c);
            }
        }

        /// <summary>The reader finishes a swipe with Enter.</summary>
        private void OperatorBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            e.Handled = true;

            string badge = _badge.ToString();
            _badge.Clear();

            if (badge.Length < MinimumBadgeLength)
            {
                MessageText.Text = "Swipe your ID card.";
                return;
            }

            if (DateTime.Now - _scanStarted > ScanMustFinishIn)
            {
                MessageText.Text = "Typing the ID is not allowed - please swipe your ID card.";
                return;
            }

            OperatorId = badge;
            OperatorBox.Text = badge;
            MessageText.Text = "Badge read. Press OK.";
            OkButton.IsEnabled = true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(OperatorId))
                return;

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Back to the alarm window, which stays on the screen until a badge is swiped.
            DialogResult = false;
        }
    }
}
