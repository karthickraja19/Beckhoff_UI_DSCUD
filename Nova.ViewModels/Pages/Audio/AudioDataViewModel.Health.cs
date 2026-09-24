using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data.Models;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Health tab - S2.4.4.4 Health &amp; Early-warning: glue left in each
    /// cartridge, pot life, nozzle health, and the dosages and thresholds behind them.
    /// </summary>
    /// <remarks>
    /// This tab shows the state and its thresholds. The warnings themselves - the
    /// low-glue and pot-life pop-ups, the buzzer and tower light, the staging request
    /// to MES - belong to the service that watches these values while the machine
    /// runs, not to a page that is only open when someone looks at it.
    /// </remarks>
    public partial class AudioDataViewModel
    {
        private IReadOnlyList<GlueCartridgeStatus> _cartridges = Empty<GlueCartridgeStatus>();
        private IReadOnlyList<NozzleHealthRecord> _nozzles = Empty<NozzleHealthRecord>();
        private GlueControlSettings _glueControl = new GlueControlSettings();

        public ObservableCollection<GlueCartridgeRow> GlueCartridges { get; } = new ObservableCollection<GlueCartridgeRow>();

        public ObservableCollection<PotLifeRow> PotLife { get; } = new ObservableCollection<PotLifeRow>();

        public ObservableCollection<NozzleHealthRow> Nozzles { get; } = new ObservableCollection<NozzleHealthRow>();

        public bool HasCartridges => GlueCartridges.Count > 0;

        public bool HasNozzles => Nozzles.Count > 0;


        // ==================================================================
        // Threshold explanations (S2.4.4.4 tables)
        // ==================================================================

        public string GlueThresholdText =>
            "Normal above " + _glueControl.LowGluePercent.ToString("0.#") + "%. "
            + "At or below " + _glueControl.LowGluePercent.ToString("0.#") + "%: a staging request goes to MES and the "
            + "green light flashes. At or below " + _glueControl.EmptyGluePercent.ToString("0.#") + "%: change the glue "
            + "- buzzer and pop-up. Production does not stop.";

        public string PotLifeThresholdText =>
            "Below " + _glueControl.PotLifeWarningHours.ToString("0.0#") + " h: pop-up and siren, production continues. "
            + "At 0 h the machine stops and HIVE goes to downtime. MES is asked every minute.";

        public string NozzleThresholdText =>
            "Below " + _glueControl.NozzleWarningHours.ToString("0.#") + " h: prompt every 4 hours. Below "
            + _glueControl.NozzleCriticalHours.ToString("0.#") + " h: prompt every 2 hours (PD-12).";


        // ==================================================================
        // Glue control settings: dosages per gantry and the thresholds
        // ==================================================================

        public ObservableCollection<GantryDosageRow> DosageRows { get; } = new ObservableCollection<GantryDosageRow>();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditGlueControlCommand))]
        private bool _hasGlueControl;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(EditGlueControlCommand))]
        private bool _isEditingGlueControl;

        [ObservableProperty]
        private string _lowGluePercent = string.Empty;

        [ObservableProperty]
        private string _emptyGluePercent = string.Empty;

        [ObservableProperty]
        private string _potLifeWarningHours = string.Empty;

        [ObservableProperty]
        private string _nozzleWarningHours = string.Empty;

        [ObservableProperty]
        private string _nozzleCriticalHours = string.Empty;

        [ObservableProperty]
        private string _glueControlError;

        /// <summary>Level 2 and up, once the settings have been read.</summary>
        public IRelayCommand EditGlueControlCommand { get; private set; }

        private bool CanEditGlueControl()
        {
            return CanEditSettings && HasGlueControl && !IsEditingGlueControl;
        }

        private void EditGlueControl()
        {
            GlueControlError = null;
            IsEditingGlueControl = true;
        }

        [RelayCommand]
        private void CancelGlueControl()
        {
            GlueControlError = null;
            IsEditingGlueControl = false;
            LoadGlueControlFields();
        }

        [RelayCommand]
        private async Task SaveGlueControlAsync()
        {
            if (_dispenser == null || !CanEditSettings)
                return;

            var dosages = new List<GantryGlueDosage>();
            bool rowsValid = true;
            foreach (GantryDosageRow row in DosageRows)
            {
                GantryGlueDosage dosage;
                if (row.TryBuild(out dosage))
                    dosages.Add(dosage);
                else
                    rowsValid = false;
            }

            double low, empty, potLife, nozzleWarning, nozzleCritical;

            if (!rowsValid)
                GlueControlError = "Correct the dosages marked in red.";
            else if (!TryParsePercent(LowGluePercent, out low))
                GlueControlError = "Low-glue threshold must be a percentage from 0 to 100.";
            else if (!TryParsePercent(EmptyGluePercent, out empty))
                GlueControlError = "Change-glue threshold must be a percentage from 0 to 100.";
            else if (empty >= low)
                GlueControlError = "The change-glue threshold must be below the low-glue threshold.";
            else if (!GantryDosageRow.TryParsePositive(PotLifeWarningHours, out potLife))
                GlueControlError = "Pot-life warning must be a number of hours above 0.";
            else if (!GantryDosageRow.TryParsePositive(NozzleWarningHours, out nozzleWarning))
                GlueControlError = "Nozzle warning must be a number of hours above 0.";
            else if (!GantryDosageRow.TryParsePositive(NozzleCriticalHours, out nozzleCritical))
                GlueControlError = "Nozzle critical level must be a number of hours above 0.";
            else if (nozzleCritical >= nozzleWarning)
                GlueControlError = "The nozzle critical level must be below the warning level.";
            else
            {
                var settings = new GlueControlSettings
                {
                    Gantries = dosages,
                    LowGluePercent = low,
                    EmptyGluePercent = empty,
                    PotLifeWarningHours = potLife,
                    NozzleWarningHours = nozzleWarning,
                    NozzleCriticalHours = nozzleCritical
                };

                try
                {
                    await _dispenser.SaveGlueControlSettingsAsync(settings);
                    _glueControl = settings;
                    GlueControlError = null;
                    IsEditingGlueControl = false;
                    BuildHealth();
                    LogAction("Glue control settings changed: "
                              + string.Join("; ", dosages.Select(d => d.Gantry + " cartridge " + d.CartridgeWeightG
                                                                      + " g, " + d.GluePerProductG + " g/product, long purge "
                                                                      + d.LongPurgeDoseG + " g, short purge " + d.ShortPurgeDoseG + " g"))
                              + "; low " + low + "%, change " + empty + "%, pot life " + potLife + " h, nozzle "
                              + nozzleWarning + "/" + nozzleCritical + " h");
                }
                catch (Exception ex)
                {
                    GlueControlError = "Could not save: " + ex.Message;
                }
            }
        }

        private static bool TryParsePercent(string text, out double value)
        {
            return GantryDosageRow.TryParseNonNegative(text, out value) && value <= 100;
        }

        /// <summary>
        /// One dosage row per gantry in the settings, plus any gantry that has a
        /// cartridge but no dosage yet, so OSS can fill it in.
        /// </summary>
        private void LoadGlueControlFields()
        {
            DosageRows.Clear();

            var gantries = _glueControl.Gantries
                .Select(g => g.Gantry)
                .Concat(_cartridges.Select(c => c.Gantry))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => string.Equals(g, FrontGantry, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();

            foreach (string gantry in gantries)
            {
                GantryGlueDosage dosage = DosageFor(gantry) ?? new GantryGlueDosage { Gantry = gantry };
                DosageRows.Add(new GantryDosageRow(dosage));
            }

            LowGluePercent = GantryDosageRow.Format(_glueControl.LowGluePercent);
            EmptyGluePercent = GantryDosageRow.Format(_glueControl.EmptyGluePercent);
            PotLifeWarningHours = GantryDosageRow.Format(_glueControl.PotLifeWarningHours);
            NozzleWarningHours = GantryDosageRow.Format(_glueControl.NozzleWarningHours);
            NozzleCriticalHours = GantryDosageRow.Format(_glueControl.NozzleCriticalHours);
        }

        private GantryGlueDosage DosageFor(string gantry)
        {
            return _glueControl.Gantries.FirstOrDefault(g => string.Equals(g.Gantry, gantry, StringComparison.OrdinalIgnoreCase));
        }


        // ==================================================================
        // Loading and building
        // ==================================================================

        private async Task LoadHealthAsync()
        {
            if (_dispenser == null)
            {
                _cartridges = Empty<GlueCartridgeStatus>();
                _nozzles = Empty<NozzleHealthRecord>();
                HasGlueControl = false;
                HealthStatus = "Glue and nozzle health are not connected to this HMI yet. They need the machine's "
                               + "glue counters and the MES pot-life and nozzle queries.";
                BuildHealth();
                return;
            }

            try
            {
                _cartridges = await _dispenser.GetGlueCartridgesAsync() ?? Empty<GlueCartridgeStatus>();
                _nozzles = await _dispenser.GetNozzleHealthAsync() ?? Empty<NozzleHealthRecord>();
                _glueControl = await _dispenser.GetGlueControlSettingsAsync() ?? new GlueControlSettings();
                HasGlueControl = true;
            }
            catch (Exception ex)
            {
                _cartridges = Empty<GlueCartridgeStatus>();
                _nozzles = Empty<NozzleHealthRecord>();
                _healthReadAt = null;   // so the rebuild below keeps the failure on screen
                HealthStatus = "Query failed: " + ex.Message;
                BuildHealth();
                return;
            }

            _healthReadAt = DateTime.Now;
            BuildHealth();
            ExportCommand.NotifyCanExecuteChanged();
        }

        private DateTime? _healthReadAt;

        /// <summary>
        /// The status line: when the values were read and how many are past a threshold.
        /// Re-counted on every rebuild, so saving new thresholds updates it too.
        /// </summary>
        private void UpdateHealthStatus()
        {
            if (_dispenser == null || !_healthReadAt.HasValue)
                return;

            int warnings = GlueCartridges.Count(r => r.Level >= HealthLevel.Warning)
                           + PotLife.Count(r => r.Level >= HealthLevel.Warning)
                           + Nozzles.Count(r => r.Level >= HealthLevel.Warning);

            HealthStatus = "Updated " + _healthReadAt.Value.ToString("HH:mm:ss") + ". "
                           + (warnings == 0 ? "Everything is within its thresholds." : warnings + " item(s) need attention.");
        }

        private void BuildHealth()
        {
            GlueCartridges.Clear();
            PotLife.Clear();

            foreach (GlueCartridgeStatus cartridge in _cartridges
                         .OrderBy(c => string.Equals(c.Gantry, FrontGantry, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                GlueCartridges.Add(new GlueCartridgeRow(cartridge, DosageFor(cartridge.Gantry), _glueControl));
                PotLife.Add(new PotLifeRow(cartridge, _glueControl.PotLifeWarningHours));
            }

            Nozzles.Clear();
            foreach (NozzleHealthRecord nozzle in _nozzles
                         .OrderBy(n => string.Equals(n.Gantry, FrontGantry, StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                Nozzles.Add(new NozzleHealthRow(nozzle, _glueControl.NozzleWarningHours, _glueControl.NozzleCriticalHours));
            }

            if (!IsEditingGlueControl)
                LoadGlueControlFields();

            UpdateHealthStatus();

            OnPropertyChanged(nameof(HasCartridges));
            OnPropertyChanged(nameof(HasNozzles));
            OnPropertyChanged(nameof(GlueThresholdText));
            OnPropertyChanged(nameof(PotLifeThresholdText));
            OnPropertyChanged(nameof(NozzleThresholdText));
        }


        // ==================================================================
        // Export
        // ==================================================================

        private static IReadOnlyList<string> HealthExportHeaders()
        {
            return new[]
            {
                "Section", "Gantry", "Glue SN", "Valve SN", "Nozzle ID", "Cartridge (g)", "Glue/product (g)",
                "Produced", "Long purge (g)", "Short purge (g)", "Weighing (g)", "Remaining (g)", "Remaining %",
                "Est. output", "Pot life (h)", "Pot life threshold (h)", "Nozzle value (h)", "LSL", "USL", "Status"
            };
        }

        private IReadOnlyList<IReadOnlyList<string>> HealthExportRows()
        {
            var rows = new List<IReadOnlyList<string>>();

            foreach (GlueCartridgeRow r in GlueCartridges)
            {
                rows.Add(new[]
                {
                    "Remaining glue", r.Gantry, r.GlueSn, "", "", r.CartridgeText, r.GluePerProductText,
                    r.ProducedText, r.LongPurgeText, r.ShortPurgeText, r.WeighingText, r.RemainingText,
                    r.RemainingPercentText, r.EstimatedOutputText, "", "", "", "", "", r.StatusText
                });
            }

            foreach (PotLifeRow r in PotLife)
            {
                rows.Add(new[]
                {
                    "Pot life", r.Gantry, r.GlueSn, "", "", "", "", "", "", "", "", "", "", "",
                    r.RemainingText, r.ThresholdText, "", "", "", r.StatusText
                });
            }

            foreach (NozzleHealthRow r in Nozzles)
            {
                rows.Add(new[]
                {
                    "Nozzle health", r.Gantry, "", r.ValveSn, r.NozzleId, "", "", "", "", "", "", "", "", "",
                    "", "", r.ValueText, r.LowerLimitText, r.UpperLimitText, r.StatusText
                });
            }

            return rows;
        }
    }
}
