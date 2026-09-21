using System.Windows.Controls;
using System.Windows.Input;
using Nova.ViewModels.Pages.Recipe;

namespace Nova.Wpf.App.Views.Pages.Recipe
{
    public partial class GantryRecipeView : UserControl
    {
        public GantryRecipeView()
        {
            InitializeComponent();
        }

        private GantryRecipeViewModel Vm => DataContext as GantryRecipeViewModel;

        private void JogXPositive_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogXPositiveCommand.Execute(null);

        private void JogXNegative_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogXNegativeCommand.Execute(null);

        private void JogYPositive_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogYPositiveCommand.Execute(null);

        private void JogYNegative_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogYNegativeCommand.Execute(null);

        private void JogZPositive_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogZPositiveCommand.Execute(null);

        private void JogZNegative_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogZNegativeCommand.Execute(null);

        private void JogRotateCw_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogRotateCwCommand.Execute(null);

        private void JogRotateCcw_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => Vm?.JogRotateCcwCommand.Execute(null);

        private void Jog_PreviewMouseUp(object sender, MouseButtonEventArgs e)
            => Vm?.StopJogCommand.Execute(null);

        private void Jog_MouseLeave(object sender, MouseEventArgs e)
            => Vm?.StopJogCommand.Execute(null);
    }
}
