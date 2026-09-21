using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.ViewModels.Pages.Manual;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>
    /// Calibration &gt; Robot — select a trigger command, connect, trigger, and watch the
    /// received data stream. Mirrors the V3 robot calibration group.
    /// </summary>
    public partial class CalibrationRobotViewModel : ObservableObject
    {
        public string Title => "Robot";

        public ObservableCollection<string> Commands { get; }

        [ObservableProperty]
        private string _selectedCommand;

        public ObservableCollection<CameraConsoleLine> DataStream { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CommStatusText))]
        [NotifyPropertyChangedFor(nameof(CanConnect))]
        private bool _isConnected;

        public bool CanConnect => !IsConnected;
        public string CommStatusText => IsConnected ? "COMM OK" : "COMM STATUS";

        public CalibrationRobotViewModel()
        {
            Commands = new ObservableCollection<string>
            {
                "Command_1 (Initialize Axis)",
                "Command_2 (Home All Axes)",
                "Command_3 (Move to Calib Point)",
                "Command_4 (Read Position)",
            };
            _selectedCommand = Commands[0];

            DataStream = new ObservableCollection<CameraConsoleLine>();
        }

        [RelayCommand]
        private void Connect()
        {
            if (IsConnected) return;
            IsConnected = true;
            DataStream.Add(new CameraConsoleLine("CONNECT ROBOT..."));
            DataStream.Add(new CameraConsoleLine("ACK LINK_ESTABLISHED", isAck: true));
            // TODO: IRobotClient.Connect()
        }

        [RelayCommand]
        private void Trigger()
        {
            if (!IsConnected) return;
            DataStream.Add(new CameraConsoleLine("SEND " + (SelectedCommand ?? "CMD")));
            DataStream.Add(new CameraConsoleLine("ACK DONE", isAck: true));
            // TODO: IRobotClient.Send(SelectedCommand)
        }

        [RelayCommand]
        private void ManualReset()
        {
            IsConnected = false;
            DataStream.Clear();
            // TODO: IRobotClient.Reset()
        }
    }
}
