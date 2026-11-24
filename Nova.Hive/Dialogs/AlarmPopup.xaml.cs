using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>
    /// The alarm pop-up of Audio SW DFM v5.4, slide 50. It shows the alarm that stopped
    /// the machine, offers a repair action and a replacement error detail, and can only
    /// be left by pressing Confirm and swiping a badge in <see cref="OperatorDialog"/>.
    /// There is no Close button.
    /// </summary>
    public partial class AlarmPopup : Window
    {
        private readonly string _definedDetail;
        private bool _acknowledged;
        private bool _closable;

        /// <summary>Checks the closing badge; null accepts any badge from the reader.</summary>
        public IAccessControlService CloseAccess { get; set; }

        /// <summary>The lowest level whose badge may close the pop-up (Level 1 = any operator).</summary>
        public AccessLevel CloseLevel { get; set; } = AccessLevel.Level1;

        /// <summary>
        /// True shows the X in the title bar and lets the pop-up be closed without Confirm
        /// (nothing is reported to HIVE then); false (default) has no X and the pop-up can
        /// only be left with Confirm and a badge.
        /// </summary>
        public bool Closable
        {
            get { return _closable; }
            set { _closable = value; }
        }

        /// <summary>What the X button's Visibility is bound to: Visible when closable, else Collapsed.</summary>
        public Visibility CloseButtonVisibility
        {
            get { return _closable ? Visibility.Visible : Visibility.Collapsed; }
        }

        public AlarmPopup(string startTime, string errorCode, string errorMessage, string errorDetail,
            string shownMessage = null, string shownDetail = null)
        {
            InitializeComponent();

            _definedDetail = errorDetail;

            StartTimeText.Text = startTime;
            ErrorCodeText.Text = errorCode;
            // The selected language on screen; _definedDetail (English) is what HIVE gets.
            ErrorMessageText.Text = shownMessage ?? errorMessage;
            ErrorDetailText.Text = shownDetail ?? errorDetail;

            RepairActionBox.ItemsSource = RepairAction.All;
            RepairActionBox.SelectedIndex = 0;
        }

        /// <summary>
        /// The detail to report: what the operator typed under Options, or the defined
        /// detail when they typed nothing.
        /// </summary>
        public string ErrorDetail
        {
            get
            {
                string changed = ChangeDetailBox.Text.Trim();
                return changed.Length > 0 ? changed : _definedDetail;
            }
        }

        /// <summary>The chosen repair action, e.g. "-09", or an empty string.</summary>
        public string RepairActionCode
        {
            get
            {
                var action = RepairActionBox.SelectedItem as RepairAction;
                return action != null ? action.Code : "";
            }
        }

        /// <summary>The badge that was swiped in the operator dialog.</summary>
        public string OperatorId { get; private set; }

        /// <summary>True once the operator pressed Mute. Wire this to the buzzer output.</summary>
        public bool IsMuted { get; private set; }

        private void ChangeDetailBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ChangeDetailHint.Visibility = ChangeDetailBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            // There is no buzzer service in this build yet; the button records the
            // operator's choice so it can be passed on once there is one.
            IsMuted = true;
            MuteButton.IsEnabled = false;
        }

        /// <summary>
        /// Slide 50: "Once confirm window is clicked, pop up window for card scan".
        /// The alarm window only closes when that dialog comes back with a badge.
        /// </summary>
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            // Slide 50: any operator's badge. The Alarm page spec asks for Level 2/3 to close
            // the pop-up (Q8 with AME), so the level is a setting (AlarmPopupCloseLevel).
            var operatorDialog = CloseAccess != null && CloseLevel >= AccessLevel.Level2
                ? new OperatorDialog("Processing Downtime", CloseAccess, CloseLevel) { Owner = this }
                : new OperatorDialog { Owner = this };

            if (operatorDialog.ShowDialog() != true)
                return;   // cancelled: the alarm window stays on the screen

            OperatorId = operatorDialog.OperatorId;
            _acknowledged = true;
            DialogResult = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Only reachable when Closable; closes without an acknowledgement.
            Close();
        }

        /// <summary>The window stays on the screen until a badge has been swiped, unless it is closable.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = !_acknowledged && !_closable;
            base.OnClosing(e);
        }
    }

    /// <summary>One repair action of the DFM's list, shown in the Options drop-down.</summary>
    public sealed class RepairAction
    {
        public RepairAction(string label, string code)
        {
            Label = label;
            Code = code;
        }

        public string Label { get; }

        /// <summary>What is added to the error code, e.g. "-09". Empty when none was chosen.</summary>
        public string Code { get; }

        /// <summary>The repair actions of Audio SW DFM v5.4 slide 35, in its order.</summary>
        public static IReadOnlyList<RepairAction> All { get; } = new List<RepairAction>
        {
            new RepairAction("<Add repair action>", ""),
            new RepairAction("Adjusted", "-01"),
            new RepairAction("Calibrated", "-02"),
            new RepairAction("Checked", "-03"),
            new RepairAction("Cleaned", "-04"),
            new RepairAction("Erased", "-05"),
            new RepairAction("Filled", "-06"),
            new RepairAction("Fixed", "-07"),
            new RepairAction("Lubricated", "-08"),
            new RepairAction("Replaced", "-09"),
            new RepairAction("Restored", "-10"),
            new RepairAction("Stretched", "-11"),
            new RepairAction("Tightened", "-12"),
            new RepairAction("Trained", "-13"),
            new RepairAction("Tuned", "-14"),
            new RepairAction("Other", "-99"),
        };
    }
}
