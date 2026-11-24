using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;
using Nova.ViewModels.Pages.Audio;
using Nova.Wpf.App.Views.Dialogs;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// Watches the glue and nozzles while the machine runs and raises the warnings of
    /// S2.4.4.4 Health &amp; Early-warning. The Health tab only shows the state; this is
    /// what acts on it, whatever page is on screen.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Low glue: pop-up, alternating buzzer, flashing green light; production continues.</item>
    /// <item>Glue empty (or at the "change now" line): pop-up, continuous buzzer, flashing red light.</item>
    /// <item>Pot life under the warning threshold: pop-up. Pot life expired: pop-up, the machine
    /// halts and HIVE goes to downtime.</item>
    /// <item>The first of low glue or pot-life warning sends one staging request to MES
    /// (GLUE_WARNING_SAVE) per cartridge; a new cartridge clears it (GLUE_WARNING_REMOVE).</item>
    /// <item>Nozzle health (PD-12): a warning prompts every 4 hours, critical every 2 hours.</item>
    /// <item>Each pop-up can mute the buzzer; closing it needs a Level 2 or 3 badge.</item>
    /// </list>
    /// The buzzer and lights are the PLC's: the alert level (0 none, 1 warning, 2 critical)
    /// and the mute go to the PLC variables named in App.config, when they are named.
    /// </remarks>
    public sealed class GlueWarningMonitor : IDisposable
    {
        private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan NozzleWarningEvery = TimeSpan.FromHours(4);
        private static readonly TimeSpan NozzleCriticalEvery = TimeSpan.FromHours(2);

        private readonly IDispenserDataService _dispenser;
        private readonly IBadgePrompt _badges;
        private readonly IMesClient _mes;
        private readonly Func<string> _station;
        private readonly Func<string> _project;
        private readonly Action<string> _raiseDowntime;
        private readonly Action<int> _alertLevelToPlc;
        private readonly Action<bool> _muteToPlc;

        private readonly Dictionary<string, CartridgeState> _cartridges = new Dictionary<string, CartridgeState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _nozzlePrompted = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<Alert> _pending = new Queue<Alert>();

        private DispatcherTimer _timer;
        private bool _checking;
        private bool _showing;
        private int _openLevel;

        public GlueWarningMonitor(
            IDispenserDataService dispenser,
            IBadgePrompt badges,
            IMesClient mes,
            Func<string> station,
            Func<string> project,
            Action<string> raiseDowntime,
            Action<int> alertLevelToPlc,
            Action<bool> muteToPlc)
        {
            _dispenser = dispenser;
            _badges = badges;
            _mes = mes;
            _station = station;
            _project = project;
            _raiseDowntime = raiseDowntime;
            _alertLevelToPlc = alertLevelToPlc;
            _muteToPlc = muteToPlc;
        }

        /// <summary>Starts watching; does nothing when there is no glue data source.</summary>
        public void Start()
        {
            if (_dispenser == null || _timer != null)
                return;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += async (sender, e) =>
            {
                _timer.Interval = CheckEvery;
                await CheckAsync();
            };
            _timer.Start();
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task CheckAsync()
        {
            if (_checking)
                return;

            _checking = true;
            try
            {
                IReadOnlyList<GlueCartridgeStatus> cartridges = await _dispenser.GetGlueCartridgesAsync() ?? new GlueCartridgeStatus[0];
                GlueControlSettings settings = await _dispenser.GetGlueControlSettingsAsync() ?? new GlueControlSettings();
                IReadOnlyList<NozzleHealthRecord> nozzles = await _dispenser.GetNozzleHealthAsync() ?? new NozzleHealthRecord[0];

                foreach (GlueCartridgeStatus cartridge in cartridges)
                    await CheckCartridgeAsync(cartridge, settings);

                foreach (NozzleHealthRecord nozzle in nozzles)
                    CheckNozzle(nozzle, settings);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Glue", "Monitor", "The glue check failed: " + ex.Message, ex.ToString());
            }
            finally
            {
                _checking = false;
            }

            ShowNext();
        }

        private async Task CheckCartridgeAsync(GlueCartridgeStatus cartridge, GlueControlSettings settings)
        {
            string gantry = cartridge.Gantry ?? string.Empty;
            CartridgeState state;
            if (!_cartridges.TryGetValue(gantry, out state) || !string.Equals(state.GlueSn, cartridge.GlueSn, StringComparison.OrdinalIgnoreCase))
            {
                // A new cartridge starts again, and MES stops preparing a replacement for the old one.
                if (state != null && state.StagingRequested && !string.IsNullOrWhiteSpace(state.GlueSn))
                    await StagingAsync("GLUE_WARNING_REMOVE", gantry, state.GlueSn);

                state = new CartridgeState { GlueSn = cartridge.GlueSn };
                _cartridges[gantry] = state;
            }

            GantryGlueDosage dosage = (settings.Gantries ?? new List<GantryGlueDosage>())
                .FirstOrDefault(d => string.Equals(d.Gantry, gantry, StringComparison.OrdinalIgnoreCase));
            var row = new GlueCartridgeRow(cartridge, dosage, settings);
            string sn = string.IsNullOrWhiteSpace(cartridge.GlueSn) ? "(no glue SN)" : cartridge.GlueSn;

            if (row.HasDosage)
            {
                if (row.Level == HealthLevel.Critical && !state.EmptyShown)
                {
                    state.EmptyShown = state.LowShown = true;
                    Enqueue(new Alert(
                        gantry + " gantry: change the glue now",
                        "Remaining glue " + row.RemainingPercentText + " (" + row.RemainingText + " g). Change the cartridge; the machine keeps running.",
                        "Glue SN " + sn, true));
                    await RequestStagingOnceAsync(state, gantry);
                }
                else if (row.Level == HealthLevel.Warning && !state.LowShown)
                {
                    state.LowShown = true;
                    Enqueue(new Alert(
                        gantry + " gantry: low glue",
                        "Remaining glue " + row.RemainingPercentText + " - below the low-glue line of " + settings.LowGluePercent.ToString("0.##")
                            + "%. Material staging has been requested; production continues.",
                        "Glue SN " + sn + ", about " + row.EstimatedOutputText + " units left.", false));
                    await RequestStagingOnceAsync(state, gantry);
                }
            }

            double? potLife = cartridge.RemainingPotLifeHours;
            if (potLife.HasValue)
            {
                if (potLife.Value <= 0 && !state.PotLifeExpiredShown)
                {
                    state.PotLifeExpiredShown = state.PotLifeWarningShown = true;
                    string detail = "Glue pot life expired on the " + gantry + " gantry (glue SN " + sn + ").";
                    Enqueue(new Alert(gantry + " gantry: glue pot life expired",
                        "The glue's pot life has run out. The machine halts and HIVE goes to downtime; change the glue.", "Glue SN " + sn, true));
                    Logger.WriteLog(LogType.Error, "Glue", "Monitor", "[Error/Alert] " + detail, "");
                    if (_raiseDowntime != null)
                        _raiseDowntime(detail);
                    await RequestStagingOnceAsync(state, gantry);
                }
                else if (potLife.Value > 0 && potLife.Value < settings.PotLifeWarningHours && !state.PotLifeWarningShown)
                {
                    state.PotLifeWarningShown = true;
                    Enqueue(new Alert(gantry + " gantry: glue pot life ending",
                        "Remaining pot life " + potLife.Value.ToString("0.00") + " h, below the warning of "
                            + settings.PotLifeWarningHours.ToString("0.##") + " h. Material staging has been requested.",
                        "Glue SN " + sn, false));
                    await RequestStagingOnceAsync(state, gantry);
                }
            }
        }

        /// <summary>PD-12: a warning every 4 hours, critical every 2 hours, while the nozzle stays there.</summary>
        private void CheckNozzle(NozzleHealthRecord nozzle, GlueControlSettings settings)
        {
            bool critical = nozzle.Value <= settings.NozzleCriticalHours;
            bool warning = !critical && nozzle.Value < settings.NozzleWarningHours;
            if (!critical && !warning)
            {
                _nozzlePrompted.Remove(nozzle.NozzleId ?? string.Empty);
                return;
            }

            string key = nozzle.NozzleId ?? string.Empty;
            DateTime last;
            TimeSpan every = critical ? NozzleCriticalEvery : NozzleWarningEvery;
            if (_nozzlePrompted.TryGetValue(key, out last) && DateTime.Now - last < every)
                return;

            _nozzlePrompted[key] = DateTime.Now;
            Enqueue(new Alert(
                nozzle.Gantry + " gantry: nozzle health " + (critical ? "critical" : "low"),
                "Nozzle " + nozzle.NozzleId + " has " + nozzle.Value.ToString("0.#") + " h of health left. Plan a Nozzle Change (PD-12); the machine keeps running.",
                "Valve SN " + nozzle.ValveSn, critical));
        }

        private async Task RequestStagingOnceAsync(CartridgeState state, string gantry)
        {
            // S2.3.2: only one staging request per glue cartridge.
            if (state.StagingRequested || string.IsNullOrWhiteSpace(state.GlueSn))
                return;

            state.StagingRequested = true;
            await StagingAsync("GLUE_WARNING_SAVE", gantry, state.GlueSn);
        }

        /// <summary>serial_Number GLUE=sn$Machine=STATION_FRONT, empNo AUTO + project (S2.4.4.4).</summary>
        private async Task StagingAsync(string command, string gantry, string glueSn)
        {
            if (_mes == null || !_mes.IsConfigured)
            {
                Logger.WriteLog(LogType.Info, "Glue", "Monitor", "MES is not configured - " + command + " for glue " + glueSn + " not sent.", "");
                return;
            }

            string machine = (Safe(_station) + "_" + gantry).ToUpperInvariant();
            MesResult result = await _mes.GetCmdResultAsync(command, "GLUE=" + glueSn + "$Machine=" + machine, "", "", "AUTO" + Safe(_project));
            if (!result.Reached || !result.Ok)
                Logger.WriteLog(LogType.Error, "Glue", "Monitor", command + " for glue " + glueSn + " failed: " + result.Message, "");
        }

        private void Enqueue(Alert alert)
        {
            Logger.WriteLog(LogType.Info, "Glue", "Monitor", "[Error/Alert] " + alert.Title + ": " + alert.Message, "");
            _pending.Enqueue(alert);
        }

        /// <summary>One pop-up at a time; the PLC gets the highest level on screen.</summary>
        private void ShowNext()
        {
            if (_showing || _pending.Count == 0 || Application.Current == null)
                return;

            _showing = true;
            try
            {
                while (_pending.Count > 0)
                {
                    Alert alert = _pending.Dequeue();
                    _openLevel = alert.Critical ? 2 : 1;
                    Push(_alertLevelToPlc, _openLevel);
                    Push(_muteToPlc, false);

                    var window = new GlueAlertWindow(alert.Title, alert.Message, alert.Detail, alert.Critical, _badges)
                    {
                        Owner = Application.Current.MainWindow,
                    };
                    window.Muted += (sender, e) => Push(_muteToPlc, true);
                    window.ShowDialog();

                    Logger.WriteLog(LogType.Info, "Glue", "Monitor", "Alert \"" + alert.Title + "\" closed by " + window.ClosedBy + ".", "");
                }
            }
            finally
            {
                _openLevel = 0;
                Push(_alertLevelToPlc, 0);
                Push(_muteToPlc, false);
                _showing = false;
            }
        }

        private static void Push<T>(Action<T> write, T value)
        {
            if (write == null)
                return;

            try
            {
                write(value);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Glue", "Monitor", "Could not pass the glue alert to the PLC: " + ex.Message, "");
            }
        }

        private static string Safe(Func<string> value)
        {
            try
            {
                return value != null ? value() ?? string.Empty : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private sealed class CartridgeState
        {
            public string GlueSn;
            public bool LowShown;
            public bool EmptyShown;
            public bool PotLifeWarningShown;
            public bool PotLifeExpiredShown;
            public bool StagingRequested;
        }

        private sealed class Alert
        {
            public Alert(string title, string message, string detail, bool critical)
            {
                Title = title;
                Message = message;
                Detail = detail;
                Critical = critical;
            }

            public string Title { get; }
            public string Message { get; }
            public string Detail { get; }
            public bool Critical { get; }
        }
    }
}
