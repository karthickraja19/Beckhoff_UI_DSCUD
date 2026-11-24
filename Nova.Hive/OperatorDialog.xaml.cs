using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>
    /// The card-scan window of Audio SW DFM v5.4, slide 50. It opens when the operator
    /// presses Confirm on the alarm pop-up and reads the badge that is sent to HIVE as
    /// u_id. The badge must come from the reader: "Manually key in is NOT allowed".
    /// </summary>
    /// <remarks>
    /// The badge goes through <see cref="CardScan"/> (slide 37: 200 ms, nominal length,
    /// no Control key or right mouse button). When an access check is passed in, the
    /// badge must also be in the access list at the level asked for - slide 49's "Level
    /// 2/3 OSS only" for a manual HIVE state change.
    /// </remarks>
    public partial class OperatorDialog : Window
    {
        private readonly CardScan _scan = new CardScan();
        private readonly IAccessControlService _access;
        private readonly AccessLevel _minimumLevel;
        private readonly Func<AccessCheckResult, string> _extraCheck;
        private readonly bool _approver;

        /// <summary>Counts badge checks, so a slow answer for an earlier swipe is ignored.</summary>
        private int _check;

        /// <param name="header">
        /// The red line at the top: "Processing Downtime" when an alarm is being
        /// confirmed, otherwise the state the operator is applying.
        /// </param>
        /// <param name="access">Checks the badge against the access list; null to accept any badge.</param>
        /// <param name="minimumLevel">The lowest access level accepted when <paramref name="access"/> is given.</param>
        /// <param name="extraCheck">
        /// A further rule for a granted badge, e.g. the second approval of slide 45: returns
        /// why the badge is refused, or null to accept it. Only used with <paramref name="access"/>.
        /// </param>
        /// <param name="prompt">The line under the badge box before anything is swiped; null for the default.</param>
        /// <param name="approver">
        /// Check the badge as the approver of a two-step verification (slide 45) rather
        /// than as a sign-in.
        /// </param>
        public OperatorDialog(
            string header = "Processing Downtime",
            IAccessControlService access = null,
            AccessLevel minimumLevel = AccessLevel.Level1,
            Func<AccessCheckResult, string> extraCheck = null,
            string prompt = null,
            bool approver = false)
        {
            InitializeComponent();

            HeaderText.Text = HiveText.T(header);
            _access = access;
            _minimumLevel = minimumLevel;
            _extraCheck = extraCheck;
            _approver = approver;

            CardScan.BlockManualEntry(OperatorBox);

            if (!string.IsNullOrWhiteSpace(prompt))
                MessageText.Text = HiveText.T(prompt);
            else if (access != null && minimumLevel >= AccessLevel.Level2)
                MessageText.Text = HiveText.T("Swipe a Level " + (int)minimumLevel + " or higher ID card.");

            Loaded += (sender, e) => OperatorBox.Focus();
        }

        /// <summary>The badge that was swiped.</summary>
        public string OperatorId { get; private set; }

        /// <summary>What the access check returned for the badge; null when no access check was made.</summary>
        public AccessCheckResult Result { get; private set; }

        /// <summary>Collects the characters the reader types.</summary>
        private void OperatorBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
            _scan.Add(e.Text);
        }

        /// <summary>The reader finishes a swipe with Enter.</summary>
        private void OperatorBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            e.Handled = true;

            // A new swipe replaces whatever was read before.
            OperatorId = null;
            Result = null;
            OperatorBox.Text = string.Empty;
            OkButton.IsEnabled = false;
            _check++;

            string refused;
            string badge = _scan.Finish(out refused);
            if (badge == null)
            {
                MessageText.Text = HiveText.T(refused ?? "Swipe your ID card.");
                return;
            }

            if (_access == null)
            {
                Accept(badge, "Badge read. Press OK.");
                return;
            }

            var ignored = CheckBadgeAsync(badge, _check);
        }

        /// <summary>Slide 49: only a Level 2/3 badge from the access list may change the HIVE state.</summary>
        private async Task CheckBadgeAsync(string badge, int check)
        {
            MessageText.Text = HiveText.T("Checking the badge...");

            AccessCheckResult result;
            try
            {
                result = _approver
                    ? await _access.CheckApproverAsync(badge)
                    : await _access.CheckAccessAsync(badge);
            }
            catch (Exception ex)
            {
                result = AccessCheckResult.Deny("The badge could not be checked: " + ex.Message);
            }

            // Swiped again, or the dialog closed, while this one was being checked.
            if (check != _check || !IsLoaded)
                return;

            if (result == null || !result.Granted)
            {
                MessageText.Text = HiveText.T(result != null ? result.FailureReason : "Access denied.") + " " + HiveText.T("Please swipe again.");
                return;
            }

            if (result.Level < _minimumLevel)
            {
                MessageText.Text = result.Name + " is Level " + (int)result.Level + ". This needs Level "
                    + (int)_minimumLevel + " or higher - please swipe another ID card.";
                return;
            }

            string refusedByRule = _extraCheck != null ? _extraCheck(result) : null;
            if (refusedByRule != null)
            {
                MessageText.Text = HiveText.T(refusedByRule);
                return;
            }

            Result = result;
            Accept(badge, result.Name + " (Level " + (int)result.Level + "). Press OK.");
        }

        private void Accept(string badge, string message)
        {
            OperatorId = badge;
            OperatorBox.Text = badge;
            MessageText.Text = HiveText.T(message);
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
