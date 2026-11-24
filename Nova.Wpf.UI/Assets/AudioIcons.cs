using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nova.Abstractions.Data;

namespace Nova.Wpf.UI.Assets
{
    /// <summary>
    /// Resolves the icon artwork shipped with Audio SW DFM v5.4 to a cached
    /// <see cref="ImageSource"/>.
    /// <para>
    /// The art is supplied as one PNG per glyph per state, so which file to show is a
    /// naming question, not a drawing question. Keeping that naming in one place means
    /// a view never spells an asset name and the shell never carries a trigger per
    /// state per button (which would be 24 triggers for the indicator bar alone).
    /// </para>
    /// <para>
    /// The originals are ~4000px, ~400KB each. They are committed under
    /// <c>Assets/Icons</c> downsampled to a 192px square (glyphs) or 96px tall
    /// (chips) - still 3x the largest size anything here is drawn at, and ~230KB for
    /// the whole set instead of ~14MB.
    /// </para>
    /// </summary>
    public static class AudioIcons
    {
        private const string PackRoot = "pack://application:,,,/Nova.Wpf.UI;component/Assets/Icons/";

        // BitmapImage is freezable and the same glyph is shown by several buttons, so
        // each file is decoded once for the life of the process. Guarded because a
        // binding can be evaluated from a background thread during a layout pass.
        private static readonly Dictionary<string, ImageSource> _cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _gate = new object();

        /// <summary>
        /// The asset for one glyph in one state.
        /// </summary>
        /// <param name="glyph">
        /// Home, Alarm, Config, Data, Vision, Setting, Running, Paused or Stopped.
        /// </param>
        public static ImageSource Get(string glyph, AudioIconState state)
        {
            string assetName = ResolveName(glyph, state);
            return assetName == null ? null : Load(assetName);
        }

        /// <summary>
        /// Maps a glyph and state to the supplied file's base name.
        /// </summary>
        /// <remarks>
        /// The supplied set is nearly - but not quite - regular. Every state suffixes
        /// its glyph (<c>Home_Energized</c>, <c>Home_De_energized</c>,
        /// <c>Home_Disabled</c>) EXCEPT the energized pause button, which ships as
        /// <c>Energized_Paused.png</c> with the two halves the other way round. That
        /// one exception is handled here rather than by renaming the file, so the
        /// assets in the repository still match the names in the DFM package and can
        /// be diffed against a future drop.
        /// </remarks>
        public static string ResolveName(string glyph, AudioIconState state)
        {
            if (string.IsNullOrEmpty(glyph))
                return null;

            if (state == AudioIconState.Alerted)
            {
                // Only the Alarm bell has alerted art (DFM slide 39). Anything else
                // asking for it is a caller bug, but it must not blank the button
                // mid-run, so it falls back to the energized glyph.
                return string.Equals(glyph, "Alarm", StringComparison.OrdinalIgnoreCase)
                    ? "Alarm_Alerted"
                    : glyph + "_Energized";
            }

            if (state == AudioIconState.Energized &&
                string.Equals(glyph, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                return "Energized_Paused";
            }

            switch (state)
            {
                case AudioIconState.Energized:   return glyph + "_Energized";
                case AudioIconState.DeEnergized: return glyph + "_De_energized";
                case AudioIconState.Disabled:    return glyph + "_Disabled";
                default:                         return glyph + "_De_energized";
            }
        }

        /// <summary>
        /// The chip artwork for one HIVE state (DFM slide 39, "HIVE Status").
        /// </summary>
        public static string ResolveHiveAsset(HiveState state)
        {
            switch (state)
            {
                case HiveState.Running:         return "HIVE_Running";
                case HiveState.Idle:            return "HIVE_Idle";
                case HiveState.Downtime:        return "HIVE_DT";
                default:                        return "HIVE_Disabled";
            }
        }

        /// <summary>
        /// The chip artwork for one access level (DFM slide 39, "User Level").
        /// </summary>
        public static string ResolveLevelAsset(AccessLevel level)
        {
            switch (level)
            {
                case AccessLevel.Level1: return "Level1";
                case AccessLevel.Level2: return "Level2";
                case AccessLevel.Level3: return "Level3";
                default:                 return "No_login";
            }
        }

        /// <summary>
        /// One asset by its base name, with no state logic - for the artwork that is
        /// already a finished chip rather than a glyph in a set: <c>Level1</c>,
        /// <c>Level2</c>, <c>Level3</c>, <c>No_login</c>, <c>HIVE_Running</c>,
        /// <c>HIVE_Idle</c>, <c>HIVE_DT</c>, <c>HIVE_Plan_DT</c>,
        /// <c>HIVE_Engineering</c>, <c>HIVE_Disabled</c>, <c>Audio_Logo</c>.
        /// </summary>
        public static ImageSource Load(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;

            lock (_gate)
            {
                ImageSource cached;
                if (_cache.TryGetValue(assetName, out cached))
                    return cached;

                ImageSource loaded = null;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(PackRoot + assetName + ".png", UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    loaded = bitmap;
                }
                catch (Exception)
                {
                    // A missing or corrupt asset must not take the shell down - the
                    // operator would lose the machine controls along with the icon.
                    // Cached as null so a broken name is not retried on every layout.
                }

                _cache[assetName] = loaded;
                return loaded;
            }
        }
    }
}
