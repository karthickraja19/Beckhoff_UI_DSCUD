using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;
using TwinCAT.Ads;

namespace Nova.Alarm
{
    /// <summary>
    /// Subscribes to the PLC's alarm list over ADS: GVL_Alarm.fbAlarmManager.aAlarms, which
    /// is an ARRAY[1..MAX_ALARMS] OF ST_Alarms. The PLC sends the whole list every time it
    /// changes, so the HMI never has to ask for it.
    /// </summary>
    /// <remarks>
    /// The subscription is two ADS notifications:
    /// <list type="bullet">
    /// <item>the alarm list - the PLC checks it every 100 ms and sends it only when it changed</item>
    /// <item>the PLC's run state - so the HMI knows when the PLC stops and the subscription is lost</item>
    /// </list>
    /// A new PLC program or the PLC leaving Run throws its notifications away. Then, or when
    /// the PLC cannot be reached, the HMI subscribes again every 5 seconds until the PLC is
    /// back in Run. That retry is the only timer here, and it only runs while there is no
    /// subscription.
    ///
    /// Both notifications go over the HMI's one shared ADS connection
    /// (<see cref="IPlcConnection"/>), which this reader uses but does not own.
    ///
    /// Each list arrives as raw bytes and is cut into entries using the layout below. The
    /// layout is ST_Alarms as the PLC compiles it (see alarm.tmc in the PLC project). If
    /// ST_Alarms changes in the PLC, update the numbers below to match.
    /// </remarks>
    public sealed class PlcAlarmReader : IDisposable
    {
        public const string AlarmListSymbol = "GVL_Alarm.fbAlarmManager.aAlarms";

        // ---- ST_Alarms layout: where each field starts inside one entry ----
        // A PLC STRING[n] takes n + 1 bytes: the text plus a closing zero byte.
        private const int EntrySize = 180;
        private const int StationNameOffset = 0;          // STRING[30]
        private const int ComponentNameOffset = 31;       // STRING[30]
        private const int ErrorCodeOffset = 62;           // STRING[30]
        private const int ErrorTypeOffset = 93;           // STRING[30]
        private const int ErrorDescriptionOffset = 124;   // STRING[50]
        private const int LevelOffset = 176;              // INT (byte 175 is padding)
        private const int ModuleNoOffset = 178;           // INT

        /// <summary>
        /// The PLC compares the value every 100 ms and sends it only when it changed - and
        /// once straight after subscribing, with the value as it is now.
        /// </summary>
        private static readonly NotificationSettings OnChange = new NotificationSettings(AdsTransMode.OnChange, 100, 0);

        /// <summary>How long to wait before subscribing again when the PLC is not there.</summary>
        private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

        // A TwinCAT STRING is one byte per character, Windows-1252.
        private static readonly Encoding PlcText = Encoding.GetEncoding(1252);

        private readonly IPlcConnection _plc;
        private readonly object _sync = new object();

        private bool _started;
        private volatile Subscription _subscription;   // null = not subscribed
        private Timer _retry;                          // only runs while not subscribed
        private int _subscribeQueued;                  // 1 while a Subscribe is waiting to run
        private bool _subscribeFailing;

        /// <summary>
        /// Raised with the alarms in the PLC's list every time the list changes, and once
        /// after each subscribe. Raised on the ADS thread, so handlers must be quick.
        /// </summary>
        public event EventHandler<PlcAlarmList> AlarmsChanged;

        public PlcAlarmReader(IPlcConnection plc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        }

        public void Start()
        {
            lock (_sync)
            {
                if (_started)
                    return;

                _started = true;
                _plc.Client.AdsNotification += OnAdsNotification;
                _plc.Client.AdsNotificationsInvalidated += OnNotificationsInvalidated;
            }

            // Not on the caller's thread: reaching a PLC that is not running can take seconds.
            QueueSubscribe();
        }

        public void Stop()
        {
            lock (_sync)
            {
                if (!_started)
                    return;

                _started = false;
                StopRetry();
                Unsubscribe();
                _plc.Client.AdsNotification -= OnAdsNotification;
                _plc.Client.AdsNotificationsInvalidated -= OnNotificationsInvalidated;
            }
        }

