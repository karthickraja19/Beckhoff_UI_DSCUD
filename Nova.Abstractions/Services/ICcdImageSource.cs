using System;
using System.Collections.Generic;

namespace Nova.Abstractions.Services
{
    /// <summary>One capture of one camera, for the Vision page (slide 61).</summary>
    public sealed class CcdCapture
    {
        /// <summary>The camera, e.g. "CCD1".</summary>
        public string Camera { get; set; }

        /// <summary>Full path of the image file.</summary>
        public string ImagePath { get; set; }

        public DateTime Time { get; set; }

        /// <summary>The unit the capture belongs to, when known.</summary>
        public string UnitSn { get; set; }

        /// <summary>"OK" / "NG" when known.</summary>
        public string Result { get; set; }

        /// <summary>
        /// "Displays information for capture" (slide 61): angle, gaps, brightness, exposure
        /// and whatever else the vision software wrote alongside the image, in its order.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Information { get; set; }
    }

    /// <summary>
    /// The newest capture of each camera, for the Vision page: "shows all CCD images for
    /// current running units" (slide 61).
    /// </summary>
    public interface ICcdImageSource
    {
        /// <summary>The cameras, in the order the page shows them.</summary>
        IReadOnlyList<string> Cameras { get; }

        /// <summary>Where the images come from, for the page to say.</summary>
        string Description { get; }

        /// <summary>The newest capture of a camera, or null.</summary>
        CcdCapture Latest(string camera);

        /// <summary>Raised with the camera's name when it has a new capture. May be raised on any thread.</summary>
        event EventHandler<string> CaptureChanged;
    }
}
