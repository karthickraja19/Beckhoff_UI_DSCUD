using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.Wpf.App.Views.Dialogs;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// <see cref="IEarlyWarning"/> - S17 Early Warning For Identifying Common Defect Factors X3.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>After each unit passes the station: <c>GET_SMART_FA_ALTER_MSG</c> with the unit's
    /// serial number and this machine's terminal name.</item>
    /// <item>Result false: the GUI goes to the Alarm page and a pop-up shows MES's RetMsg. The
    /// machine does not stop. The pop-up stays on top of everything.</item>
    /// <item>While a pop-up is open the tower light's red is on and the buzzer sounds 2 s on /
    /// 1 s off (the PLC times it; symbol map Process.EarlyWarningLamp / EarlyWarningBuzzer).
    /// Mute silences the buzzer. Both go off when the last pop-up closes.</item>
    /// <item>AE / HIVE stations: the pop-up closes only after the CM's card was checked with
    /// MES (<c>GET_OP_ACCESS</c>, "UID,Close"); the switch that disables the pop-up needs
    /// "UID,Authorize". URP / ME stations: a Confirm button only.</item>
    /// <item>Disabled, only the pop-up and the buzzer are off - MES is still asked, and the
    /// answer is in the machine log. Enabled again at every start.</item>
    /// <item>No MES address: nothing is asked, and that is logged once.</item>
    /// </list>
    /// </remarks>
    public sealed class EarlyWarningService : IEarlyWarning
    {
        public const string Command = "GET_SMART_FA_ALTER_MSG";
        public const string AccessCommand = "GET_OP_ACCESS";

        private readonly IMesClient _mes;
        private readonly INavigationService _navigation;
        private readonly Action<bool> _lampToPlc;
        private readonly Action<bool> _buzzerToPlc;
        private readonly List<EarlyWarningWindow> _open = new List<EarlyWarningWindow>();
        private bool _popupEnabled = true;
        private bool _saidNotConfigured;

        /// <param name="needsCard">True for an AE / HIVE station (card to close), false for URP / ME (Confirm only).</param>
        /// <param name="lampToPlc">Holds the tower light's red on; null when the map has no signal.</param>
        /// <param name="buzzerToPlc">Holds the buzzer on; null when the map has no signal.</param>
        public EarlyWarningService(IMesClient mes, INavigationService navigation, bool needsCard,
            Action<bool> lampToPlc, Action<bool> buzzerToPlc)
        {
            _mes = mes;
            _navigation = navigation;
            NeedsCard = needsCard;
            _lampToPlc = lampToPlc;
            _buzzerToPlc = buzzerToPlc;
        }

        public event EventHandler Changed;

        public bool IsAvailable
        {
            get { return _mes != null && _mes.IsConfigured; }
        }

        public bool IsPopupEnabled
        {
            get { return _popupEnabled; }
        }

        public bool NeedsCard { get; }

        public string TerminalName
        {
            get { return _mes != null ? _mes.TerminalName : string.Empty; }
        }

        /// <summary>A unit passed the station. Returns at once; MES is asked behind.</summary>
        public void UnitPassed(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
                return;

            if (!IsAvailable)
            {
                if (!_saidNotConfigured)
                {
                    _saidNotConfigured = true;
                    Log(LogType.Info, "MES is not configured: no early-warning check is made after a unit.");
                }

                return;
            }

            Task.Run(() => CheckAsync(serialNumber.Trim()));
        }

        private async Task CheckAsync(string serialNumber)
        {
            try
            {
                MesResult result = await _mes.GetCmdResultAsync(Command, serialNumber).ConfigureAwait(false);
                if (!result.Reached)
                {
                    Log(LogType.Error, "The early-warning check of unit " + serialNumber + " got no answer from MES: " + result.Message);
                    return;
                }

                if (result.Ok)
                    return;

                string message = result.Message ?? string.Empty;
                Log(LogType.Error, "[Error/Alert] Early warning for unit " + serialNumber + ": " + message
                    + (_popupEnabled ? string.Empty : " (the pop-up is disabled)"));

                if (!_popupEnabled)
                    return;

                Application app = Application.Current;
                if (app != null)
                    await app.Dispatcher.BeginInvoke(new Action(() => Show(serialNumber, message)));
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "The early-warning check of unit " + serialNumber + " failed: " + ex.Message);
            }
        }

        /// <summary>On the UI thread.</summary>
        private void Show(string serialNumber, string message)
        {
            // "The machine software GUI automatically jumps to the Alarm page."
            try
            {
                if (_navigation != null)
                    _navigation.NavigateTo(AppPageNames.Alarm);
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "The Alarm page could not be opened for the early warning: " + ex.Message);
            }

            var window = new EarlyWarningWindow(serialNumber, message, NeedsCard, uid => CheckCardAsync(uid, "Close"));
            window.Muted += (sender, e) => Push(_buzzerToPlc, false);
            window.Closed += (sender, e) =>
            {
                _open.Remove(window);
                Log(LogType.Info, "Early warning for unit " + serialNumber + " closed"
                    + (string.IsNullOrEmpty(window.ClosedBy) ? "." : " with card " + window.ClosedBy + "."));

                if (_open.Count == 0)
                {
                    Push(_lampToPlc, false);
                    Push(_buzzerToPlc, false);
                }
            };

            _open.Add(window);
            Push(_lampToPlc, true);
            Push(_buzzerToPlc, true);
            window.Show();
        }

        public string SetPopupEnabled(bool enabled)
        {
            if (enabled == _popupEnabled)
                return null;

            string card = null;
            if (NeedsCard)
            {
                if (!IsAvailable)
                    return "MES is not configured on this machine, so the card cannot be checked.";

                var dialog = new MesCardDialog(
                    (enabled ? "Enable" : "Disable") + " the early warning for common defect factors",
                    "2-Step Verification: the CM DRI swipes their ID card.",
                    uid => CheckCardAsync(uid, "Authorize"))
                {
                    Owner = Application.Current != null ? Application.Current.MainWindow : null,
                };

                if (dialog.ShowDialog() != true)
                    return "Not changed: no card was authorized.";

                card = dialog.CardUid;
            }

            _popupEnabled = enabled;
            Log(LogType.Info, "Early-warning pop-up and buzzer " + (enabled ? "enabled" : "disabled")
                + (card != null ? " with card " + card : string.Empty) + ". MES is still asked after every unit.");
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }

        /// <summary>GET_OP_ACCESS with serial_Number "UID,Close" or "UID,Authorize". Null when MES grants it.</summary>
        private async Task<string> CheckCardAsync(string cardUid, string type)
        {
            if (!IsAvailable)
                return "MES is not configured on this machine, so the card cannot be checked.";

            MesResult result = await _mes.GetCmdResultAsync(AccessCommand, cardUid + "," + type);
            if (!result.Reached)
                return "MES could not check the card: " + result.Message;

            return result.Ok ? null : "MES refused the card: " + result.Message;
        }

        public async Task<EarlyWarningTest> TestAsync(string terminalName, string serialNumber)
        {
            string terminal = string.IsNullOrWhiteSpace(terminalName) ? TerminalName : terminalName.Trim();
            string serial = (serialNumber ?? string.Empty).Trim();

            var test = new EarlyWarningTest
            {
                Request = new JObject
                {
                    ["terminalName"] = terminal,
                    ["serial_Number"] = serial,
                    ["pCmd"] = Command,
                }.ToString(Formatting.Indented),
            };

            if (!IsAvailable)
            {
                test.Response = "MES is not configured on this machine (MesUrl in App.config).";
                return test;
            }

            if (serial.Length == 0)
            {
                test.Response = "Enter the serial number to test with.";
                return test;
            }

            MesResult result = await _mes.GetCmdResultAsync(Command, serial, terminalName: terminal);
            test.IsWarning = result.Reached && !result.Ok;
            test.Response = result.Reached
                ? new JObject { ["Result"] = result.Ok, ["RetMsg"] = result.Message ?? string.Empty }.ToString(Formatting.Indented)
                : result.Message;
            return test;
        }

        private static void Push(Action<bool> write, bool value)
        {
            if (write == null)
                return;

            try
            {
                write(value);
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "The early-warning light / buzzer could not be written to the PLC: " + ex.Message);
            }
        }

        private static void Log(LogType type, string message)
        {
            Logger.WriteLog(type, "MES", "EarlyWarning", message, string.Empty);
        }
    }
}
