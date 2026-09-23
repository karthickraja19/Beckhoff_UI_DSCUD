using System.Windows;

namespace Nova.Hive
{
    /// <summary>What the operator picked in <see cref="HiveStateDialog"/>.</summary>
    public enum HiveStateChoice
    {
        None,
        Running,
        Idle,
        PlannedDowntime,
        ManualDowntime,
    }

    /// <summary>
    /// The state options the HIVE chip opens - Audio SW DFM v5.4, slide 49. Level 2/3
    /// only, and the state is not applied until the operator has badged in.
    /// </summary>
    public partial class HiveStateDialog : Window
    {
        public HiveStateDialog()
        {
            InitializeComponent();
        }

        public HiveStateChoice Choice { get; private set; }

        private void Running_Click(object sender, RoutedEventArgs e) => Pick(HiveStateChoice.Running);

        private void Idle_Click(object sender, RoutedEventArgs e) => Pick(HiveStateChoice.Idle);

        private void PlannedDowntime_Click(object sender, RoutedEventArgs e) => Pick(HiveStateChoice.PlannedDowntime);

        private void ManualDowntime_Click(object sender, RoutedEventArgs e) => Pick(HiveStateChoice.ManualDowntime);

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Pick(HiveStateChoice choice)
        {
            Choice = choice;
            DialogResult = true;
        }
    }
}
