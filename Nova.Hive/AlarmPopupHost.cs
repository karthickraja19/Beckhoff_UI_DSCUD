using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>
    /// Opens the alarm pop-up when an alarm arrives (Audio SW DFM v5.4, slide 50): the
    /// pop-up appears, the GUI switches to the Alarm page behind it, and the pop-up stays
    /// there until the operator swipes a badge. The badge and the confirmed wording then
    /// go to HIVE.
    /// </summary>
    /// <remarks>
    /// Pop-ups are only raised while HIVE is enabled (<see cref="HiveSettings.Enabled"/>:
    /// App.config HiveEnabled, or the HIVE Control tab), and only for the alarms HIVE takes
    /// in (<see cref="HiveService.AlarmReported"/>) - none while the PLC is in manual; one
    /// still active when it leaves manual pops up then. Every alarm gets its own pop-up:
    /// alarms that arrive while one is open are queued and shown one after another. The
    /// pop-up has no Close button unless App.config AlarmPopupClosable is true; otherwise it
    /// only goes away with Confirm and a badge.
    /// </remarks>
    public sealed class AlarmPopupHost : IDisposable
    {
        private readonly HiveService _hive;
        private readonly INavigationService _navigation;
        private readonly IAccessControlService _access;
        private readonly AccessLevel _closeLevel;
        private readonly Dispatcher _uiThread;
        private readonly bool _closable;

        private readonly Queue<AlarmRecord> _pending = new Queue<AlarmRecord>();

        private AlarmPopup _popup;
        private bool _showing;

        /// <param name="access">Checks the badge that closes the pop-up when <paramref name="closeLevel"/> is Level 2 or 3.</param>
        /// <param name="closeLevel">
        /// Who may close the pop-up: Level 1 is any operator (slide 50); Level 2 is the Alarm
        /// page spec's "Level 2 or Level 3 user" (App.config AlarmPopupCloseLevel).
        /// </param>
        public AlarmPopupHost(HiveService hive, INavigationService navigation = null,
            IAccessControlService access = null, AccessLevel closeLevel = AccessLevel.Level1,
            bool closable = false)
        {
            _closable = closable;
            _hive = hive;
            _navigation = navigation;
            _access = access;
            _closeLevel = closeLevel;
            _uiThread = Application.Current.Dispatcher;
        }

        public void Start()
        {
            _hive.AlarmReported += OnAlarmStarted;
        }

        public void Stop()
        {
            _hive.AlarmReported -= OnAlarmStarted;
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnAlarmStarted(object sender, AlarmRecord alarm)
        {
            // No pop-up unless HIVE is on; the alarm is still on the Alarm page and in [Alarms].
            if (!_hive.Settings.Enabled)
                return;

            // Alarms arrive on the monitor thread; windows belong to the UI thread.
            _uiThread.BeginInvoke(new Action(() => Enqueue(alarm)));
        }

        private void Enqueue(AlarmRecord alarm)
        {
            _pending.Enqueue(alarm);

            // A pop-up is already open (its modal loop is what delivered this call): the
            // alarm waits its turn and is shown when that one is closed.
            if (_showing)
                return;

            _showing = true;
            try
            {
                while (_pending.Count > 0)
                    Show(_pending.Dequeue());
            }
            finally
            {
                _showing = false;
            }
        }

        private void Show(AlarmRecord alarm)
        {
            try
            {
                HiveError error = _hive.ErrorFor(alarm);

                _popup = new AlarmPopup(
                    alarm.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    error.Code,
                    error.Message,
                    error.Detail,
                    // Shown in the language chosen on the Setting page, from the description
                    // of the alarm's code. HIVE is still sent the English above.
                    UiLanguage.Alarm(error.Message),
                    UiLanguage.Alarm(error.Detail, alarm.ErrorCode))
                {
                    Owner = Application.Current.MainWindow,
                    CloseAccess = _access,
                    CloseLevel = _closeLevel,
                    Closable = _closable,
                };

                // The pop-up first. Building the Alarm page (its charts and table) takes a
                // second or more, and switching before the pop-up is shown makes the pop-up
                // wait for it. The page is switched once the pop-up is on the screen.
                _popup.ContentRendered += (sender, e) => ShowAlarmPage();

                bool acknowledged = _popup.ShowDialog() == true;

                if (acknowledged)
                {
                    _hive.AlarmAcknowledged(error, _popup.ErrorDetail, _popup.RepairActionCode, _popup.OperatorId);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "Hive", "Popup", "The alarm pop-up failed: " + ex.Message, ex.ToString());
            }
            finally
            {
                _popup = null;
            }
        }

        /// <summary>Switches the GUI to the Alarm page, behind the pop-up.</summary>
        private void ShowAlarmPage()
        {
            try
            {
                _navigation?.NavigateTo(AppPageNames.Alarm);
            }
            catch (Exception ex)
            {
                // A page that fails to open must not take the pop-up with it.
                Logger.WriteLog(LogType.Error, "Hive", "Popup", "Could not switch to the Alarm page: " + ex.Message, ex.ToString());
            }
        }
    }
}
