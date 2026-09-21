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
using Nova.Abstractions.Hardware.Enums;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Recipe
{
    /// <summary>
    /// "Gantry" tab of the Recipe screen (V3 RecipieForm gantry side).
    /// A gantry module is selected (GantryStructure.xml), its taught positions load
    /// into the table (*Receipe_Param.csv) and can be edited/saved. Below the table:
    /// single-axis servo motion, a module jog pad with live positions, and loadcells.
    /// Rows are seeded from the reference data; in production they load from configuration.
    /// </summary>
    public partial class GantryRecipeViewModel : ObservableObject, Nova.ViewModels.Common.IEditableTab
    {
        private readonly IRecipeService _recipeService;
        private readonly IDialogService _dialogService;
        private readonly IAxisService _axisService;
        private readonly IAxisMonitorService _axisMonitor;
        private readonly ILoadCellMonitorService _loadCellMonitor;
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
            ILoadCellMonitorService loadCellMonitor)
        {
            _recipeService = recipeService ?? throw new System.ArgumentNullException(nameof(recipeService));
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
            if (Axes.Count == 0)
            {
                foreach (var name in Enum.GetNames(typeof(Servos)))
                {
                    Axes.Add(name);
                }
            }
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
                _dialogService.ShowWarning(doc.MissingSchemaMessage, "Column Schema Warning");
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
        private void Save()
        {
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

            var result = _recipeService.SaveGantryRecipeDynamic(SelectedGantryModule, doc);
            _dialogService.ShowInfo(result.Message, "Recipe");

            // "No data modified !" ends the edit too — the operator pressed Save and
            // there was nothing to write. Leaving IsEditing set is what kept the grid
            // unlocked, and kept it unlocked across a trip to another tab.
            if (result.IsSaved || result.IsNoChanges)
            {
                IsEditing = false;
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

            GantryModuleDefinition definition = GetCurrentModuleDefinition();
            IReadOnlyList<string> priority = definition.MovementPriority;
            string posName = targetRow["Position"];
            if (posName != null
                && posName.IndexOf("safe", StringComparison.OrdinalIgnoreCase) >= 0
                && definition.SafePositionPriority.Count > 0)
            {
                priority = definition.SafePositionPriority;
            }

            if (priority.Count == 0)
            {
                _dialogService.ShowError("No movement priority is defined for module " + SelectedGantryModule + ".", "Recipe");
                return;
            }

            foreach (string axisToken in priority)
            {
                AxisRole role = GetAxisRole(axisToken);
                if (role == AxisRole.Unknown)
                {
                    continue;
                }

                string axisId = ResolveAxisId(axisToken, role);
                if (string.IsNullOrWhiteSpace(axisId))
                {
                    _dialogService.ShowError("Axis '" + axisToken + "' is not available.", "Recipe");
                    return;
                }

                if (!TryReadRowPosition(targetRow, role, out double position))
                {
                    _dialogService.ShowError("Selected row has an invalid " + role + " position.", "Recipe");
                    return;
                }

                if (!TryReadSpeed(axisId, out double speed))
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

            targetRow["X"] = FormatPosition(XPos);
            targetRow["Y"] = FormatPosition(YPos);
            targetRow["Z"] = FormatPosition(ZPos);
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

            GantryModuleDefinition definition = GetCurrentModuleDefinition();
            foreach (string axisToken in definition.MovementPriority)
            {
                AxisRole role = GetAxisRole(axisToken);
                if (role == AxisRole.Unknown)
                {
                    continue;
                }

                string resolved = ResolveAxisId(axisToken, role);
                if (string.IsNullOrWhiteSpace(resolved))
                {
                    continue;
                }

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

        /// <summary>
        /// Finds the axis of <paramref name="role"/> that belongs to
        /// <paramref name="moduleName"/>, and nothing else.
        ///
        /// GantryStructure.xml names its axes generically (Servo_X1, Servo_Z1) while the
        /// machine's real axes are module-scoped (PressingModule_XAxis, BTH_XAxis), so
        /// the direct name match always misses and every mapping lands here. This used
        /// to end with "return candidates[0]" — if no axis of that role carried the
        /// module's name, it silently handed back the first axis of that role on the
        /// whole machine. Selecting BTHGantry and pressing jog then drove
        /// PressingModule_XAxis, which is exactly the wrong-axis movement QA saw.
        ///
        /// Matching is on the axis's own module prefix (everything before the role
        /// token) against the module name, in either direction, so BTH_XAxis pairs with
        /// BTHGantry and PressingModule_XAxis with PressingModule, while
        /// InspectionModule_XAxis matches neither. The longest matching prefix wins, so
        /// a more specific axis beats a shorter one. A module whose axis still cannot be
        /// identified maps to nothing and its jog buttons stay disabled, rather than the
        /// screen guessing at which hardware to move.
        /// </summary>
        private string ResolveAxisFallback(AxisRole role, string moduleName)
        {
            string moduleToken = NormalizeIdentifier(moduleName);
            if (string.IsNullOrWhiteSpace(moduleToken))
            {
                return string.Empty;
            }

            string best = string.Empty;
            int bestPrefixLength = -1;

            foreach (string axisName in _axesById.Keys)
            {
                if (GetAxisRole(axisName) != role)
                {
                    continue;
                }

                string prefix = GetModulePrefix(axisName);
                // An axis with no module prefix at all (a bare "XAxis") belongs to no
                // module in particular; claiming it for whichever module is selected is
                // the guess this method exists to avoid.
                if (prefix.Length == 0)
                {
                    continue;
                }

                bool matches = moduleToken.StartsWith(prefix, StringComparison.Ordinal)
                    || prefix.StartsWith(moduleToken, StringComparison.Ordinal);

                if (matches && prefix.Length > bestPrefixLength)
                {
                    best = axisName;
                    bestPrefixLength = prefix.Length;
                }
            }

            return best;
        }

        /// <summary>
        /// The module part of an axis name — everything except the role token.
        /// "PressingModule_XAxis" -> "pressingmodule", "BTH_XAxis" -> "bth".
        /// </summary>
        private static string GetModulePrefix(string axisName)
        {
            if (string.IsNullOrWhiteSpace(axisName))
            {
                return string.Empty;
            }

            string[] tokens = axisName
                .ToLowerInvariant()
                .Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);

            var kept = tokens.Where(t => !IsRoleToken(t)).ToArray();
            return NormalizeIdentifier(string.Concat(kept));
        }

        /// <summary>The tokens <see cref="GetAxisRole"/> recognises as naming an axis role.</summary>
        private static bool IsRoleToken(string token)
        {
            switch (token)
            {
                case "xaxis":
                case "x":
                case "x1":
                case "yaxis":
                case "y":
                case "y1":
                case "zaxis":
                case "z":
                case "z1":
                case "raxis":
                case "r":
                case "r1":
                case "rotation":
                    return true;
                default:
                    return false;
            }
        }

        private string ResolveAxisId(string axisToken, AxisRole role)
        {
            if (string.IsNullOrWhiteSpace(axisToken))
            {
                return string.Empty;
            }

            string directMatch = _axesById.Keys
                .FirstOrDefault(name => name.Equals(axisToken, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(directMatch))
            {
                return directMatch;
            }

            string normalizedToken = NormalizeIdentifier(axisToken);
            if (!string.IsNullOrWhiteSpace(normalizedToken))
            {
                string normalizedMatch = _axesById.Keys
                    .FirstOrDefault(name => NormalizeIdentifier(name).Equals(normalizedToken, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(normalizedMatch))
                {
                    return normalizedMatch;
                }
            }

            return ResolveAxisFallback(role, SelectedGantryModule ?? string.Empty);
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
            if (!TryReadPositiveNumber(Speed, "Please enter a valid speed.", out double requestedSpeed))
            {
                return false;
            }

            double limit = GetAxisSpeedLimit(axisId);
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

        private static bool TryReadRowPosition(DynamicGridRow row, AxisRole role, out double position)
        {
            position = 0d;
            string raw = string.Empty;
            switch (role)
            {
                case AxisRole.X:
                    raw = row["X"];
                    break;
                case AxisRole.Y:
                    raw = row["Y"];
                    break;
                case AxisRole.Z:
                    raw = row["Z"];
                    break;
                case AxisRole.R:
                    raw = "0";
                    break;
                default:
                    return false;
            }

            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out position)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out position);
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

        private static AxisRole GetAxisRole(string axisName)
        {
            if (string.IsNullOrWhiteSpace(axisName))
            {
                return AxisRole.Unknown;
            }

            string[] tokens = axisName
                .ToLowerInvariant()
                .Split(new[] { '_', '-', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Any(t => t == "raxis" || t == "r" || t == "r1" || t == "rotation"))
            {
                return AxisRole.R;
            }

            if (tokens.Any(t => t == "xaxis" || t == "x" || t == "x1"))
            {
                return AxisRole.X;
            }

            if (tokens.Any(t => t == "yaxis" || t == "y" || t == "y1"))
            {
                return AxisRole.Y;
            }

            if (tokens.Any(t => t == "zaxis" || t == "z" || t == "z1"))
            {
                return AxisRole.Z;
            }

            if (axisName.IndexOf("XAxis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AxisRole.X;
            }

            if (axisName.IndexOf("YAxis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AxisRole.Y;
            }

            if (axisName.IndexOf("ZAxis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AxisRole.Z;
            }

            if (axisName.IndexOf("RAxis", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AxisRole.R;
            }

            return AxisRole.Unknown;
        }

        private enum AxisRole
        {
            Unknown = 0,
            X = 1,
            Y = 2,
            Z = 3,
            R = 4,
        }

        private static GantryPositionRow MapToRow(GantryPositionRecord record)
        {
            var row = new GantryPositionRow(record.Position, record.X, record.Y, record.Z)
            {
                XVel = record.XVel,
                XAcc = record.XAcc,
                XDeAcc = record.XDeAcc,
                YVel = record.YVel,
                YAcc = record.YAcc,
                YDeAcc = record.YDeAcc,
                ZVel = record.ZVel,
                ZAcc = record.ZAcc,
                ZDeAcc = record.ZDeAcc,
            };

            return row;
        }

        private static GantryPositionRecord MapToRecord(GantryPositionRow row)
        {
            return new GantryPositionRecord
            {
                Position = row.Position,
                X = row.X,
                XVel = row.XVel,
                XAcc = row.XAcc,
                XDeAcc = row.XDeAcc,
                Y = row.Y,
                YVel = row.YVel,
                YAcc = row.YAcc,
                YDeAcc = row.YDeAcc,
                Z = row.Z,
                ZVel = row.ZVel,
                ZAcc = row.ZAcc,
                ZDeAcc = row.ZDeAcc,
            };
        }
    }
}
