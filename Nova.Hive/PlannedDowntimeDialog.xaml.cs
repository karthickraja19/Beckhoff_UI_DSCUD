using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Nova.Hive
{
    /// <summary>One planned downtime reason of Audio SW DFM v5.4, slides 31 and 49.</summary>
    public sealed class PlannedDowntimeReason
    {
        /// <summary>Slide 31: "Tuning/Debug Material Run".</summary>
        public const string DebugRunCode = "PD-17";

        public PlannedDowntimeReason(
            string name, string code, bool isSelectable = true, bool allowsDebugRun = false, bool needsDetails = false)
        {
            Name = name;
            Code = code;
            IsSelectable = isSelectable;
            AllowsDebugRun = allowsDebugRun;
            NeedsDetails = needsDetails;
        }

        /// <summary>The reason, e.g. "Needle Change". Reported as error_message.</summary>
        public string Name { get; }

        /// <summary>The planned downtime code, e.g. "PD-03". Reported as the error code.</summary>
        public string Code { get; }

        /// <summary>
        /// False for a reason that is entered by its own procedure rather than chosen
        /// here. (PD-14 and PD-15, greyed out on slide 49, are chosen here by a Level 3
        /// user - slides 74 and 78 - and <see cref="HiveStatePrompt"/> enforces that.)
        /// </summary>
        public bool IsSelectable { get; }

        /// <summary>
        /// Slides 74 and 80: "Only enable when Admin user logged in" (PD-14) and the hash
        /// key page for Level 3 (PD-15).
        /// </summary>
        public bool NeedsLevel3
        {
            get { return Code == "PD-14" || Code == "PD-15"; }
        }

        /// <summary>
        /// The reasons slide 31 marks with "*": once the work is done the machine may go
        /// on to a Tuning/Debug Material Run (PD-17) before it goes back to running.
        /// </summary>
        public bool AllowsDebugRun { get; }

        /// <summary>
        /// Slide 31: Offline Processing and Other need the operator to say what is being
        /// done - "Must enter DOE details" and "Specify action details".
        /// </summary>
        public bool NeedsDetails { get; }

        public string Label
        {
            get { return Name + " (" + Code + ")"; }
        }

        /// <summary>
        /// The list in slide 49's order, with the codes slide 31 adds after PD-15. Others
        /// stays PD-101 as slide 49 prints it; slide 31 calls it PD-99 and AME has not
        /// said which is right yet.
        /// </summary>
        public static IReadOnlyList<PlannedDowntimeReason> All { get; } = new List<PlannedDowntimeReason>
        {
            new PlannedDowntimeReason("Daily Maintenance", "PD-01"),
            new PlannedDowntimeReason("Glue Change", "PD-02", allowsDebugRun: true),
            new PlannedDowntimeReason("Needle Change", "PD-03", allowsDebugRun: true),
            new PlannedDowntimeReason("Valve Change", "PD-04", allowsDebugRun: true),
            new PlannedDowntimeReason("Stress Test", "PD-05"),
            new PlannedDowntimeReason("Laser Calibration", "PD-06", allowsDebugRun: true),
            new PlannedDowntimeReason("Consumable part Replacement", "PD-07", allowsDebugRun: true),
            new PlannedDowntimeReason("Material Replacement", "PD-08"),
            new PlannedDowntimeReason("Weekly Maintenance", "PD-09"),
            new PlannedDowntimeReason("Glue Weighing", "PD-10", allowsDebugRun: true),
            new PlannedDowntimeReason("LAD", "PD-11"),
            new PlannedDowntimeReason("Nozzle Change", "PD-12", allowsDebugRun: true),
            new PlannedDowntimeReason("CCD Repeatability Testing", "PD-13"),
            new PlannedDowntimeReason("Dispensing Pose Calibration", "PD-14", allowsDebugRun: true),
            new PlannedDowntimeReason("Uploaded Hash keys to MES", "PD-15"),
            new PlannedDowntimeReason("24hr Dry Run", "PD-16"),
            new PlannedDowntimeReason("Tuning/Debug Material Run", DebugRunCode),
            new PlannedDowntimeReason("Offline Processing", "PD-100", needsDetails: true),
            new PlannedDowntimeReason("Others", "PD-101", needsDetails: true),
        };

        /// <summary>The reason with this code, or null.</summary>
        public static PlannedDowntimeReason Find(string code)
        {
            foreach (PlannedDowntimeReason reason in All)
            {
                if (string.Equals(reason.Code, code, StringComparison.OrdinalIgnoreCase))
                    return reason;
            }

            return null;
        }

        /// <summary>The error sent to HIVE for this reason. Details replace the name as error_detail.</summary>
        public HiveError ToError(string details = null)
        {
            return new HiveError
            {
                Code = Code,
                Message = Name,
                Detail = string.IsNullOrWhiteSpace(details) ? Name : details.Trim(),
                Severity = "warning",
                StopsMachine = true,
            };
        }
    }

    /// <summary>One line of the dialog: a reason, and why it cannot be chosen right now.</summary>
    public sealed class PlannedDowntimeOption
    {
        public PlannedDowntimeOption(PlannedDowntimeReason reason, string blockedBecause)
        {
            Reason = reason;
            BlockedBecause = blockedBecause;
        }

        public PlannedDowntimeReason Reason { get; }

        public string Label
        {
            get { return Reason.Label; }
        }

        /// <summary>Null when the reason can be chosen.</summary>
        public string BlockedBecause { get; }

        public bool IsEnabled
        {
            get { return BlockedBecause == null; }
        }
    }

    /// <summary>The planned downtime reasons the operator picks from (slide 49).</summary>
    public partial class PlannedDowntimeDialog : Window
    {
        /// <param name="options">Every reason, each with why it cannot be chosen from the current state.</param>
        public PlannedDowntimeDialog(IEnumerable<PlannedDowntimeOption> options)
        {
            InitializeComponent();

            ReasonList.ItemsSource = options;
        }

        /// <summary>The chosen reason, or null.</summary>
        public PlannedDowntimeReason Reason { get; private set; }

        /// <summary>What the operator entered for a reason that needs details; otherwise empty.</summary>
        public string Details
        {
            get { return Reason != null && Reason.NeedsDetails ? DetailsBox.Text.Trim() : string.Empty; }
        }

        private void Reason_Checked(object sender, RoutedEventArgs e)
        {
            Reason = ((PlannedDowntimeOption)((RadioButton)sender).Tag).Reason;

            DetailsPanel.Visibility = Reason.NeedsDetails ? Visibility.Visible : Visibility.Collapsed;
            DetailsLabel.Text = Reason.Code == "PD-100"
                ? "DOE details (needs Apple AME approval before use):"
                : "Details of the planned work:";

            UpdateOk();

            if (Reason.NeedsDetails)
                DetailsBox.Focus();
        }

        private void DetailsBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOk();
        }

        /// <summary>A reason that needs details cannot be sent without them.</summary>
        private void UpdateOk()
        {
            OkButton.IsEnabled = Reason != null && (!Reason.NeedsDetails || DetailsBox.Text.Trim().Length > 0);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!OkButton.IsEnabled)
                return;

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
