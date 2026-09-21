using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Nova.ViewModels.Audio
{
    /// <summary>
    /// The Audio SW DFM v5.4 colours, as Skia paints for the LiveCharts series the
    /// Audio pages build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same values live in XAML as <c>Audio.*</c> brushes
    /// (Nova.Wpf.UI/Styles/AudioTokens.xaml). They are duplicated here rather than
    /// read from the resource dictionary because these view models are
    /// netstandard2.0 and cannot see a WPF <c>ResourceDictionary</c> - and because a
    /// chart built on a background thread must not touch one anyway.
    /// </para>
    /// <para>
    /// Duplication is the trade, so both copies carry the DFM's own RGB triplet in a
    /// comment and any change has to be made in both. Everything derives from the
    /// <see cref="SKColor"/> constants below, so there is exactly one literal per
    /// colour in this file.
    /// </para>
    /// </remarks>
    public static class AudioChartPalette
    {
        // ---- HIVE machine state (DFM slide 40) ----

        /// <summary>State 1, Running. DFM: R 211 G 235 B 115.</summary>
        public static readonly SKColor Running = new SKColor(211, 235, 115);

        /// <summary>State 2, Idle. DFM: R 235 G 255 B 254.</summary>
        public static readonly SKColor Idle = new SKColor(235, 255, 254);

        /// <summary>State 5, Downtime. DFM: R 235 G 115 B 115.</summary>
        public static readonly SKColor Downtime = new SKColor(235, 115, 115);

        /// <summary>Planned downtime, sampled from the DFM's HIVE_Plan_DT chip.</summary>
        public static readonly SKColor PlannedDowntime = new SKColor(255, 215, 212);

        /// <summary>Engineering time, sampled from the DFM's HIVE_Engineering chip.</summary>
        public static readonly SKColor Engineering = new SKColor(204, 171, 216);

        // ---- Downtime statistics Top 10 (DFM slide 40) ----
        // Ordinal, not semantic: index 0 is the largest contributor, not a category.

        public static readonly SKColor[] Top10 =
        {
            new SKColor(111, 121, 128), // 6F7980
            new SKColor( 84, 151, 193), // 5497C1
            new SKColor( 83, 172, 122), // 53AC7A
            new SKColor(248, 195,  92), // F8C35C
            new SKColor(243, 150,  91), // F3965B
            new SKColor(228,  94, 105), // E45E69
            new SKColor(125, 114, 187), // 7D72BB
            new SKColor( 76, 170, 233), // 4CAAE9
            new SKColor(102, 217,  56), // 66D938
            new SKColor(194,  72, 134)  // C24886
        };

        // ---- Chrome ----

        /// <summary>Axis lines, ticks and labels: HIG secondaryLabel.</summary>
        public static readonly SKColor AxisLine = new SKColor(60, 60, 67, 153);

        /// <summary>Gridlines: one step lighter again, so they sit behind the data.</summary>
        public static readonly SKColor GridLine = new SKColor(60, 60, 67, 48);

        /// <summary>A hairline stroke around a filled bar or slice so adjacent fills separate.</summary>
        public static readonly SKColor Stroke = new SKColor(255, 255, 255);

        /// <summary>Series colour for a measured output line (Data page DS/NS output).</summary>
        public static readonly SKColor SeriesPrimary = new SKColor(84, 151, 193);

        /// <summary>Second series in a two-series comparison (Data page day/night shift).</summary>
        public static readonly SKColor SeriesSecondary = new SKColor(111, 121, 128);

        /// <summary>
        /// A solid fill paint, optionally with the hairline separator stroke that keeps
        /// stacked bars and pie slices from bleeding into each other.
        /// </summary>
        public static SolidColorPaint Fill(SKColor color)
        {
            return new SolidColorPaint(color);
        }

        /// <summary>A stroke paint of the given width.</summary>
        public static SolidColorPaint Line(SKColor color, float width)
        {
            return new SolidColorPaint(color) { StrokeThickness = width };
        }

        /// <summary>
        /// The Top 10 colour for a zero-based rank, wrapping past ten so a longer list
        /// still renders instead of throwing.
        /// </summary>
        public static SKColor TopN(int zeroBasedRank)
        {
            if (zeroBasedRank < 0)
                zeroBasedRank = 0;

            return Top10[zeroBasedRank % Top10.Length];
        }
    }
}
