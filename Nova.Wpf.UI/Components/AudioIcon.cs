using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nova.Abstractions.Data;
using Nova.Wpf.UI.Assets;

namespace Nova.Wpf.UI.Components
{
    /// <summary>
    /// Draws one piece of DFM icon artwork.
    /// <para>
    /// Two ways to say which: set <see cref="Glyph"/> and <see cref="State"/> for a
    /// button glyph that changes with its state (Home, Alarm, Config, Data, Vision,
    /// Setting, Running, Paused, Stopped), or set <see cref="Asset"/> for artwork that
    /// is already a finished chip (the Level and HIVE chips, the Audio wordmark).
    /// <see cref="Asset"/> wins if both are set.
    /// </para>
    /// <para>
    /// This exists so a view can bind one string and one enum instead of carrying a
    /// trigger per state, and so the icon a button shows is decided by view-model
    /// state rather than by XAML guessing from IsChecked/IsEnabled - the DFM's rules
    /// ("disabled before login", "alerted when an error occurs") are access and
    /// machine rules, not visual-tree facts.
    /// </para>
    /// </summary>
    public class AudioIcon : Control
    {
        static AudioIcon()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(AudioIcon), new FrameworkPropertyMetadata(typeof(AudioIcon)));

            // The 192px source is drawn at 56px or smaller. Without this WPF uses
            // Linear, which visibly softens the glyph edges at that ratio.
            RenderOptions.BitmapScalingModeProperty.OverrideMetadata(
                typeof(AudioIcon), new FrameworkPropertyMetadata(BitmapScalingMode.HighQuality));

            // Decorative by default: every call site pairs it with a button that
            // carries the accessible name, so the icon announcing itself as well
            // would read the label twice.
            IsTabStopProperty.OverrideMetadata(
                typeof(AudioIcon), new FrameworkPropertyMetadata(false));
            FocusableProperty.OverrideMetadata(
                typeof(AudioIcon), new FrameworkPropertyMetadata(false));
        }

        /// <summary>
        /// Glyph family: Home, Alarm, Config, Data, Vision, Setting, Running, Paused
        /// or Stopped. Combined with <see cref="State"/> to pick the file.
        /// </summary>
        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(
                "Glyph", typeof(string), typeof(AudioIcon),
                new FrameworkPropertyMetadata(null, OnSourceInputChanged));

        public string Glyph
        {
            get { return (string)GetValue(GlyphProperty); }
            set { SetValue(GlyphProperty, value); }
        }

        /// <summary>Which of the four DFM appearances to draw.</summary>
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register(
                "State", typeof(AudioIconState), typeof(AudioIcon),
                new FrameworkPropertyMetadata(AudioIconState.DeEnergized, OnSourceInputChanged));

        public AudioIconState State
        {
            get { return (AudioIconState)GetValue(StateProperty); }
            set { SetValue(StateProperty, value); }
        }

        /// <summary>
        /// An asset base name used verbatim, bypassing <see cref="Glyph"/> and
        /// <see cref="State"/> - for chips whose text is baked into the art
        /// (<c>Level2</c>, <c>HIVE_Running</c>, <c>No_login</c>, <c>Audio_Logo</c>).
        /// </summary>
        public static readonly DependencyProperty AssetProperty =
            DependencyProperty.Register(
                "Asset", typeof(string), typeof(AudioIcon),
                new FrameworkPropertyMetadata(null, OnSourceInputChanged));

        public string Asset
        {
            get { return (string)GetValue(AssetProperty); }
            set { SetValue(AssetProperty, value); }
        }

        private static readonly DependencyPropertyKey SourcePropertyKey =
            DependencyProperty.RegisterReadOnly(
                "Source", typeof(ImageSource), typeof(AudioIcon),
                new FrameworkPropertyMetadata(null));

        /// <summary>The resolved artwork. Read-only; the control template binds it.</summary>
        public static readonly DependencyProperty SourceProperty = SourcePropertyKey.DependencyProperty;

        public ImageSource Source
        {
            get { return (ImageSource)GetValue(SourceProperty); }
        }

        private static void OnSourceInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((AudioIcon)d).UpdateSource();
        }

        private void UpdateSource()
        {
            ImageSource resolved = !string.IsNullOrEmpty(Asset)
                ? AudioIcons.Load(Asset)
                : AudioIcons.Get(Glyph, State);

            SetValue(SourcePropertyKey, resolved);
        }
    }
}
