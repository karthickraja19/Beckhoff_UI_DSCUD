using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HandyControl.Tools.Extension;

namespace Nova.Wpf.App.Views.Dialogs
{
    public enum AppDialogKind
    {
        Info = 0,
        Error = 1,
        Confirm = 2,
        Warning = 3,
    }

    public partial class AppDialogView : UserControl, IDialogResultable<bool>
    {
        public string DialogTitle { get; }
        public string DialogMessage { get; }
        public string PrimaryButtonText { get; }
        public string SecondaryButtonText { get; }
        public Visibility SecondaryVisibility { get; }
        public string IconText { get; }
        public Brush AccentBrush { get; }

        public bool Result { get; set; }
        public Action CloseAction { get; set; }

        public AppDialogView(string message, string title, AppDialogKind kind)
        {
            DialogMessage = message ?? string.Empty;
            DialogTitle = string.IsNullOrWhiteSpace(title) ? "Message" : title;

            switch (kind)
            {
                case AppDialogKind.Error:
                    IconText = "!";
                    AccentBrush = ResolveBrush("DangerBrush", Brushes.IndianRed);
                    PrimaryButtonText = "OK";
                    SecondaryButtonText = string.Empty;
                    SecondaryVisibility = Visibility.Collapsed;
                    break;

                case AppDialogKind.Warning:
                    IconText = "!";
                    AccentBrush = ResolveBrush("WarningBrush", Brushes.Orange);
                    PrimaryButtonText = "OK";
                    SecondaryButtonText = string.Empty;
                    SecondaryVisibility = Visibility.Collapsed;
                    break;

                case AppDialogKind.Confirm:
                    IconText = "?";
                    AccentBrush = ResolveBrush("PrimaryBrush", Brushes.DodgerBlue);
                    PrimaryButtonText = "Yes";
                    SecondaryButtonText = "No";
                    SecondaryVisibility = Visibility.Visible;
                    break;

                default:
                    IconText = "i";
                    AccentBrush = ResolveBrush("PrimaryBrush", Brushes.DodgerBlue);
                    PrimaryButtonText = "OK";
                    SecondaryButtonText = string.Empty;
                    SecondaryVisibility = Visibility.Collapsed;
                    break;
            }

            InitializeComponent();
            DataContext = this;
        }

        private static Brush ResolveBrush(string resourceKey, Brush fallback)
        {
            if (Application.Current != null)
            {
                var resource = Application.Current.TryFindResource(resourceKey) as Brush;
                if (resource != null)
                {
                    return resource;
                }
            }

            return fallback;
        }

        private void OnPrimaryClick(object sender, RoutedEventArgs e)
        {
            Result = true;
            CloseAction?.Invoke();
        }

        private void OnSecondaryClick(object sender, RoutedEventArgs e)
        {
            Result = false;
            CloseAction?.Invoke();
        }
    }
}