        /// <summary>The connection is shared and belongs to its creator, so it is left open.</summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>Subscribes on a worker thread. Several requests close together run once.</summary>
        private void QueueSubscribe()
        {
            if (Interlocked.Exchange(ref _subscribeQueued, 1) == 0)
                ThreadPool.QueueUserWorkItem(_ => Subscribe());
        }

        /// <summary>
        /// Subscribes to the alarm list and the PLC state, dropping the old subscription
        /// first. If the PLC is not there or not in Run, tries again in 5 seconds.
        /// </summary>
        private void Subscribe()
        {
            Interlocked.Exchange(ref _subscribeQueued, 0);

            lock (_sync)
            {
                if (!_started)
                    return;

                StopRetry();
                Unsubscribe();

                try
                {
                    // Reconnects if the connection dropped. A PLC that is not running has no
                    // alarm list to send.
                    if (!_plc.IsConnected)
                        throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

                    lock (_plc.SyncRoot)
                    {
                        AdsClient client = _plc.Client;
                        var subscription = new Subscription { EntryCount = ReadEntryCount(client) };

                        // Set before subscribing: the first notification can arrive before
                        // AddDeviceNotification returns.
                        _subscription = subscription;

                        subscription.StateHandle = client.AddDeviceNotification(
                            (uint)AdsReservedIndexGroup.DeviceData, (uint)AdsReservedIndexOffset.DeviceDataAdsState,
                            sizeof(ushort), OnChange, subscription.StateTag);

                        subscription.ListHandle = client.AddDeviceNotification(
                            AlarmListSymbol, subscription.EntryCount * EntrySize, OnChange, subscription.ListTag);
                    }

                    if (_subscribeFailing)
                        Logger.WriteLog(LogType.Info, "Alarm", "PLC", "Subscribed to the alarm list " + AlarmListSymbol + " again", "");

                    _subscribeFailing = false;
                }
                catch (Exception ex)
                {
                    Unsubscribe();

                    // Logged once, not every 5 seconds.
                    if (!_subscribeFailing)
                    {
                        _subscribeFailing = true;
                        LogError("Cannot subscribe to the alarm list " + AlarmListSymbol + " - trying again every "
                            + RetryAfter.TotalSeconds + " s", ex);
                    }

                    _retry = new Timer(_ => Subscribe(), null, RetryAfter, Timeout.InfiniteTimeSpan);
                }
            }
        }

        /// <summary>Drops the current subscription, if there is one.</summary>
        private void Unsubscribe()
        {
            Subscription old = _subscription;
            _subscription = null;

            if (old == null)
                return;

            lock (_plc.SyncRoot)
            {
                AdsClient client = _plc.Client;
                if (!client.IsConnected)
                    return;

                // After a new PLC program the handles are already gone, so a failure here is normal.
                if (old.ListHandle != 0)
                    client.TryDeleteDeviceNotification(old.ListHandle);

                if (old.StateHandle != 0)
                    client.TryDeleteDeviceNotification(old.StateHandle);
            }
        }

        private void StopRetry()
        {
            if (_retry != null)
            {
                _retry.Dispose();
                _retry = null;
            }
        }

        /// <summary>
        /// A notification arrived. This runs on the ADS thread: no locks and no ADS calls in
        /// here, or the connection can deadlock - anything that needs the PLC is queued.
        /// </summary>
        private void OnAdsNotification(object sender, AdsNotificationEventArgs e)
        {
            try
            {
                Subscription current = _subscription;
                if (current == null)
                    return;

                if (e.UserData == current.StateTag)
                    OnPlcState(e);
                else if (e.UserData == current.ListTag)
                    OnAlarmList(current, e);

                // Anything else is from a subscription that has since been replaced, or
                // belongs to another user of the shared connection.
            }
            catch (Exception ex)
            {
                LogError("Could not handle a notification from the PLC", ex);
            }
        }

        private void OnPlcState(AdsNotificationEventArgs e)
        {
            // No data means the PLC dropped the notification: subscribe again.
            if (e.Data.Length < sizeof(ushort))
            {
                QueueSubscribe();
                return;
            }

            // Leaving Run throws the subscription away. Subscribe keeps trying until the
            // PLC is back in Run.
            var state = (AdsState)BitConverter.ToUInt16(e.Data.ToArray(), 0);
            if (state != AdsState.Run)
                QueueSubscribe();
        }

