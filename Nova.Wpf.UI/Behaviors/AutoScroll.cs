using System;
using System.Windows;
using System.Windows.Controls;

namespace Nova.Wpf.UI.Behaviors
{
    /// <summary>
    /// Attached behaviour that keeps a <see cref="ScrollViewer"/> on its last line whenever
    /// its content grows, so the newest console response is always the one in view.
    ///
    /// FAM 2 MS_10 / MS_13 / MS_17: the Received Response consoles added each answer at the
    /// bottom but never scrolled to it, and stayed where the operator had dragged the bar.
    ///
    /// Usage:
    ///   &lt;ScrollViewer behaviors:AutoScroll.ToEnd="True"&gt;...&lt;/ScrollViewer&gt;
    /// </summary>
    public static class AutoScroll
    {
        public static readonly DependencyProperty ToEndProperty =
            DependencyProperty.RegisterAttached(
                "ToEnd",
                typeof(bool),
                typeof(AutoScroll),
                new PropertyMetadata(false, OnToEndChanged));

        public static bool GetToEnd(DependencyObject element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            return (bool)element.GetValue(ToEndProperty);
        }

        public static void SetToEnd(DependencyObject element, bool value)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            element.SetValue(ToEndProperty, value);
        }

        private static void OnToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var viewer = d as ScrollViewer;
            if (viewer == null)
                return;

            viewer.ScrollChanged -= OnScrollChanged;
            if ((bool)e.NewValue)
                viewer.ScrollChanged += OnScrollChanged;
        }

        /// <summary>Only a change in the content's height moves the view: scrolling by hand still works.</summary>
        private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange != 0)
                ((ScrollViewer)sender).ScrollToEnd();
        }
    }
}
