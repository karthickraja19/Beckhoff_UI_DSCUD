using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Nova.Wpf.UI.Components
{
    /// <summary>
    /// Read-only status light. IsActive=true → green "On" (live/connected);
    /// false → muted "Off" (not connected). Bind IsActive to the live device state.
    ///
    /// Colours resolve through the active theme dictionary (SetResourceReference is the
    /// code equivalent of DynamicResource) rather than baked-in brushes, so the "off"
    /// gray does not disappear against the dark theme's surfaces.
    /// </summary>
    public partial class StatusIndicator : UserControl
    {
        private const string OnBrushKey = "SuccessBrush";
        private const string OffBrushKey = "ThirdlyTextBrush";

        // The halo is the dot's own hue, softened — no separate translucent brush needed.
        private const double OnHaloOpacity = 0.22;
        private const double OffHaloOpacity = 0.12;

        public StatusIndicator()
        {
            InitializeComponent();
            Apply();
        }

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(StatusIndicator),
                new PropertyMetadata(false, (d, e) => ((StatusIndicator)d).Apply()));

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        private void Apply()
        {
            if (Dot == null) return;

            string key = IsActive ? OnBrushKey : OffBrushKey;

            Dot.SetResourceReference(Shape.FillProperty, key);
            Halo.SetResourceReference(Shape.FillProperty, key);
            Halo.Opacity = IsActive ? OnHaloOpacity : OffHaloOpacity;

            Label.SetResourceReference(TextBlock.ForegroundProperty, key);
        }
    }
}
