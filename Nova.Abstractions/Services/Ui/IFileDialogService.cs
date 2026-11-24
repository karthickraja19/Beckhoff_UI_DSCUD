namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The Windows open and save file dialogs, so view models can ask for a path without
    /// referencing WPF.
    /// </summary>
    public interface IFileDialogService
    {
        /// <summary>An existing file chosen by the operator, or null when cancelled.</summary>
        /// <param name="filter">A Windows filter, e.g. "CSV files (*.csv)|*.csv".</param>
        string OpenFile(string title, string filter);

        /// <summary>Where to save, chosen by the operator, or null when cancelled.</summary>
        string SaveFile(string title, string filter, string suggestedFileName);
    }
}