        private void OnAlarmList(Subscription subscription, AdsNotificationEventArgs e)
        {
            // No data means the PLC dropped the notification (e.g. a new program, or
            // ST_Alarms changed by an online change): subscribe again, which also checks
            // the array size.
            if (e.Data.Length != subscription.EntryCount * EntrySize)
            {
                QueueSubscribe();
                return;
            }

            // The PLC's own time stamp for this list. It runs on this PC, so it is the same clock.
            var list = new PlcAlarmList(ReadAlarms(e.Data.ToArray(), subscription.EntryCount), e.TimeStamp.LocalDateTime);

            AlarmsChanged?.Invoke(this, list);
        }

        /// <summary>The PLC dropped some notifications - usually a new PLC program. Subscribe again.</summary>
        private void OnNotificationsInvalidated(object sender, AdsNotificationsInvalidatedEventArgs e)
        {
            QueueSubscribe();
        }

        /// <summary>
        /// Number of entries in the PLC array (MAX_ALARMS), worked out from the array's size.
        /// Also catches the common mistake of ST_Alarms being changed in the PLC but not here.
        /// </summary>
        private static int ReadEntryCount(AdsClient client)
        {
            int arraySize = client.ReadSymbol(AlarmListSymbol).ByteSize;

            if (arraySize == 0 || arraySize % EntrySize != 0)
            {
                throw new InvalidOperationException(
                    AlarmListSymbol + " is " + arraySize + " bytes, which is not a whole number of "
                    + EntrySize + "-byte ST_Alarms entries. ST_Alarms was changed in the PLC - "
                    + "update the layout in PlcAlarmReader to match.");
            }

            return arraySize / EntrySize;
        }

        /// <summary>Cuts the raw list into entries, leaving out the empty slots.</summary>
        private static List<PlcAlarm> ReadAlarms(byte[] data, int entryCount)
        {
            var alarms = new List<PlcAlarm>();
            for (int i = 0; i < entryCount; i++)
            {
                PlcAlarm alarm = ReadEntry(data, i * EntrySize);
                if (!alarm.IsEmpty)
                    alarms.Add(alarm);
            }

            return alarms;
        }

        private static PlcAlarm ReadEntry(byte[] data, int start)
        {
            return new PlcAlarm
            {
                StationName = ReadString(data, start + StationNameOffset, 31),
                ComponentName = ReadString(data, start + ComponentNameOffset, 31),
                ErrorCode = ReadString(data, start + ErrorCodeOffset, 31),
                ErrorType = ReadString(data, start + ErrorTypeOffset, 31),
                ErrorDescription = ReadString(data, start + ErrorDescriptionOffset, 51),
                Level = BitConverter.ToInt16(data, start + LevelOffset),
                ModuleNo = BitConverter.ToInt16(data, start + ModuleNoOffset),
            };
        }

        /// <summary>Reads a PLC STRING: the text runs up to the first zero byte.</summary>
        private static string ReadString(byte[] data, int start, int size)
        {
            int length = 0;
            while (length < size && data[start + length] != 0)
                length++;

            return PlcText.GetString(data, start, length);
        }

        private static void LogError(string message, Exception ex)
        {
            Logger.WriteLog(LogType.Error, "Alarm", "PLC", message + ": " + ex.Message, ex.ToString());
        }

        /// <summary>
        /// One subscription: its two notification handles, and the tags that tell its
        /// notifications apart from each other and from an older subscription's.
        /// </summary>
        private sealed class Subscription
        {
            public readonly object ListTag = new object();
            public readonly object StateTag = new object();
            public int EntryCount;
            public uint ListHandle;
            public uint StateHandle;
        }
    }

    /// <summary>The PLC's alarm list at one moment, as one notification delivered it.</summary>
    public sealed class PlcAlarmList
    {
        public PlcAlarmList(List<PlcAlarm> alarms, DateTime time)
        {
            Alarms = alarms;
            Time = time;
        }

        /// <summary>The alarms in the list, empty slots left out.</summary>
        public List<PlcAlarm> Alarms { get; }

        /// <summary>When the PLC saw the list like this.</summary>
        public DateTime Time { get; }
    }
}
