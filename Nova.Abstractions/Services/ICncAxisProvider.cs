using System.Collections.Generic;
using Nova.Abstractions.Hardware;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The axes of the CNC channels (the two dispensing stations), for the Recipe page's CNC
    /// tab to jog and teach. They are not on the Manual page's Axis tab: a CNC axis is jogged
    /// through its channel, not driven as a single servo.
    /// </summary>
    public interface ICncAxisProvider
    {
        /// <summary>
        /// Every CNC axis, its <see cref="IAxis.ComponentId"/> being what a channel's axis
        /// names as its "Servo" in Data\CncRecipe.json (e.g. CNC1_X1).
        /// </summary>
        IReadOnlyList<IAxis> Axes { get; }
    }
}
