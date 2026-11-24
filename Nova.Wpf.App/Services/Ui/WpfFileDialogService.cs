using Microsoft.Win32;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Services
{
    /// <summary><see cref="IFileDialogService"/> over the Win32 file dialogs.</summary>
    public sealed class WpfFileDialogService : IFileDialogService
    {
        public string OpenFile(string title, string filter)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true,
                RestoreDirectory = true,
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string SaveFile(string title, string filter, string suggestedFileName)
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = suggestedFileName,
                RestoreDirectory = true,
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
