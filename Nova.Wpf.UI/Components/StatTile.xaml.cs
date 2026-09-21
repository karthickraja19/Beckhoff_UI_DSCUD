using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nova.Wpf.UI.Components
{
    /// <summary>A labelled metric tile with an optional colored status dot.</summary>
    public partial class StatTile : UserControl
    {
        public StatTile() => InitializeComponent();

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DotColorProperty =
            DependencyProperty.Register(nameof(DotColor), typeof(string), typeof(StatTile), new PropertyMetadata("#00000000"));

        public static readonly DependencyProperty ShowDotProperty =
            DependencyProperty.Register(nameof(ShowDot), typeof(bool), typeof(StatTile), new PropertyMetadata(true));

        public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
        public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public string DotColor { get => (string)GetValue(DotColorProperty); set => SetValue(DotColorProperty, value); }
        public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }
    }
}
