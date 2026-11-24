using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using HandyControl.Controls;
using HandyControl.Tools.Extension;
using Nova.Abstractions.Services;
using Nova.Wpf.App.Data;
using Nova.Wpf.App.Views.Dialogs;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// WPF dialogs, callable from any thread.
    ///
    /// Callers reach these from wherever they happen to be — the axis poll thread, the
    /// IO poll thread, a V3 monitor loop, or a vendor library's own worker.
    /// HandyControl's MessageBox builds a WPF Window, which throws on a thread-pool
    /// (MTA) thread, so every call is marshalled onto the UI dispatcher first.
    ///
    /// The void methods deliberately do NOT block a background caller: a modal held
    /// open by an operator would otherwise stall the vendor thread that reported the
    /// fault, and the device library behind it. Confirm must block, because the
    /// caller needs the answer.
    /// </summary>
    public class WpfDialogService : IDialogService
    {
        private static Dispatcher UiDispatcher => Application.Current?.Dispatcher;

        public void ShowError(string message, string title)
            => Post(() => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));

        public void ShowWarning(string message, string title)
            => Post(() => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning));

        public void ShowInfo(string message, string title)
            => Post(() => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information));

        public bool Confirm(string message, string title)
            => Send(() => Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);

        private static void Post(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            Dispatcher dispatcher = UiDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.BeginInvoke(action);
        }

        private static T Send<T>(Func<T> function)
        {
            if (function == null)
            {
                throw new ArgumentNullException(nameof(function));
            }

            Dispatcher dispatcher = UiDispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return function();
            }

            return dispatcher.Invoke(function);
        }

        private static MessageBoxResult Show(string message, string title,
            MessageBoxButton buttons, MessageBoxImage icon)
        {
            if (buttons == MessageBoxButton.YesNo)
            {
                bool confirmed = ShowDialog(message, title, AppDialogKind.Confirm);
                return confirmed ? MessageBoxResult.Yes : MessageBoxResult.No;
            }

            AppDialogKind kind = icon == MessageBoxImage.Error
                ? AppDialogKind.Error
                : (icon == MessageBoxImage.Warning ? AppDialogKind.Warning : AppDialogKind.Info);

            ShowDialog(message, title, kind);
            return MessageBoxResult.OK;
        }

        private static bool ShowDialog(string message, string title, AppDialogKind kind)
        {
            // Dual language: the view models write their messages in English.
            var localization = Nova.Wpf.UI.Localization.LocalizationSource.Instance;
            var view = new AppDialogView(localization.Translate(message), localization.Translate(title), kind);
            var dialog = Dialog.Show(view, DialogTokens.MainWindow);
            var resultTask = dialog.GetResultAsync<bool>();
            return WaitForResult(resultTask);
        }

        private static T WaitForResult<T>(Task<T> task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (task.IsCompleted) return task.GetAwaiter().GetResult();

            Dispatcher dispatcher = UiDispatcher;
            if (dispatcher == null || !dispatcher.CheckAccess())
            {
                return task.GetAwaiter().GetResult();
            }

            var frame = new DispatcherFrame();
            task.ContinueWith(
                _ => frame.Continue = false,
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
            return task.GetAwaiter().GetResult();
        }
    }
}
