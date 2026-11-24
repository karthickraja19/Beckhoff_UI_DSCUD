using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Machine.Interfaces;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// "CNC" tab of the Recipe screen. A CNC channel is selected (Data\CncRecipe.json), its
    /// X1, Y1, Z1, X2, Z2 (linear) and A, R (rotational) axes jog while their buttons are
    /// held, their live positions are shown, and the channel's recipe - a table of the
    /// positions named in the JSON, one column per axis - is typed, or taught row by row
    /// from the live positions, and saved. Save also writes the channel's positions into
    /// its CNC recipe in the PLC (IPlcRecipeService).
    /// </summary>
    public partial class CNCRecipeViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab, Nova.ViewModels.Common.IActivatableTab
    {
        /// <summary>The axes that belong to a CNC channel rather than being a servo of their own.</summary>
        private readonly HashSet<string> _cncAxisIds;

        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly IPlcRecipeService _plcRecipeService;
        private readonly IMachineOrchestrator _orchestrator;
        private readonly ICncHmiLauncher _cncHmi;
        private readonly Dictionary<string, IAxis> _axesById;

        private List<CncChannelRecipe> _channels = new List<CncChannelRecipe>();
        private bool _isActive;

        /// <summary>The live status of the channel's axes, followed while the tab is on screen.</summary>
        private List<IDisposable> _following = new List<IDisposable>();

        /// <summary>The axis jogging now, set before the jog starts so a release during the start still stops it.</summary>
        private IAxis _joggingAxis;

        public string Title => "CNC";

        public ObservableCollection<string> Channels { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private string _selectedChannel;

        /// <summary>The channel's axes in <see cref="CncChannelRecipe.AxisNames"/> order. Fixed: only their values change.</summary>
        public IReadOnlyList<CncAxisItem> Axes { get; }

        public CncAxisItem X1 => Axes[0];
        public CncAxisItem Y1 => Axes[1];
        public CncAxisItem Z1 => Axes[2];
        public CncAxisItem A => Axes[3];
        public CncAxisItem R => Axes[4];
        public CncAxisItem X2 => Axes[5];
        public CncAxisItem Z2 => Axes[6];

        /// <summary>Jog speed, mm/s for the linear axes and deg/s for A, R; refused above the axis parameter speed.</summary>
        [ObservableProperty]
        private string _jogSpeed = "10";

        // ---- Recipe table: Position (named in the JSON, read-only), then one column per axis ----
        public ObservableCollection<DynamicColumnSchema> Columns { get; }
        public ObservableCollection<DynamicGridRow> Rows { get; } = new ObservableCollection<DynamicGridRow>();

        [ObservableProperty]
        private DynamicGridRow _selectedRow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;

        /// <summary>Read by DynamicGridHelper's value cells: locked until Edit or Teach.</summary>
        public bool IsReadOnlyMode => !IsEditing;

        private const string PositionColumn = "Position";

        public CNCRecipeViewModel(
            IRecipeService recipeService,
            IDialogService dialogService,
            IAxisService axisService,
            IAxisMonitorService axisMonitor,
            IPlcRecipeService plcRecipeService,
            IMachineOrchestrator orchestrator,
            // The axes of the CNC channels themselves (CNC1_X1, ...), jogged through their channel.
            ICncAxisProvider cncAxes = null,
            // Beckhoff's CNC HMI, started from the button on this tab; without it the button is disabled.
            ICncHmiLauncher cncHmi = null)
        {
            _cncHmi = cncHmi;
            _recipeService = recipeService ?? throw new ArgumentNullException(nameof(recipeService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _plcRecipeService = plcRecipeService ?? throw new ArgumentNullException(nameof(plcRecipeService));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            if (axisService == null) throw new ArgumentNullException(nameof(axisService));
            if (axisMonitor == null) throw new ArgumentNullException(nameof(axisMonitor));

            _cncAxisIds = new HashSet<string>(
                (cncAxes != null && cncAxes.Axes != null ? cncAxes.Axes : new IAxis[0]).Select(a => a.ComponentId),
                StringComparer.OrdinalIgnoreCase);

            _axesById = (axisService.GetAllAxes() ?? new IAxis[0])
                .Concat(cncAxes != null && cncAxes.Axes != null ? cncAxes.Axes : new IAxis[0])
                .Where(axis => axis != null && !string.IsNullOrWhiteSpace(axis.ComponentId))
                .GroupBy(axis => axis.ComponentId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            Axes = CncChannelRecipe.AxisNames.Select(name => new CncAxisItem(name)).ToList();

            Columns = new ObservableCollection<DynamicColumnSchema>(
                new[] { new DynamicColumnSchema { Key = PositionColumn, Title = "Position", IsReadOnly = true, IsIdentifier = true, DataType = ColumnDataType.Text } }
                    .Concat(CncChannelRecipe.AxisNames.Select(name => new DynamicColumnSchema
                    {
                        Key = name,
                        Title = name + (CncChannelRecipe.IsRotational(name) ? " (deg)" : " (mm)"),
                        DataType = ColumnDataType.Numeric,
                    })));

            axisMonitor.AxisStatusUpdated += OnAxisStatusUpdated;

            LoadChannels();
        }

        // ---- Beckhoff CNC HMI -------------------------------------------------

        /// <summary>True when this machine has a CNC HMI launcher; the button is disabled without one.</summary>
        public bool CanOpenCncHmi => _cncHmi != null;

        /// <summary>Where this machine looks for the CNC HMI, for the button's tooltip.</summary>
        public string CncHmiToolTip => _cncHmi == null
            ? "The CNC HMI is not available."
            : "Open Beckhoff's CNC HMI (" + _cncHmi.ExePath + "). It runs in its own window; if it is already running it is brought to the front.";

        /// <summary>
        /// Starts Beckhoff's CNC HMI, or brings it forward when it is already running. This tab
        /// stays as it is - manual mode stays held while it is on screen - and the CNC HMI works
        /// through the PLC like any other client, so the PLC's own interlocks still apply.
        /// </summary>
        [RelayCommand]
        private async Task OpenCncHmi()
        {
            if (_cncHmi == null)
                return;

            CncHmiLaunchResult result = await Task.Run(() => _cncHmi.Launch());
            if (!result.Success)
                _dialogService.ShowError(result.Message, "CNC HMI");
        }

        partial void OnSelectedChannelChanged(string value)
        {
            StopJogCommand.Execute(null);
            ShowChannel(value);
        }

        /// <summary>
        /// The tab came on screen: manual mode on - the PLC takes jog commands from the HMI
        /// only in MANUAL - and the channel's axes followed.
        /// </summary>
        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            _orchestrator.SetManualMode(true);
            FollowAxes();
        }

        /// <summary>The tab left the screen: manual mode let go, as the Gantry tab does.</summary>
        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            StopJogCommand.Execute(null);
            _orchestrator.SetManualMode(false);
            foreach (IDisposable following in _following)
                following.Dispose();
            _following = new List<IDisposable>();
        }

        /// <summary>Leaving the tab abandons the edit: relock and reload, so the next visit shows the saved recipe.</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            LoadChannels();
        }

        private void LoadChannels()
        {
            string selected = SelectedChannel;

            try
            {
                _channels = _recipeService.LoadCncRecipe().ToList();
            }
            catch (Exception ex)
            {
                _channels = new List<CncChannelRecipe>();
                _dialogService.ShowWarning(ex.Message, "CNC Recipe");
            }

            Channels.Clear();
            foreach (CncChannelRecipe channel in _channels)
                Channels.Add(channel.Name);

            string next = Channels.FirstOrDefault(name => string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
                ?? Channels.FirstOrDefault();

            // The same channel again still reloads its positions, which the setter would skip.
            if (string.Equals(next, SelectedChannel, StringComparison.Ordinal))
                ShowChannel(next);
            else
                SelectedChannel = next;
        }

        /// <summary>Puts the channel's servos and saved positions on screen, and follows its axes.</summary>
        private void ShowChannel(string channelName)
        {
            CncChannelRecipe channel = FindChannel(channelName);

            int selectedIndex = SelectedRow != null ? Rows.IndexOf(SelectedRow) : 0;
            Rows.Clear();
            foreach (CncPositionRecipe position in channel?.Positions ?? new List<CncPositionRecipe>())
            {
                var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [PositionColumn] = position.Name ?? string.Empty };
                foreach (string axis in CncChannelRecipe.AxisNames)
                {
                    double value = 0d;
                    position.Values?.TryGetValue(axis, out value);
                    cells[axis] = FormatPosition(value);
                }

                Rows.Add(new DynamicGridRow(cells, Columns));
            }

            // Reloading after Save or Cancel keeps the operator on the row they were on.
            SelectedRow = Rows.Count == 0 ? null : Rows[Math.Max(0, Math.Min(selectedIndex, Rows.Count - 1))];

            foreach (CncAxisItem item in Axes)
            {
                CncAxisRecipe saved = FindAxis(channel, item.Name);
                item.Servo = (saved?.Servo ?? string.Empty).Trim();
                item.IsMapped = GetAxis(item.Servo) != null;

                // The monitor reports an axis when its status changes, which for one standing
                // still may be never, so start from what it has now.
                item.LivePosition = item.IsMapped ? GetAxis(item.Servo).ReadStatus()?.ActualPosition ?? 0d : 0d;
            }

            FollowAxes();
        }

        /// <summary>
        /// Follows the channel's mapped axes. The new set is followed before the old one is
        /// let go, so an axis in both stays subscribed.
        /// </summary>
        private void FollowAxes()
        {
            if (!_isActive)
                return;

            List<IDisposable> previous = _following;
            _following = Axes
                .Where(item => item.IsMapped)
                .Select(item => item.Servo)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(servo => (GetAxis(servo) as ILiveStatus)?.FollowStatus())
                .Where(following => following != null)
                .ToList();

            foreach (IDisposable following in previous)
                following.Dispose();
        }

        /// <summary>Raised on a background thread: only sets scalar properties.</summary>
        private void OnAxisStatusUpdated(object sender, AxisStatusSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ComponentId))
                return;

            foreach (CncAxisItem item in Axes)
            {
                if (item.IsMapped && string.Equals(item.Servo, snapshot.ComponentId, StringComparison.OrdinalIgnoreCase))
                    item.LivePosition = snapshot.ActualPosition;
            }
        }

        // ==================================================================
        // Teach / Edit / Save
        // ==================================================================

        [RelayCommand]
        private void Edit()
        {
            if (FindChannel(SelectedChannel) == null)
            {
                _dialogService.ShowInfo("No CNC channel is configured in Data\\CncRecipe.json.", "Recipe");
                return;
            }

            IsEditing = true;
        }

        /// <summary>
        /// Copies the live position of every mapped axis into the selected row; unmapped axes
        /// keep their value. Teach starts the edit itself, so jog - Teach - Save needs no Edit
        /// in between; nothing is kept until Save.
        /// </summary>
        [RelayCommand]
        private void Teach()
        {
            if (FindChannel(SelectedChannel) == null)
            {
                _dialogService.ShowInfo("No CNC channel is configured in Data\\CncRecipe.json.", "Recipe");
                return;
            }

            DynamicGridRow row = SelectedRow;
            if (row == null)
            {
                _dialogService.ShowInfo("Select a position to teach.", "Recipe");
                return;
            }

            List<CncAxisItem> mapped = Axes.Where(item => item.IsMapped).ToList();
            if (mapped.Count == 0)
            {
                _dialogService.ShowInfo("No axis of " + SelectedChannel + " is mapped to a servo in Data\\CncRecipe.json.", "Recipe");
                return;
            }

            IsEditing = true;
            foreach (CncAxisItem item in mapped)
                row[item.Name] = FormatPosition(item.LivePosition);
        }

        [RelayCommand]
        private async Task Save()
        {
            CncChannelRecipe channel = FindChannel(SelectedChannel);
            if (!IsEditing || channel == null)
                return;

            if (Rows.Any(r => r.HasErrors))
            {
                _dialogService.ShowError("Please correct invalid values before saving.", "Recipe");
                return;
            }

            // The rows as saved positions; a blank cell is 0.
            var positions = new List<CncPositionRecipe>(Rows.Count);
            foreach (DynamicGridRow row in Rows)
            {
                var position = new CncPositionRecipe { Name = row[PositionColumn] };
                foreach (string axis in CncChannelRecipe.AxisNames)
                {
                    string raw = (row[axis] ?? string.Empty).Trim();
                    double value = 0d;
                    if (raw.Length > 0 && !TryParse(raw, out value))
                    {
                        _dialogService.ShowError("Please enter a valid " + axis + " value for " + position.Name + ".", "Recipe");
                        return;
                    }

                    position.Values[axis] = value;
                }

                positions.Add(position);
            }

            if (!_dialogService.Confirm("Do you want to save the data?", "Confirmation"))
                return;

            // The saved channels with this one's positions replaced; the in-memory list is
            // only taken over once the file is written.
            List<CncChannelRecipe> updated = _channels
                .Select(c => ReferenceEquals(c, channel) ? WithPositions(c, positions) : c)
                .ToList();

            RecipeSaveResult result;
            try
            {
                result = _recipeService.SaveCncRecipe(updated);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("The CNC recipe could not be saved: " + ex.Message, "Recipe");
                return;
            }

            if (!result.IsSaved && !result.IsNoChanges)
            {
                _dialogService.ShowInfo(result.Message, "Recipe");
                return;
            }

            _channels = updated;
            IsEditing = false;

            // The PLC does not keep the CNC recipe, so it is written on every Save - also when
            // nothing changed, which resends positions the PLC lost.
            string channelName = channel.Name;
            try
            {
                await _plcRecipeService.WriteCncRecipeAsync(channelName);
                _dialogService.ShowInfo(result.Message + "\nWritten to the PLC.", "Recipe");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(result.Message + "\nNot written to the PLC: " + ex.Message, "Recipe");
            }
        }

        /// <summary>A copy of the channel, its axis mapping in AxisNames order, with the new positions.</summary>
        private static CncChannelRecipe WithPositions(CncChannelRecipe channel, List<CncPositionRecipe> positions)
        {
            return new CncChannelRecipe
            {
                Name = channel.Name,
                PlcChannel = channel.PlcChannel,
                Axes = CncChannelRecipe.AxisNames
                    .Select(name => new CncAxisRecipe { Axis = name, Servo = FindAxis(channel, name)?.Servo ?? string.Empty })
                    .ToList(),
                Positions = positions,
            };
        }

        // ==================================================================
        // Jog
        // ==================================================================

        [RelayCommand]
        private Task JogPositive(CncAxisItem axis) => StartJogAsync(axis, true);

        [RelayCommand]
        private Task JogNegative(CncAxisItem axis) => StartJogAsync(axis, false);

        [RelayCommand]
        private async Task StopJog()
        {
            IAxis axis = _joggingAxis;
            _joggingAxis = null;
            if (axis != null)
                await axis.JogStopAsync();
        }

        private async Task StartJogAsync(CncAxisItem item, bool forward)
        {
            if (item == null)
                return;

            IAxis axis = item.IsMapped ? GetAxis(item.Servo) : null;
            if (axis == null)
            {
                _dialogService.ShowError("Axis " + item.Name + " of " + SelectedChannel + " is not mapped to a servo.", "Recipe");
                return;
            }

            if (!TryParse(JogSpeed, out double speed) || speed <= 0d)
            {
                _dialogService.ShowError("Please enter a valid speed.", "Recipe");
                return;
            }

            AxisStatusSnapshot status = axis.ReadStatus();
            if (status == null || !status.IsEnabled)
            {
                // The CNC axes are enabled together by one PLC BOOL and this tab is the only
                // place that needs them, so it asks for them; a servo is enabled on its own tab.
                if (!_cncAxisIds.Contains(axis.ComponentId) || !await axis.EnableAsync())
                {
                    _dialogService.ShowError("Servo is disabled for axis " + item.Servo + ".", "Recipe");
                    return;
                }
            }

            if (status.SpeedLimit > 0d && speed > status.SpeedLimit)
            {
                _dialogService.ShowError("Please enter speed greater than 0 and less than axis parameter speed ("
                    + status.SpeedLimit.ToString("0.##", CultureInfo.InvariantCulture) + ").", "Recipe");
                return;
            }

            _joggingAxis = axis;
            try
            {
                await axis.JogStartAsync(forward, speed);
            }
            catch (Exception ex)
            {
                // The PLC did not take the jog: say why, and leave no key held.
                _joggingAxis = null;
                try { await axis.JogStopAsync(); } catch (Exception) { }
                _dialogService.ShowError("Axis " + item.Name + " could not be jogged: " + ex.Message, "Recipe");
            }
        }

        // ==================================================================
        // Helpers
        // ==================================================================

        private CncChannelRecipe FindChannel(string channelName)
        {
            return _channels.FirstOrDefault(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        }

        private static CncAxisRecipe FindAxis(CncChannelRecipe channel, string axisName)
        {
            return channel?.Axes?.FirstOrDefault(a => string.Equals((a.Axis ?? string.Empty).Trim(), axisName, StringComparison.OrdinalIgnoreCase));
        }

        private IAxis GetAxis(string axisId)
        {
            if (string.IsNullOrWhiteSpace(axisId))
                return null;

            return _axesById.TryGetValue(axisId, out IAxis axis) ? axis : null;
        }

        private static bool TryParse(string raw, out double value)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static string FormatPosition(double value)
            => value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
