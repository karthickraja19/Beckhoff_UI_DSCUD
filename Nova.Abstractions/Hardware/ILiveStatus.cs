using System;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// A device whose live status is only kept up to date while something shows it. A
    /// screen calls <see cref="FollowStatus"/> for each device it shows and disposes the
    /// handle when the device goes out of view - the tab is left, or another device is
    /// selected. A device that does not implement this always has its status.
    /// </summary>
    public interface ILiveStatus
    {
        /// <summary>
        /// Keeps the device's status up to date until the returned handle is disposed. Each
        /// call counts separately, so two screens showing the same device do not interfere.
        /// </summary>
        IDisposable FollowStatus();
    }
}
