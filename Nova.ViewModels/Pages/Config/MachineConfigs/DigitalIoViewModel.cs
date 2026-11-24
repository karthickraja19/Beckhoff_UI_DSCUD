using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Settings
{
    public partial class DigitalIoPointRow : ObservableObject
    {
       public int Bit { get; }
       public string Name { get; }
       public string Address { get; }

       /// <summary>
       /// What the I/O Diagnostics grid shows: the point's description in the selected
       /// language (Setting &gt; Config Description), or its name when it has none.
       /// </summary>
       [ObservableProperty]
       private string _displayName;

       [ObservableProperty]
       private bool _isOn;

       public DigitalIoPointRow(int bit, string name, string address, bool isOn)
       {
           Bit = bit;
           Name = name;
           Address = address;
           _displayName = UiLanguage.Point(name);
           _isOn = isOn;
       }
    }

    /// <summary>
    /// I/O Diagnostics &gt; Digital I/O — live input indicators and forceable outputs,
    /// each with its own name search. Rows are config-driven from Data\Config.csv
    /// through <see cref="IDigitalIOService"/>. The IO is read only while the tab is on
    /// screen: <see cref="Activate"/> starts the service reading it (when it is an
    /// <see cref="ILiveStatus"/>), and <see cref="Deactivate"/> stops it.
    /// </summary>
    public partial class DigitalIoViewModel : ObservableObject, IActivatableTab
    {
       private readonly IDigitalIOService _ioService;
       private IDisposable _following;
       private bool _isActive;

       public string Title => "Digital I/O";

       private readonly List<DigitalIoPointRow> _allInputs = new List<DigitalIoPointRow>();
       private readonly List<DigitalIoPointRow> _allOutputs = new List<DigitalIoPointRow>();
       private readonly Dictionary<int, DigitalIoPointRow> _inputsByBit = new Dictionary<int, DigitalIoPointRow>();
       private readonly Dictionary<int, DigitalIoPointRow> _outputsByBit = new Dictionary<int, DigitalIoPointRow>();

       public ObservableCollection<DigitalIoPointRow> Inputs { get; } = new ObservableCollection<DigitalIoPointRow>();
       public ObservableCollection<DigitalIoPointRow> Outputs { get; } = new ObservableCollection<DigitalIoPointRow>();

       [ObservableProperty]
       private string _inputSearch = "";

        [ObservableProperty]
        private string _outputSearch = "";

        [ObservableProperty]
        private string _activeCountText;

        [ObservableProperty]
        private string _forcedCountText;

        public DigitalIoViewModel(IDigitalIOService ioService)
        {
            _ioService = ioService ?? throw new ArgumentNullException(nameof(ioService));

            foreach (var input in _ioService.ReadInputs())
            {
                var row = CreateRow(input);
                _allInputs.Add(row);
                _inputsByBit[input.Bit] = row;
            }

            foreach (var output in _ioService.ReadOutputs())
            {
                var row = CreateRow(output);
                _allOutputs.Add(row);
                _outputsByBit[output.Bit] = row;
            }

            RefreshInputs();
            RefreshOutputs();
            RecalculateCounts();

            // A description saved on the Setting page, or a language change.
            UiLanguage.DescriptionsChanged += (sender, e) => RefreshDisplayNames();
        }

        private void RefreshDisplayNames()
        {
            foreach (DigitalIoPointRow row in _allInputs)
                row.DisplayName = UiLanguage.Point(row.Name);
            foreach (DigitalIoPointRow row in _allOutputs)
                row.DisplayName = UiLanguage.Point(row.Name);
        }

        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            _ioService.IOChanged += OnIoChanged;
            _following = (_ioService as ILiveStatus)?.FollowStatus();

            // What the IO is now, not what was left from the last visit.
            OnIoChanged(this, EventArgs.Empty);
        }

        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            _ioService.IOChanged -= OnIoChanged;
            _following?.Dispose();
            _following = null;
        }

        private static DigitalIoPointRow CreateRow(IOPointSnapshot point) =>
            new DigitalIoPointRow(point.Bit, point.Name, point.Address, point.IsOn);

        partial void OnInputSearchChanged(string value) => RefreshInputs();

        partial void OnOutputSearchChanged(string value) => RefreshOutputs();

        private static bool Matches(DigitalIoPointRow s, string search) =>
            string.IsNullOrWhiteSpace(search) ||
            s.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
            (s.DisplayName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
            s.Address.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private void OnIoChanged(object sender, EventArgs e)
        {
            foreach (var input in _ioService.ReadInputs())
            {
                if (_inputsByBit.TryGetValue(input.Bit, out var row))
                {
                    row.IsOn = input.IsOn;
                }
            }

            foreach (var output in _ioService.ReadOutputs())
            {
                if (_outputsByBit.TryGetValue(output.Bit, out var row))
                {
                    row.IsOn = output.IsOn;
                }
            }

            RecalculateCounts();
        }

        private void RefreshInputs()
        {
            Inputs.Clear();
            foreach (var s in _allInputs)
                if (Matches(s, InputSearch)) Inputs.Add(s);
        }

        private void RefreshOutputs()
        {
            Outputs.Clear();
            foreach (var s in _allOutputs)
                if (Matches(s, OutputSearch)) Outputs.Add(s);
        }

        private void RecalculateCounts()
        {
            int active = 0;
            foreach (var s in _allInputs) if (s.IsOn) active++;
            int forced = 0;
            foreach (var s in _allOutputs) if (s.IsOn) forced++;

            ActiveCountText = active + " Active";
            ForcedCountText = forced + " Forced";
        }

        [RelayCommand]
        private void ClearInputSearch() => InputSearch = "";

        [RelayCommand]
        private void ClearOutputSearch() => OutputSearch = "";

        [RelayCommand]
        private async System.Threading.Tasks.Task ToggleOutput(DigitalIoPointRow row)
        {
            if (row == null)
            {
                return;
            }

            await _ioService.WriteOutputAsync(row.Bit, !row.IsOn);
        }
    }
}
