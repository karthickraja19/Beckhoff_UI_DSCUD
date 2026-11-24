using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>One image the vision software saved, for the Vision page (slide 61).</summary>
    public sealed class CcdCapture
    {
        /// <summary>Full path of the image file.</summary>
        public string ImagePath { get; set; }

        /// <summary>When the image was taken: from its file name when it carries a time, otherwise when the file was written.</summary>
        public DateTime Time { get; set; }

        /// <summary>The unit the capture belongs to, when its file name says.</summary>
        public string UnitSn { get; set; }

        /// <summary>"OK" / "NG" when its file name says.</summary>
        public string Result { get; set; }

        /// <summary>
        /// "Displays information for capture" (slide 61): angle, gaps, brightness, exposure
        /// and whatever else the vision software wrote alongside the image, in its order.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Information { get; set; }
    }

    /// <summary>
    /// The images the vision software is saving, for the Vision page: "shows all CCD images
    /// for current running units" (slide 61). The images are in one folder; the source does
    /// not need to know which camera took which.
    /// </summary>
    public interface ICcdImageSource
    {
        /// <summary>Where the images come from, for the page to say.</summary>
        string Description { get; }

        /// <summary>The newest images, newest first; fewer when there are fewer.</summary>
        IReadOnlyList<CcdCapture> Newest(int count);

        /// <summary>Raised when the newest images changed. May be raised on any thread.</summary>
        event EventHandler ImagesChanged;
    }
}
