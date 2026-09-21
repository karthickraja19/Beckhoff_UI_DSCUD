using System.Windows.Controls;

namespace Nova.Wpf.App.Views.Shells
{
    /// <summary>
    /// Code-behind for the Audio/Home indicator bar. Deliberately empty: every piece of
    /// state the bar draws - which button is energized, which chip artwork shows,
    /// whether the bar is red - is decided by <c>AudioShellViewModel</c>, because the
    /// DFM's rules for those are access-control and machine rules rather than visual
    /// ones.
    /// </summary>
    public partial class AudioShellView : UserControl
    {
        public AudioShellView()
        {
            InitializeComponent();
        }
    }
}
