namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Frame-agnostic contract for modal dialogs (errors, info, confirmation).
    /// </summary>
    public interface IDialogService
    {
        void ShowError(string message, string title);
        void ShowWarning(string message, string title);
        void ShowInfo(string message, string title);
        bool Confirm(string message, string title);
    }
}
