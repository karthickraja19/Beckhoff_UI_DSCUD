using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nova.ViewModels.Pages.Recipe;

namespace Nova.Wpf.App.Views.Pages.Recipe
{
    /// <summary>
    /// CNC recipe tab. A jog key jogs while it is held: pressed starts the jog of the axis in
    /// its Tag, released or left stops it.
    /// </summary>
    public partial class CNCRecipeView : UserControl
    {
        public CNCRecipeView()
        {
            InitializeComponent();
        }

        private CNCRecipeViewModel Vm => DataContext as CNCRecipeViewModel;

        private void JogPositive_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogPositiveCommand.Execute((sender as FrameworkElement)?.Tag as CncAxisItem);

        private void JogNegative_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogNegativeCommand.Execute((sender as FrameworkElement)?.Tag as CncAxisItem);

        private void Jog_PreviewMouseUp(object sender, MouseButtonEventArgs e)
            => Vm?.StopJogCommand.Execute(null);

        private void Jog_MouseLeave(object sender, MouseEventArgs e)
            => Vm?.StopJogCommand.Execute(null);
    }
}
