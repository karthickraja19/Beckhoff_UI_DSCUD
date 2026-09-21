using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>One device row in a connection grid (TCP: Name/IP/Port; Serial: Name/COM/Baud).</summary>
    public partial class ConnectionRow : ObservableObject
    {
        public string Name { get; }

        [ObservableProperty]
        private string _address;

        [ObservableProperty]
        private string _port;

        public ConnectionRow(string name, string address, string port)
        {
            Name = name;
            _address = address;
            _port = port;
        }
    }
}
