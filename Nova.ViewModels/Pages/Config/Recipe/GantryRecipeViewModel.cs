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
    /// "Gantry" tab of the Recipe screen. A gantry (Data\Gantry.json: Name, PlcGantry) has its
    /// recipe in its own file in the data folder, {Name}.xlsx or {Name}.csv: a row per
    /// position and a column group per servo - Position, Speed, Acc, Dec. Gantry.json's
    /// optional Axes give a servo its type (X, Y, Z, R: its jog button) and the order it
    /// moves in. Move drives each servo to its own value, in MovementPriority order
    /// (SafePositionPriority for a "safe" position; column order where none is given); Teach
    /// takes each servo's live position; Save writes the file and then the gantry's recipe
    /// to the PLC (IPlcRecipeService). Below the table: single-axis servo motion, a jog pad
    /// driving the gantry's X, Y, Z and R servos, and loadcells.
    /// </summary>
    public partial class GantryRecipeViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab, Nova.ViewModels.Common.IActivatableTab
    {
        private bool _isActive;

        /// <summary>The live status of the axes the page shows and drives, followed while it is on screen.</summary>
        private List<IDisposable> _following = new List<IDisposable>();

        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly IAxisService _axisService;
        private readonly IAxisMonitorService _axisMonitor;
        private readonly ILoadCellMonitorService _loadCellMonitor;
        private readonly IPlcRecipeService _plcRecipeService;
        private readonly IMachineOrchestrator _orchestrator;
        private readonly Dictionary<string, IAxis> _axesById;
        private readonly Dictionary<string, LoadCellItem> _loadCellsByAlias;
        private readonly Dictionary<string, GantryModuleDefinition> _moduleDefinitions;
        private readonly IReadOnlyDictionary<string, double> _axisSpeedLimits;
        private string _mappedXAxisId = string.Empty;
        private string _mappedYAxisId = string.Empty;
        private string _mappedZAxisId = string.Empty;
        private string _mappedRAxisId = string.Empty;
        private string _activeJogAxisId = string.Empty;

        public string Title => "Gantry";

        // ---- Module selection + position table ----
        public ObservableCollection<string> GantryModules { get; }

        [ObservableProperty]
        private string _selectedGantryModule;

        public ObservableCollection<DynamicColumnSchema> Columns { get; } = new ObservableCollection<DynamicColumnSchema>();
        public ObservableCollection<DynamicGridRow> Rows { get; }

        [ObservableProperty]
        private DynamicGridRow _selectedRow;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanEdit))]
        [NotifyPropertyChangedFor(nameof(IsReadOnlyMode))]
        private bool _isEditing;

        public bool CanEdit => !IsEditing;
        public bool IsReadOnlyMode => !IsEditing;

        // ---- Servo motion ----
        public ObservableCollection<string> Axes { get; }

        [ObservableProperty]
        private string _selectedAxis;

        public ObservableCollection<string> MotionModes { get; }

        [ObservableProperty]
        private string _selectedMotionMode;

        [ObservableProperty]
        private string _speed = "150";

        [ObservableProperty]
        private string _targetPosition = "0.00";

        [ObservableProperty]
        private bool _canJogX;

        [ObservableProperty]
        private bool _canJogY;

        [ObservableProperty]
        private bool _canJogZ;

        [ObservableProperty]
        private bool _canJogR;

        // The jog pad drives the axes mapped from the selected module's
        // MovementPriority, not the axis in the SELECT AXIS combo (which is for the
        // Move/Stop card beside it). Surfacing the mapped names lets the operator see
        // on the button which axis it will actually move, rather than finding out.
        [ObservableProperty]
        private string _xAxisName = "";

        [ObservableProperty]
        private string _yAxisName = "";

        [ObservableProperty]
        private string _zAxisName = "";

        [ObservableProperty]
        private string _rAxisName = "";

        // ---- Jog pad live positions ----
        [ObservableProperty]
        private double _xPos;

        [ObservableProperty]
        private double _yPos;

        [ObservableProperty]
        private double _zPos;

        [ObservableProperty]
        private double _rPos;

        // ---- Loadcells ----
        public ObservableCollection<LoadCellItem> LoadCells { get; }

        public GantryRecipeViewModel(
            IRecipeService recipeService,
            IDialogService dialogService,
            IAxisService axisService,
            IAxisMonitorService axisMonitor,
            ILoadCellMonitorService loadCellMonitor,
            IPlcRecipeService plcRecipeService,
            IMachineOrchestrator orchestrator)
        {
            _recipeService = recipeService ?? throw new System.ArgumentNullException(nameof(recipeService));
            _plcRecipeService = plcRecipeService ?? throw new ArgumentNullException(nameof(plcRecipeService));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _dialogService = dialogService ?? throw new System.ArgumentNullException(nameof(dialogService));
            _axisService = axisService ?? throw new ArgumentNullException(nameof(axisService));
            _axisMonitor = axisMonitor ?? throw new ArgumentNullException(nameof(axisMonitor));
            _loadCellMonitor = loadCellMonitor ?? throw new ArgumentNullException(nameof(loadCellMonitor));

            _axisSpeedLimits = _recipeService.GetAxisSpeedLimits();
            _moduleDefinitions = new Dictionary<string, GantryModuleDefinition>(StringComparer.OrdinalIgnoreCase);
            _axesById = BuildAxisLookup(_axisService.GetAllAxes());
            _loadCellsByAlias = new Dictionary<string, LoadCellItem>(StringComparer.OrdinalIgnoreCase);

            GantryModules = new ObservableCollection<string>(_recipeService.GetGantryModules());
            foreach (string moduleName in GantryModules)
            {
                var definition = _recipeService.GetGantryModuleDefinition(moduleName);
                _moduleDefinitions[moduleName] = definition;
            }

            Rows = new ObservableCollection<DynamicGridRow>();

            Axes = new ObservableCollection<string>(_axesById.Keys);
            SelectedAxis = Axes.Count > 0 ? Axes[0] : string.Empty;

            MotionModes = new ObservableCollection<string> { "Absolute", "Relative" };
            SelectedMotionMode = MotionModes[0];

            LoadCells = new ObservableCollection<LoadCellItem>
            {
                new LoadCellItem("LOADCELL 1"),
                new LoadCellItem("LOADCELL 2"),
            };
            RegisterLoadCellAliases(LoadCells[0],
                "Robot_LoadCell",
                "AI0_0_Robot_Load_Cell",
                "Robot_Load_Cell",
                "LoadCell1",
                "LoadCell_1");
            RegisterLoadCellAliases(LoadCells[1],
                "Pressing_LoadCell",
                "AI0_1_Roller_Module_Load_Cell_1",
                "Roller_Module_Load_Cell_1",
                "AI0_2_Roller_Module_Load_Cell_2",
                "Roller_Module_Load_Cell_2",
                "LoadCell2",
                "LoadCell_2");

            _axisMonitor.AxisStatusUpdated += OnAxisStatusUpdated;
            _loadCellMonitor.LoadCellStatusUpdated += OnLoadCellStatusUpdated;
            ApplyLoadCellSnapshots(_loadCellMonitor.ReadAll());

            if (GantryModules.Count > 0)
            {
                SelectedGantryModule = GantryModules[0];
            }
        }

        partial void OnSelectedGantryModuleChanged(string value)
        {
            LoadModule(value);
            RemapModuleAxes(value);
        }

        partial void OnSelectedAxisChanged(string value)
        {
            FollowAxes();

            // FAM 2 Bug_Id_043: what was typed for one axis is not left in the boxes for the next.
            TargetPosition = string.Empty;
            Speed = string.Empty;
        }

        /// <summary>
        /// The tab came on screen: manual mode on - the PLC takes the move, jog and enable
        /// commands from the HMI only in MANUAL - and the axes' live status followed.
        /// </summary>
        public void Activate()
        {
            if (_isActive)
                return;

            _isActive = true;
            _orchestrator.SetManualMode(true);
            FollowAxes();
        }

        /// <summary>The tab left the screen: manual mode let go, as the Manual tab does.</summary>
        public void Deactivate()
        {
            if (!_isActive)
                return;

            _isActive = false;
            _orchestrator.SetManualMode(false);
            foreach (IDisposable following in _following)
                following.Dispose();
            _following = new List<IDisposable>();
        }

        /// <summary>
        /// Follows the live status of the selected module's X, Y, Z and R axes - the
        /// positions on screen - and of the axis the move buttons drive, whose status is
        /// checked before a move. The new set is followed before the old one is let go, so
        /// an axis in both stays subscribed. Their values arrive through the monitor.
        /// </summary>
        private void FollowAxes()
        {
            if (!_isActive)
                return;

            List<IDisposable> previous = _following;
            _following = new[] { _mappedXAxisId, _mappedYAxisId, _mappedZAxisId, _mappedRAxisId, SelectedAxis }
                .Where(axisId => !string.IsNullOrWhiteSpace(axisId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(axisId => (GetAxis(axisId) as ILiveStatus)?.FollowStatus())
                .Where(following => following != null)
                .ToList();

            foreach (IDisposable following in previous)
                following.Dispose();
        }

        private void LoadModule(string module)
        {
            if (string.IsNullOrWhiteSpace(module))
            {
                Columns.Clear();
                Rows.Clear();
                SelectedRow = null;
                return;
            }

            var doc = _recipeService.LoadGantryRecipeDynamic(module);
            if (doc != null && doc.HasMissingColumnSchema && !string.IsNullOrWhiteSpace(doc.MissingSchemaMessage))
            {
                _dialogService.ShowWarning(doc.MissingSchemaMessage, "Recipe file");
            }

            bool columnsMatch = doc != null
                && Columns.Count == doc.Columns.Count
                && Columns.Zip(doc.Columns, (a, b) => string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase)).All(x => x);

            if (!columnsMatch)
            {
                Columns.Clear();
                if (doc?.Columns != null)
                {
                    foreach (var col in doc.Columns)
                    {
                        Columns.Add(col);
                    }
                }
            }

            Rows.Clear();
            if (doc?.Rows != null && doc.Columns != null)
            {
                foreach (var row in doc.Rows)
                {
                    Rows.Add(new DynamicGridRow(row, doc.Columns));
                }
            }

            SelectedRow = Rows.Count > 0 ? Rows[0] : null;
        }

        [RelayCommand]
        private void Edit() => IsEditing = true;

        [RelayCommand]
        private async Task Save()
        {
            // FAM 2 RC_09: an empty page has nothing to save, and Save must not leave the
            // page stuck in edit mode either.
            if (Rows.Count == 0)
            {
                _dialogService.ShowError("The recipe table is empty: there is nothing to save.", "Recipe");
                IsEditing = false;
                return;
            }

            if (Rows.Any(r => r.HasErrors))
            {
                _dialogService.ShowError("Please correct invalid values before saving.", "Recipe");
                return;
            }

            if (!_dialogService.Confirm("Do you want to save the data?", "Confirmation"))
            {
                return;
            }

            var doc = new DynamicTableDocument
            {
                Title = SelectedGantryModule,
                Columns = Columns.ToList(),
                Rows = Rows.Select(r => r.ToDictionary()).ToList(),
            };

            string module = SelectedGantryModule;
            RecipeSaveResult result;
            try
            {
                result = _recipeService.SaveGantryRecipeDynamic(module, doc);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError("The recipe was not saved: " + ex.Message, "Recipe");
                return;
            }

            // "No data modified !" ends the edit too — the operator pressed Save and
            // there was nothing to write. Leaving IsEditing set is what kept the grid
            // unlocked, and kept it unlocked across a trip to another tab.
            if (!result.IsSaved && !result.IsNoChanges)
            {
                _dialogService.ShowInfo(result.Message, "Recipe");
                return;
            }

            IsEditing = false;

            // The PLC drives the taught moves from its own copy, so the saved recipe is
            // written to it - also when nothing changed, which resends a recipe the PLC lost.
            try
            {
                await _plcRecipeService.WriteGantryRecipeAsync(module);
                _dialogService.ShowInfo(result.Message + "\nWritten to the PLC.", "Recipe");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError(result.Message + "\nNot written to the PLC: " + ex.Message, "Recipe");
            }
        }

        /// <summary>Leaving the tab abandons the edit: relock and reload the module, so
        /// the next visit shows the saved recipe rather than half-typed cells.</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
            {
                return;
            }

            IsEditing = false;
            LoadModule(SelectedGantryModule);
        }

        [RelayCommand]
        private async Task Move()
        {
            if (string.IsNullOrWhiteSpace(SelectedAxis))
            {
                _dialogService.ShowInfo("Axis is not selected.", "Recipe");
                return;
            }

            // Signed, and zero is allowed. Positions on this machine are signed — the
            // Pressing recipe row teaches X = -200, and Z sits below zero — and in
            // Relative mode the field is a distance, where the sign IS the direction.
            // Requiring a positive number here meant a taught negative position could
            // not be moved to at all, and a relative move could only ever run one way,
            // whatever the operator typed.
            // FAM 2 RC_07: an empty box asks for the position.
            if (string.IsNullOrWhiteSpace(TargetPosition))
            {
                _dialogService.ShowError("Please enter a position.", "Recipe");
                return;
            }

            if (!TryReadNumber(TargetPosition, "Please enter a valid position.", out double target))
            {
                return;
            }

            if (!TryReadSpeed(SelectedAxis, out double speed))
            {
                return;
            }

            bool isRelative = string.Equals(SelectedMotionMode, "Relative", StringComparison.OrdinalIgnoreCase);
            bool moved = isRelative
                ? await MoveAxisRelativeAsync(SelectedAxis, target, speed)
                : await MoveAxisAbsoluteAsync(SelectedAxis, target, speed);

            if (!moved)
            {
                _dialogService.ShowError("Move command failed for axis " + SelectedAxis + ".", "Recipe");
            }
        }

        [RelayCommand]
        private async Task Stop()
        {
            if (string.IsNullOrWhiteSpace(SelectedAxis))
            {
                return;
            }

            var axis = GetAxis(SelectedAxis);
            if (axis == null)
            {
                return;
            }

            await axis.StopAsync();
        }

        [RelayCommand]
        private async Task MoveSelectedRow(DynamicGridRow row)
        {
            DynamicGridRow targetRow = row ?? SelectedRow;
            if (targetRow == null)
            {
                _dialogService.ShowInfo("Select a row to move.", "Recipe");
                return;
            }

            // As V3: a position whose name contains "safe" moves its axes in
            // SafePositionPriority order, any other in MovementPriority order, one axis
            // after the other (each move waits for the one before to finish).
            GantryModuleDefinition definition = GetCurrentModuleDefinition();
            string posName = targetRow[GantryRecipeColumns.Position] ?? string.Empty;
            IReadOnlyList<string> order = posName.IndexOf("safe", StringComparison.OrdinalIgnoreCase) >= 0
                ? definition.SafePositionPriority
                : definition.MovementPriority;

            if (order.Count == 0)
            {
                _dialogService.ShowError("Module " + SelectedGantryModule + " has no axes in Gantry.json.", "Recipe");
                return;
            }

            foreach (string axisId in order)
            {
                if (GetAxis(axisId) == null)
                {
                    _dialogService.ShowError("Axis '" + axisId + "' is not available.", "Recipe");
                    return;
                }

                if (!TryParseCell(targetRow[GantryRecipeColumns.Value(axisId)], out double position))
                {
                    _dialogService.ShowError("'" + posName + "' has an invalid position for axis " + axisId + ".", "Recipe");
                    return;
                }

                // The position's own speed, as V3 moves it; a position taught without one
                // (0) moves at the speed in the motion card, as a single-axis move does.
                double speed;
                if (TryParseCell(targetRow[GantryRecipeColumns.Speed(axisId)], out double rowSpeed) && rowSpeed > 0d)
                {
                    double limit = GetAxisSpeedLimit(axisId);
                    if (limit > 0d && rowSpeed > limit)
                    {
                        _dialogService.ShowError("'" + posName + "' moves " + axisId + " at " + rowSpeed.ToString("0.##", CultureInfo.InvariantCulture)
                            + ", above the axis parameter speed (" + limit.ToString("0.##", CultureInfo.InvariantCulture) + ").", "Recipe");
                        return;
                    }

                    speed = rowSpeed;
                }
                else if (!TryReadSpeed(axisId, out speed))
                {
                    return;
                }

                if (!await MoveAxisAbsoluteAsync(axisId, position, speed))
                {
                    _dialogService.ShowError("Move command failed for axis " + axisId + ".", "Recipe");
                    return;
                }
            }
        }

        private static bool TryParseCell(string raw, out double value)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        [RelayCommand]
        private void TeachSelectedRow(DynamicGridRow row)
        {
            if (!IsEditing)
            {
                _dialogService.ShowInfo("Click Edit before teaching a position.", "Recipe");
                return;
            }

            DynamicGridRow targetRow = row ?? SelectedRow;
            if (targetRow == null)
            {
                _dialogService.ShowInfo("Select a row to teach.", "Recipe");
                return;
            }

            // As V3: every axis of the module takes its live position.
            GantryModuleDefinition definition = GetCurrentModuleDefinition();
            if (definition.Axes.Count == 0)
            {
                _dialogService.ShowInfo("Module " + SelectedGantryModule + " has no axes in Gantry.json.", "Recipe");
                return;
            }

            var missing = new List<string>();
            foreach (GantryAxisDefinition axis in definition.Axes)
            {
                AxisStatusSnapshot status = GetAxis(axis.Name)?.ReadStatus();
                if (status == null)
                {
                    missing.Add(axis.Name);
                    continue;
                }

                targetRow[GantryRecipeColumns.Value(axis.Name)] = FormatPosition(status.ActualPosition);
            }

            if (missing.Count > 0)
            {
                _dialogService.ShowError("No live position for " + string.Join(", ", missing) + "; those values were not taught.", "Recipe");
            }
        }

        [RelayCommand]
        private async Task JogXPositive() => await StartJogAsync(AxisRole.X, true);

        [RelayCommand]
        private async Task JogXNegative() => await StartJogAsync(AxisRole.X, false);

        [RelayCommand]
        private async Task JogYPositive() => await StartJogAsync(AxisRole.Y, true);

        [RelayCommand]
        private async Task JogYNegative() => await StartJogAsync(AxisRole.Y, false);

        [RelayCommand]
        private async Task JogZPositive() => await StartJogAsync(AxisRole.Z, true);

        [RelayCommand]
        private async Task JogZNegative() => await StartJogAsync(AxisRole.Z, false);

        [RelayCommand]
        private async Task JogRotateCw() => await StartJogAsync(AxisRole.R, true);

        [RelayCommand]
        private async Task JogRotateCcw() => await StartJogAsync(AxisRole.R, false);

        [RelayCommand]
        private async Task StopJog()
        {
            if (string.IsNullOrWhiteSpace(_activeJogAxisId))
            {
                return;
            }

            var axis = GetAxis(_activeJogAxisId);
            if (axis != null)
            {
                await axis.JogStopAsync();
            }

            _activeJogAxisId = string.Empty;
        }

        private async Task StartJogAsync(AxisRole role, bool forward)
        {
            string axisId = GetMappedAxisId(role);
            if (string.IsNullOrWhiteSpace(axisId))
            {
                _dialogService.ShowError("Axis for " + role + " jog is not configured.", "Recipe");
                return;
            }

            if (!TryReadSpeed(axisId, out double speed))
            {
                return;
            }

            var axis = GetAxis(axisId);
            if (axis == null)
            {
                _dialogService.ShowError("Axis " + axisId + " is not available.", "Recipe");
                return;
            }

            var status = axis.ReadStatus();
            if (!status.IsEnabled)
            {
                _dialogService.ShowError("Servo is disabled for axis " + axisId + ".", "Recipe");
                return;
            }

            await axis.JogStartAsync(forward, speed);
            _activeJogAxisId = axisId;
        }

        private async Task<bool> MoveAxisAbsoluteAsync(string axisId, double position, double speed)
        {
            var axis = GetAxis(axisId);
            if (axis == null)
            {
                return false;
            }

            var status = axis.ReadStatus();
            if (!status.IsEnabled)
            {
                _dialogService.ShowError("Servo is disabled for axis " + axisId + ".", "Recipe");
                return false;
            }

            return await axis.MoveAbsAsync(position, speed);
        }

        private async Task<bool> MoveAxisRelativeAsync(string axisId, double distance, double speed)
        {
            var axis = GetAxis(axisId);
            if (axis == null)
            {
                return false;
            }

            var status = axis.ReadStatus();
            if (!status.IsEnabled)
            {
                _dialogService.ShowError("Servo is disabled for axis " + axisId + ".", "Recipe");
                return false;
            }

            return await axis.MoveRelAsync(distance, speed);
        }

        private GantryModuleDefinition GetCurrentModuleDefinition()
        {
            if (_moduleDefinitions.TryGetValue(SelectedGantryModule ?? string.Empty, out GantryModuleDefinition definition))
            {
                return definition;
            }

            definition = _recipeService.GetGantryModuleDefinition(SelectedGantryModule);
            _moduleDefinitions[SelectedGantryModule] = definition;
            return definition;
        }

        private void RemapModuleAxes(string moduleName)
        {
            _mappedXAxisId = string.Empty;
            _mappedYAxisId = string.Empty;
            _mappedZAxisId = string.Empty;
            _mappedRAxisId = string.Empty;

            if (string.IsNullOrWhiteSpace(moduleName))
            {
                UpdateJogAvailability();
                return;
            }

            // Each jog button drives the module's servo of that type (Gantry.json), and only
            // a servo the machine has: a module without one leaves the button disabled.
            GantryModuleDefinition definition = GetCurrentModuleDefinition();
            foreach (GantryAxisDefinition axis in definition.Axes)
            {
                AxisRole role = RoleOf(axis.Type);
                if (role == AxisRole.Unknown || GetAxis(axis.Name) == null)
                {
                    continue;
                }

                string resolved = axis.Name;

                if (role == AxisRole.X && string.IsNullOrWhiteSpace(_mappedXAxisId))
                {
                    _mappedXAxisId = resolved;
                }
                else if (role == AxisRole.Y && string.IsNullOrWhiteSpace(_mappedYAxisId))
                {
                    _mappedYAxisId = resolved;
                }
                else if (role == AxisRole.Z && string.IsNullOrWhiteSpace(_mappedZAxisId))
                {
                    _mappedZAxisId = resolved;
                }
                else if (role == AxisRole.R && string.IsNullOrWhiteSpace(_mappedRAxisId))
                {
                    _mappedRAxisId = resolved;
                }
            }

            UpdateJogAvailability();
            FollowAxes();

            // The monitor reports an axis when its status changes, which for an axis that is
            // standing still may be never, so start the newly mapped axes from what they have now.
            foreach (string axisId in new[] { _mappedXAxisId, _mappedYAxisId, _mappedZAxisId, _mappedRAxisId })
            {
                OnAxisStatusUpdated(this, GetAxis(axisId)?.ReadStatus());
            }
        }

        private void UpdateJogAvailability()
        {
            CanJogX = !string.IsNullOrWhiteSpace(_mappedXAxisId);
            CanJogY = !string.IsNullOrWhiteSpace(_mappedYAxisId);
            CanJogZ = !string.IsNullOrWhiteSpace(_mappedZAxisId);
            CanJogR = !string.IsNullOrWhiteSpace(_mappedRAxisId);

            XAxisName = CanJogX ? _mappedXAxisId : "not mapped";
            YAxisName = CanJogY ? _mappedYAxisId : "not mapped";
            ZAxisName = CanJogZ ? _mappedZAxisId : "not mapped";
            RAxisName = CanJogR ? _mappedRAxisId : "not mapped";

            if (!CanJogX)
            {
                XPos = 0;
            }
            if (!CanJogY)
            {
                YPos = 0;
            }
            if (!CanJogZ)
            {
                ZPos = 0;
            }
            if (!CanJogR)
            {
                RPos = 0;
            }
        }

        private static string NormalizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var chars = value.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        private static Dictionary<string, IAxis> BuildAxisLookup(IReadOnlyList<IAxis> axes)
        {
            var result = new Dictionary<string, IAxis>(StringComparer.OrdinalIgnoreCase);
            if (axes == null)
            {
                return result;
            }

            foreach (var axis in axes)
            {
                if (axis == null || string.IsNullOrWhiteSpace(axis.ComponentId))
                {
                    continue;
                }

                if (!result.ContainsKey(axis.ComponentId))
                {
                    result.Add(axis.ComponentId, axis);
                }
            }

            return result;
        }

        private IAxis GetAxis(string axisId)
        {
            if (string.IsNullOrWhiteSpace(axisId))
            {
                return null;
            }

            return _axesById.TryGetValue(axisId, out IAxis axis) ? axis : null;
        }

        private bool TryReadSpeed(string axisId, out double speed)
        {
            speed = 0d;
            double limit = GetAxisSpeedLimit(axisId);
            string max = limit > 0d ? " (max " + limit.ToString("0.##", CultureInfo.InvariantCulture) + ")." : ".";

            // FAM 2 RC_06: an empty box asks for the speed.
            if (string.IsNullOrWhiteSpace(Speed))
            {
                _dialogService.ShowError("Please enter a speed" + max, "Recipe");
                return false;
            }

            if (!TryReadPositiveNumber(Speed, "Please enter a valid speed greater than 0" + max, out double requestedSpeed))
            {
                return false;
            }

            if (limit > 0d && requestedSpeed > limit)
            {
                _dialogService.ShowError("Please enter speed greater than 0 and less than axis parameter speed (" + limit.ToString("0.##", CultureInfo.InvariantCulture) + ").", "Recipe");
                return false;
            }

            speed = requestedSpeed;
            return true;
        }

        /// <summary>Speeds only: a speed of zero or below is never a valid command.</summary>
        private bool TryReadPositiveNumber(string rawValue, string errorMessage, out double parsed)
        {
            if (!TryReadNumber(rawValue, errorMessage, out parsed))
            {
                return false;
            }

            if (parsed <= 0d)
            {
                _dialogService.ShowError(errorMessage, "Recipe");
                return false;
            }

            return true;
        }

        /// <summary>Positions and distances: signed, and zero is a legitimate value.</summary>
        private bool TryReadNumber(string rawValue, string errorMessage, out double parsed)
        {
            bool success = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
            if (!success)
            {
                _dialogService.ShowError(errorMessage, "Recipe");
                return false;
            }

            return true;
        }

        private double GetAxisSpeedLimit(string axisId)
        {
            if (string.IsNullOrWhiteSpace(axisId))
            {
                return 0d;
            }

            if (_axisSpeedLimits.TryGetValue(axisId, out double speedLimit))
            {
                return speedLimit;
            }

            string normalizedTarget = NormalizeIdentifier(axisId);
            if (string.IsNullOrWhiteSpace(normalizedTarget))
            {
                return 0d;
            }

            foreach (var pair in _axisSpeedLimits)
            {
                if (NormalizeIdentifier(pair.Key).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }

            return 0d;
        }

        private string GetMappedAxisId(AxisRole role)
        {
            switch (role)
            {
                case AxisRole.X:
                    return _mappedXAxisId;
                case AxisRole.Y:
                    return _mappedYAxisId;
                case AxisRole.Z:
                    return _mappedZAxisId;
                case AxisRole.R:
                    return _mappedRAxisId;
                default:
                    return string.Empty;
            }
        }

        private void OnAxisStatusUpdated(object sender, AxisStatusSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ComponentId))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_mappedXAxisId)
                && snapshot.ComponentId.Equals(_mappedXAxisId, StringComparison.OrdinalIgnoreCase))
            {
                XPos = snapshot.ActualPosition;
            }

            if (!string.IsNullOrWhiteSpace(_mappedYAxisId)
                && snapshot.ComponentId.Equals(_mappedYAxisId, StringComparison.OrdinalIgnoreCase))
            {
                YPos = snapshot.ActualPosition;
            }

            if (!string.IsNullOrWhiteSpace(_mappedZAxisId)
                && snapshot.ComponentId.Equals(_mappedZAxisId, StringComparison.OrdinalIgnoreCase))
            {
                ZPos = snapshot.ActualPosition;
            }

            if (!string.IsNullOrWhiteSpace(_mappedRAxisId)
                && snapshot.ComponentId.Equals(_mappedRAxisId, StringComparison.OrdinalIgnoreCase))
            {
                RPos = snapshot.ActualPosition;
            }
        }

        private void OnLoadCellStatusUpdated(object sender, LoadCellStatusSnapshot snapshot)
            => ApplyLoadCellSnapshot(snapshot);

        private void RegisterLoadCellAliases(LoadCellItem item, params string[] aliases)
        {
            if (item == null || aliases == null)
            {
                return;
            }

            foreach (string alias in aliases)
            {
                string key = NormalizeIdentifier(alias);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!_loadCellsByAlias.ContainsKey(key))
                {
                    _loadCellsByAlias.Add(key, item);
                }
            }
        }

        private void ApplyLoadCellSnapshots(IReadOnlyList<LoadCellStatusSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                return;
            }

            foreach (LoadCellStatusSnapshot snapshot in snapshots)
            {
                ApplyLoadCellSnapshot(snapshot);
            }
        }

        private void ApplyLoadCellSnapshot(LoadCellStatusSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ComponentId))
            {
                return;
            }

            string key = NormalizeIdentifier(snapshot.ComponentId);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (_loadCellsByAlias.TryGetValue(key, out LoadCellItem item))
            {
                item.Value = snapshot.Value;
            }
        }

        private static string FormatPosition(double value)
            => value.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>The jog button an axis of Gantry.json drives, from its Type.</summary>
        private static AxisRole RoleOf(string type)
        {
            switch ((type ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "X": return AxisRole.X;
                case "Y": return AxisRole.Y;
                case "Z": return AxisRole.Z;
                case "R": return AxisRole.R;
                default: return AxisRole.Unknown;
            }
        }

        private enum AxisRole
        {
            Unknown = 0,
            X = 1,
            Y = 2,
            Z = 3,
            R = 4,
        }
    }
}
