using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IStaticDynamicService"/> over the PLC's GVL_StaticDynamic (symbol map:
    /// Calibration.StaticDynamic). Static_Seq / Dynamic_Seq run the whole test; this only writes
    /// the three settings (nMode, nCcd, nIteration), pulses their buttons and reads back what
    /// they report.
    /// </summary>
    /// <remarks>
    /// Start, End and Stop are rising edges (R_TRIG in the sequences), each held TRUE for 200 ms.
    /// The status is followed with ADS notifications only while the tab is on screen; the
    /// PLC's status text, a STRING, is read once each time one of the followed values changes.
    /// The settings are part of the followed values, so the tab shows what the PLC holds - the
    /// main sequence reads nMode for every unit, and it can also have been set from elsewhere.
    /// </remarks>
    public sealed class TwincatStaticDynamicService : IStaticDynamicService
    {
        private const int PulseMs = 200;

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly StaticDynamicSymbols _map;
        private readonly object _statusGate = new object();

        private StaticDynamicStatus _status = StaticDynamicStatus.NotLive;
        private int _followers;
        private int _refreshQueued;

        public TwincatStaticDynamicService(TwincatPlcConnection plc, PlcSubscriptions subscriptions, CalibrationSymbols map)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _map = (map != null ? map.StaticDynamic : null) ?? new StaticDynamicSymbols();

            _subscriptions.ValueChanged += OnValueChanged;
            _subscriptions.LiveChanged += (sender, e) => QueueRefresh();
        }

        public bool IsAvailable => _map.CanRun;

        public string UnavailableReason => IsAvailable
            ? string.Empty
            : "Calibration.StaticDynamic (Mode, Ccd, Iteration, Start, End, Stop) is blank in Data\\" + PlcSymbolMap.FileName
              + ": the PLC has no Static / Dynamic interface.";

        public int CameraCount => Math.Max(1, _map.Cameras);

        public StaticDynamicStatus Status
        {
            get
            {
                lock (_statusGate)
                {
                    return _status;
                }
            }
        }

        public event EventHandler StatusChanged;

        // ==================================================================
        // Commands
        // ==================================================================

        public Task ApplyAsync(StaticDynamicMode mode, int ccd, int iteration)
        {
            if (!IsAvailable)
                return Task.FromException(new InvalidOperationException(UnavailableReason));

            return Task.Run(() =>
            {
                RequireLive();
                WriteSettings(mode, ccd, iteration);
            });
        }

        public Task StartAsync(StaticDynamicMode mode, int ccd, int iteration)
        {
            if (!IsAvailable)
                return Task.FromException(new InvalidOperationException(UnavailableReason));

            return Task.Run(() =>
            {
                RequireLive();
                WriteSettings(mode, ccd, iteration);
                Pulse(_map.Start);
                Logger.WriteLog(LogType.Info, "PLC", "StaticDynamic",
                    "Start " + mode + " on CCD " + ccd + ", iteration " + iteration + ".", string.Empty);
            });
        }

        public Task EndAsync()
        {
            return PulseAsync(_map.End, "End");
        }

        public Task StopAsync()
        {
            return PulseAsync(_map.Stop, "Stop");
        }

        private void WriteSettings(StaticDynamicMode mode, int ccd, int iteration)
        {
            if (ccd < 1 || ccd > CameraCount)
                throw new ArgumentOutOfRangeException(nameof(ccd), "CCD " + ccd + " is not one the PLC serves (1 to " + CameraCount + ").");
            if (iteration < 1 || iteration > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(iteration), "The iteration count must be 1 or more.");

            // The camera and the count first: the mode is what makes the main sequence act on them.
            _plc.WriteSymbol(_map.Ccd.Trim(), (short)ccd);
            _plc.WriteSymbol(_map.Iteration.Trim(), (short)iteration);
            _plc.WriteSymbol(_map.Mode.Trim(), (short)mode);
        }

        private Task PulseAsync(string symbol, string name)
        {
            if (!PlcSymbolMap.Has(symbol))
            {
                return Task.FromException(new InvalidOperationException(
                    "Calibration.StaticDynamic." + name + " is blank in Data\\" + PlcSymbolMap.FileName + "."));
            }

            return Task.Run(() =>
            {
                RequireLive();
                Pulse(symbol);
                Logger.WriteLog(LogType.Info, "PLC", "StaticDynamic", name + " pressed.", string.Empty);
            });
        }

        private void RequireLive()
        {
            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");
        }

        /// <summary>The sequences act on the rising edge and do not clear the bit themselves.</summary>
        private void Pulse(string symbol)
        {
            _plc.WriteSymbol(symbol.Trim(), true);
            try
            {
                Thread.Sleep(PulseMs);
            }
            finally
            {
                _plc.WriteSymbol(symbol.Trim(), false);
            }
        }

        // ==================================================================
        // Status
        // ==================================================================

        public IDisposable FollowStatus()
        {
            var symbols = new List<KeyValuePair<string, Type>>();
            foreach (string flag in new[] { _map.Busy, _map.Done, _map.Error })
            {
                if (PlcSymbolMap.Has(flag))
                    symbols.Add(new KeyValuePair<string, Type>(flag.Trim(), typeof(bool)));
            }
            foreach (string number in new[] { _map.Mode, _map.Ccd, _map.Iteration, _map.Step, _map.Count })
            {
                if (PlcSymbolMap.Has(number))
                    symbols.Add(new KeyValuePair<string, Type>(number.Trim(), typeof(short)));
            }
            foreach (string value in new[] { _map.VisionX, _map.VisionY, _map.VisionZ })
            {
                if (PlcSymbolMap.Has(value))
                    symbols.Add(new KeyValuePair<string, Type>(value.Trim(), typeof(double)));
            }

            IDisposable following = _subscriptions.Follow(symbols);
            Interlocked.Increment(ref _followers);
            QueueRefresh();
            return new Handle(() =>
            {
                following.Dispose();
                Interlocked.Decrement(ref _followers);
                QueueRefresh();
            });
        }

        /// <summary>Raised on the ADS thread: only queues the refresh.</summary>
        private void OnValueChanged(object sender, string symbol)
        {
            if (Volatile.Read(ref _followers) < 1 || !IsStatusSymbol(symbol))
                return;

            QueueRefresh();
        }

        private bool IsStatusSymbol(string symbol)
        {
            foreach (string own in new[]
                     {
                         _map.Busy, _map.Done, _map.Error, _map.Mode, _map.Ccd, _map.Iteration, _map.Step, _map.Count,
                         _map.VisionX, _map.VisionY, _map.VisionZ
                     })
            {
                if (PlcSymbolMap.Has(own) && string.Equals(symbol, own.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>One refresh waiting at a time; a change that arrives while one runs queues the next.</summary>
        private void QueueRefresh()
        {
            if (Interlocked.Exchange(ref _refreshQueued, 1) == 1)
                return;

            Task.Run(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                try
                {
                    Refresh();
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "PLC", "StaticDynamic", "The Static / Dynamic status could not be read: " + ex.Message, ex.ToString());
                }
            });
        }

        private void Refresh()
        {
            StaticDynamicStatus status;
            if (Volatile.Read(ref _followers) < 1)
            {
                status = StaticDynamicStatus.NotLive;
            }
            else if (!_subscriptions.IsLive)
            {
                status = new StaticDynamicStatus { NotLiveReason = _subscriptions.NotLiveReason ?? "PLC not connected" };
            }
            else
            {
                status = new StaticDynamicStatus
                {
                    IsLive = true,
                    Mode = ModeOf(Number(_map.Mode)),
                    Ccd = Number(_map.Ccd),
                    Iteration = Number(_map.Iteration),
                    IsBusy = Flag(_map.Busy),
                    IsDone = Flag(_map.Done),
                    IsError = Flag(_map.Error),
                    Step = Number(_map.Step),
                    Count = Number(_map.Count),
                    Message = ReadMessage(),
                    VisionX = Real(_map.VisionX),
                    VisionY = Real(_map.VisionY),
                    VisionZ = Real(_map.VisionZ)
                };
            }

            lock (_statusGate)
            {
                _status = status;
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private static StaticDynamicMode ModeOf(int value)
        {
            return value == (int)StaticDynamicMode.Static ? StaticDynamicMode.Static
                : value == (int)StaticDynamicMode.Dynamic ? StaticDynamicMode.Dynamic
                : StaticDynamicMode.Off;
        }

        private bool Flag(string symbol)
        {
            return PlcSymbolMap.Has(symbol) && _subscriptions.Get<bool>(symbol.Trim());
        }

        private int Number(string symbol)
        {
            return PlcSymbolMap.Has(symbol) ? _subscriptions.Get<short>(symbol.Trim()) : 0;
        }

        private double Real(string symbol)
        {
            return PlcSymbolMap.Has(symbol) ? _subscriptions.Get<double>(symbol.Trim()) : 0.0;
        }

        /// <summary>The PLC's CURRENT STATUS text, read once per change of its status.</summary>
        private string ReadMessage()
        {
            if (!PlcSymbolMap.Has(_map.Status))
                return string.Empty;

            try
            {
                return Convert.ToString(_plc.ReadSymbol(_map.Status.Trim())) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private sealed class Handle : IDisposable
        {
            private Action _dispose;

            public Handle(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                Action dispose = Interlocked.Exchange(ref _dispose, null);
                if (dispose != null)
                    dispose();
            }
        }
    }
}
