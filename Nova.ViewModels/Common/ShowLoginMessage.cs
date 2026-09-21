namespace Nova.ViewModels.Common
{
    /// <summary>
    /// Asks the window to swap to the login screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sent by the Audio/Home indicator bar when the operator clicks the access-level
    /// chip while signed out. The chip is the login control in the DFM - slide 44 draws
    /// the arrow from it to the login pop-up, captioned "Click for pop up" - but the
    /// indicator bar has no way to reach the window's view swap directly, and giving it
    /// a reference to <c>MainWindowViewModel</c> would make the shell depend on its own
    /// host.
    /// </para>
    /// <para>
    /// A message rather than a shared service because this is a single one-way
    /// notification with exactly one recipient.
    /// </para>
    /// </remarks>
    public sealed class ShowLoginMessage
    {
    }
}
