namespace Nova.ViewModels.Common
{
    /// <summary>
    /// A tab whose grid is locked until Edit is pressed and unlocked again by Save.
    ///
    /// Every page view model in the app is a DI singleton, so a tab that was left in
    /// edit mode was still in edit mode when the operator came back to it — QA saw the
    /// Settings and Gantry grids stay editable after navigating away and returning, with
    /// whatever half-typed values were in the cells. Leaving a tab has to end the edit,
    /// and the tab hosts are the only things that know when that happens, so they call
    /// <see cref="CancelEdit"/> on the tab being left.
    /// </summary>
    public interface IEditableTab
    {
        /// <summary>True while the grid is unlocked.</summary>
        bool IsEditing { get; }

        /// <summary>
        /// Ends edit mode and discards anything typed since Edit was pressed, by
        /// reloading the tab from its store. Must be safe to call when not editing —
        /// the hosts call it on every tab change without checking.
        /// </summary>
        void CancelEdit();
    }
}
