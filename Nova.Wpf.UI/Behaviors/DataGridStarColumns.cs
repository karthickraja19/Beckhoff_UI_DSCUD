using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nova.Wpf.UI.Behaviors
{
    /// <summary>
    /// Attached behaviour that keeps a <see cref="DataGrid"/>'s star-sized columns sized
    /// when the grid starts out hidden.
    ///
    /// A DataGrid that is collapsed when it is first laid out - one on a tab that is
    /// not in front - shares out no width to its star columns, and keeps those zero
    /// widths when it is shown: the column is simply missing. Re-applying each star
    /// width once the grid is visible and measured makes it share out the real width.
    ///
    /// Usage:
    ///   xmlns:behaviors="clr-namespace:Nova.Wpf.UI.Behaviors;assembly=Nova.Wpf.UI"
    ///   &lt;DataGrid behaviors:DataGridStarColumns.RefreshOnShow="True"/&gt;
    /// </summary>
    public static class DataGridStarColumns
    {
        public static readonly DependencyProperty RefreshOnShowProperty =
            DependencyProperty.RegisterAttached(
                "RefreshOnShow",
                typeof(bool),
                typeof(DataGridStarColumns),
                new PropertyMetadata(false, OnRefreshOnShowChanged));

        public static bool GetRefreshOnShow(DependencyObject element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            return (bool)element.GetValue(RefreshOnShowProperty);
        }

        public static void SetRefreshOnShow(DependencyObject element, bool value)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            element.SetValue(RefreshOnShowProperty, value);
        }

        private static void OnRefreshOnShowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var grid = d as DataGrid;
            if (grid == null)
                return;

            grid.IsVisibleChanged -= OnIsVisibleChanged;

            if ((bool)e.NewValue)
                grid.IsVisibleChanged += OnIsVisibleChanged;
        }

        private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var grid = (DataGrid)sender;
            if (!(bool)e.NewValue)
                return;

            // After layout has given the grid its width, or there is nothing to share out.
            grid.Dispatcher.BeginInvoke(new Action(() => Refresh(grid)), DispatcherPriority.Loaded);
        }

        private static void Refresh(DataGrid grid)
        {
            foreach (DataGridColumn column in grid.Columns)
            {
                DataGridLength width = column.Width;
                if (!width.IsStar)
                    continue;

                // Setting the same value again is ignored, so step through a pixel width.
                column.Width = new DataGridLength(0);
                column.Width = width;
            }
        }
    }
}
