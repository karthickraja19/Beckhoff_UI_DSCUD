using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Nova.Hive
{
    /// <summary>
    /// The alarm pop-up of Audio SW DFM v5.4, slide 50. It shows the alarm that stopped
    /// the machine, offers a repair action and a replacement error detail, and can only
    /// be left by pressing Confirm and swiping a badge in <see cref="OperatorDialog"/>.
    /// </summary>
    public partial class AlarmPopup : Window
    {
        private readonly string _definedDetail;
        private bool _acknowledged;

        public AlarmPopup(string startTime, string errorCode, string errorMessage, string errorDetail)
        {
            InitializeComponent();

            _definedDetail = errorDetail;

            StartTimeText.Text = startTime;
            ErrorCodeText.Text = errorCode;
            ErrorMessageText.Text = errorMessage;
            ErrorDetailText.Text = errorDetail;

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
            var operatorDialog = new OperatorDialog { Owner = this };

            if (operatorDialog.ShowDialog() != true)
                return;   // cancelled: the alarm window stays on the screen

            OperatorId = operatorDialog.OperatorId;
            _acknowledged = true;
            DialogResult = true;
        }

        /// <summary>The window stays on the screen until a badge has been swiped.</summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = !_acknowledged;
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

        /// <summary>The repair actions of Audio SW DFM v5.4, "Repair Action Code".</summary>
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
        };
    }
}
