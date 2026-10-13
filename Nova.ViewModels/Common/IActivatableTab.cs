namespace Nova.ViewModels.Common
{
    /// <summary>
    /// A tab that does work only while it is on screen - following the live status of the
    /// devices it shows, for one. Its host calls <see cref="Activate"/> when the tab comes
    /// into view and <see cref="Deactivate"/> when it goes out of view, including when the
    /// page holding it is left. Both must be safe to call more than once in a row.
    /// </summary>
    public interface IActivatableTab
    {
        void Activate();

        void Deactivate();
    }
}
