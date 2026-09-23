using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Nova.Hive
{
    /// <summary>One planned downtime reason of Audio SW DFM v5.4, slide 49.</summary>
    public sealed class PlannedDowntimeReason
    {
        public PlannedDowntimeReason(string name, string code, bool isSelectable = true)
        {
            Name = name;
            Code = code;
            IsSelectable = isSelectable;
        }

        /// <summary>The reason, e.g. "Needle Change". Reported as error_message.</summary>
        public string Name { get; }

        /// <summary>The planned downtime code, e.g. "PD-03". Reported as the error code.</summary>
        public string Code { get; }

        /// <summary>
        /// False for the two the slide greys out: they are entered by their own
        /// procedure rather than chosen here.
        /// </summary>
        public bool IsSelectable { get; }

        public string Label
        {
            get { return Name + " (" + Code + ")"; }
        }

        /// <summary>The list as slide 49 prints it, in that order.</summary>
        public static IReadOnlyList<PlannedDowntimeReason> All { get; } = new List<PlannedDowntimeReason>
        {
            new PlannedDowntimeReason("Daily Maintenance", "PD-01"),
            new PlannedDowntimeReason("Glue Change", "PD-02"),
            new PlannedDowntimeReason("Needle Change", "PD-03"),
            new PlannedDowntimeReason("Valve Change", "PD-04"),
            new PlannedDowntimeReason("Stress Test", "PD-05"),
            new PlannedDowntimeReason("Laser Calibration", "PD-06"),
            new PlannedDowntimeReason("Consumable part Replacement", "PD-07"),
            new PlannedDowntimeReason("Material Replacement", "PD-08"),
            new PlannedDowntimeReason("Weekly Maintenance", "PD-09"),
            new PlannedDowntimeReason("Glue Weighing", "PD-10"),
            new PlannedDowntimeReason("LAD", "PD-11"),
            new PlannedDowntimeReason("Nozzle Change", "PD-12"),
            new PlannedDowntimeReason("CCD Repeatability Testing", "PD-13"),
            new PlannedDowntimeReason("Dispensing Pose Calibration", "PD-14", false),
            new PlannedDowntimeReason("Uploaded Hash keys to MES", "PD-15", false),
            new PlannedDowntimeReason("Others", "PD-101"),
        };
    }

    /// <summary>The planned downtime reasons the operator picks from (slide 49).</summary>
    public partial class PlannedDowntimeDialog : Window
    {
        public PlannedDowntimeDialog()
        {
            InitializeComponent();

            ReasonList.ItemsSource = PlannedDowntimeReason.All;
        }

        /// <summary>The chosen reason, or null.</summary>
        public PlannedDowntimeReason Reason { get; private set; }

        private void Reason_Checked(object sender, RoutedEventArgs e)
        {
            Reason = (PlannedDowntimeReason)((RadioButton)sender).Tag;
            OkButton.IsEnabled = true;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (Reason == null)
                return;

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
