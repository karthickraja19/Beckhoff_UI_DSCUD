using System;
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
    /// Opens the alarm pop-up when an alarm arrives (Audio SW DFM v5.4, slide 50): the GUI
    /// switches to the Alarm page, the pop-up appears, and it stays there until the
    /// operator swipes a badge. The badge and the confirmed wording then go to HIVE.
    /// </summary>
    public sealed class AlarmPopupHost : IDisposable
    {
        private readonly AlarmMonitor _alarms;
        private readonly HiveService _hive;
        private readonly INavigationService _navigation;
        private readonly Dispatcher _uiThread;

        private AlarmPopup _popup;

        public AlarmPopupHost(AlarmMonitor alarms, HiveService hive, INavigationService navigation = null)
        {
            _alarms = alarms;
            _hive = hive;
            _navigation = navigation;
            _uiThread = Application.Current.Dispatcher;
        }

        public void Start()
        {
            _alarms.AlarmStarted += OnAlarmStarted;
        }

        public void Stop()
        {
            _alarms.AlarmStarted -= OnAlarmStarted;
        }

        public void Dispose()
        {
            Stop();
        }

        private void OnAlarmStarted(object sender, AlarmRecord alarm)
        {
            // Alarms arrive on the monitor thread; windows belong to the UI thread.
            _uiThread.BeginInvoke(new Action(() => Show(alarm)));
        }

        private void Show(AlarmRecord alarm)
        {
            // One pop-up at a time: the first alarm is the one that stopped the machine,
            // and the operator confirms that one. The rest are on the Alarm page.
            if (_popup != null)
                return;

            try
            {
                _navigation?.NavigateTo(AppPageNames.Alarm);

                HiveError error = _hive.ErrorFor(alarm);

                _popup = new AlarmPopup(
                    alarm.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    error.Code,
                    error.Message,
                    error.Detail)
                {
                    Owner = Application.Current.MainWindow,
                };

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
    }
}
