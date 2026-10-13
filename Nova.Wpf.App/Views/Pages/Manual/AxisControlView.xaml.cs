using Nova.ViewModels.Pages.Manual;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nova.Wpf.App.Views.Pages.Manual
{
    public partial class AxisControlView : UserControl
    {
        public AxisControlView()
        {
            InitializeComponent();
        }

        private AxisControlViewModel Vm => DataContext as AxisControlViewModel;

        // View glue only: jog must run while the button is held and stop on release
        // (same press/hold semantics as the V3 MouseDown/MouseUp handlers), which a
        // plain Command binding cannot express without a behaviors package.

        private void JogForward_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogForwardStartCommand.Execute(null);

        private void JogReverse_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogReverseStartCommand.Execute(null);

        private void Jog_PreviewMouseUp(object sender, MouseButtonEventArgs e)
            => Vm?.JogStopCommand.Execute(null);

        private void Jog_MouseLeave(object sender, MouseEventArgs e)
            => Vm?.JogStopCommand.Execute(null);
    }
}
