using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// One axis of a CNC channel, behind <see cref="IAxis"/> so the Recipe page's CNC tab can
    /// jog it and teach from its position. Everything goes through the PLC's CNC channel
    /// interface (ST_ChannelData); every variable name comes from the symbol map, section Cnc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jog: the channel is put in jog (manual) mode with a rising edge on its jog-mode
    /// request, the axis index, jog type "user speed" and the speed are written, and the
    /// plus or minus key is held TRUE until <see cref="JogStopAsync"/> clears it. The PLC
    /// moves the axis only while the key is TRUE, so a lost HMI stops the move the moment
    /// the PLC's own watchdog or the operator clears it - the HMI never commands a distance.
    /// </para>
    /// <para>
    /// Positioning and homing are not offered: the channel interface has no single-axis
    /// home, and a positioning move belongs to the CNC program. <see cref="SupportsPositioning"/>
    /// is false and those calls return false.
    /// </para>
    /// </remarks>
    public sealed class TwincatCncAxisAdapter : IAxis, ITwincatStatusSource, ILiveStatus
    {
        /// <summary>The channel's jog type for "user defined speed" (ST_UI_To_PLC.nJogMode).</summary>
        private const ushort JogTypeUserSpeed = 2;

        private static readonly TimeSpan JogModeTimeout = TimeSpan.FromSeconds(2);

        private readonly TwincatPlcConnection _plc;
        private readonly PlcSubscriptions _subscriptions;
        private readonly CncSymbols _cnc;
        private readonly int _plcChannel;
        private readonly int _axisIndex;
        private readonly string _position;
        private readonly string _enabled;
        private readonly string _homed;
        private readonly string _moving;
        private readonly string _error;
        private readonly List<KeyValuePair<string, Type>> _watched = new List<KeyValuePair<string, Type>>();

        public TwincatCncAxisAdapter(
            TwincatPlcConnection plc, PlcSubscriptions subscriptions, CncSymbols cnc,
            int hmiChannel, int plcChannel, string axisName, int axisIndex)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _subscriptions = subscriptions ?? throw new ArgumentNullException(nameof(subscriptions));
            _cnc = cnc ?? throw new ArgumentNullException(nameof(cnc));
            _plcChannel = plcChannel;
            _axisIndex = axisIndex;
            ComponentId = IdOf(hmiChannel, axisName);

            _position = Member(cnc.Members.AxisActualPosition, typeof(float));
            _enabled = Member(cnc.Members.AxisEnabled, typeof(bool));
            _homed = Member(cnc.Members.AxisHomed, typeof(bool));
            _moving = Member(cnc.Members.AxisMoving, typeof(bool));
            _error = Member(cnc.Members.AxisError, typeof(bool));
        }

        /// <summary>The id a channel's axis names as its "Servo" in Data\CncRecipe.json.</summary>
        public static string IdOf(int hmiChannel, string axisName)
        {
            return "CNC" + hmiChannel + "_" + axisName;
        }

        private string Member(string member, Type type)
        {
            string symbol = _cnc.PathOf(_plcChannel, member, _axisIndex);
            if (symbol.Length > 0)
                _watched.Add(new KeyValuePair<string, Type>(symbol, type));

            return symbol;
        }

        private string Channel(string member)
        {
            return _cnc.PathOf(_plcChannel, member);
        }

        public string ComponentId { get; }

        public IReadOnlyList<string> WatchedSymbols => _watched.Select(w => w.Key).ToList();

        public bool IsHomed => _homed.Length > 0 && _subscriptions.Get(_homed, false);

        public bool SupportsPositioning => false;

        public IDisposable FollowStatus()
        {
            return _subscriptions.Follow(_watched);
        }

        public AxisStatusSnapshot ReadStatus()
        {
            float position;
            return new AxisStatusSnapshot
            {
                ComponentId = ComponentId,
                ActualPosition = _position.Length > 0 && _subscriptions.TryGet(_position, out position) ? position : 0d,
                IsEnabled = _enabled.Length > 0 && _subscriptions.Get(_enabled, false),
                IsInError = _error.Length > 0 && _subscriptions.Get(_error, false),
                IsHomed = IsHomed,
            };
        }

        /// <summary>The CNC axes are enabled together: one BOOL for every channel.</summary>
        public Task<bool> EnableAsync()
        {
            return WriteEnable(true);
        }

        public Task<bool> DisableAsync()
        {
            return WriteEnable(false);
        }

        private Task<bool> WriteEnable(bool on)
        {
            string symbol = PlcSymbolMap.Of(_cnc.AxisEnable);
            return Run("Enable", () =>
            {
                Require(symbol, "Cnc.AxisEnable");
                _plc.WriteSymbol(symbol, on);
            });
        }

        /// <summary>The channel's reset, pulsed: the PLC acts on the rising edge.</summary>
        public Task ResetAsync()
        {
            string symbol = Channel(_cnc.Members.Reset);
            return Run("Reset", () =>
            {
                Require(symbol, "Cnc.Members.Reset");
                _plc.WriteSymbol(symbol, true);
                Thread.Sleep(200);
                _plc.WriteSymbol(symbol, false);
            });
        }

        public Task StopAsync()
        {
            return JogStopAsync();
        }

        public Task<bool> MoveAbsAsync(double position, double speed)
        {
            return NotOffered("A positioning move");
        }

        public Task<bool> MoveRelAsync(double distance, double speed)
        {
            return NotOffered("A relative move");
        }

        public Task<bool> HomeAsync(double velocity)
        {
            return NotOffered("Homing a single axis");
        }

        private Task<bool> NotOffered(string what)
        {
            Logger.WriteLog(LogType.Error, "CNC", ComponentId,
                what + " is not offered for a CNC axis: the channel interface only jogs.", string.Empty);
            return Task.FromResult(false);
        }

        /// <param name="speed">mm/s (deg/s for a rotary axis); the PLC takes mm/min.</param>
        public Task JogStartAsync(bool forward, double speed)
        {
            string jogMode = Channel(_cnc.Members.JogModeRequest);
            string jogActive = Channel(_cnc.Members.JogModeActive);
            string jogType = Channel(_cnc.Members.JogType);
            string jogAxis = Channel(_cnc.Members.JogAxisIndex);
            string jogSpeed = Channel(_cnc.Members.JogSpeed);
            string plus = Channel(_cnc.Members.JogPlus);
            string minus = Channel(_cnc.Members.JogMinus);
            string running = Channel(_cnc.Members.CycleRunning);

            return RunOrThrow(() =>
            {
                Require(jogMode, "Cnc.Members.JogModeRequest");
                Require(jogAxis, "Cnc.Members.JogAxisIndex");
                Require(plus, "Cnc.Members.JogPlus");
                Require(minus, "Cnc.Members.JogMinus");
                if (_axisIndex < 1)
                    throw new InvalidOperationException(ComponentId + " has no axis number (Cnc.AxisIndex in " + PlcSymbolMap.FileName + ")");

                // A running CNC program owns the axes.
                if (running.Length > 0 && Convert.ToBoolean(_plc.ReadSymbol(running)))
                    throw new InvalidOperationException("a CNC program is running in channel " + _plcChannel);

                // Jog mode is selected on a rising edge; wait for the channel to say it is in it.
                if (jogActive.Length == 0 || !Convert.ToBoolean(_plc.ReadSymbol(jogActive)))
                {
                    _plc.WriteSymbol(jogMode, false);
                    _plc.WriteSymbol(jogMode, true);

                    if (jogActive.Length > 0)
                    {
                        DateTime deadline = DateTime.UtcNow + JogModeTimeout;
                        while (!Convert.ToBoolean(_plc.ReadSymbol(jogActive)))
                        {
                            if (DateTime.UtcNow > deadline)
                                throw new TimeoutException("channel " + _plcChannel + " did not go to jog mode");

                            Thread.Sleep(20);
                        }
                    }
                }

                _plc.WriteSymbol(plus, false);
                _plc.WriteSymbol(minus, false);
                _plc.WriteSymbol(jogAxis, (ushort)_axisIndex);
                if (jogType.Length > 0)
                    _plc.WriteSymbol(jogType, JogTypeUserSpeed);
                if (jogSpeed.Length > 0)
                    _plc.WriteSymbol(jogSpeed, (ushort)Math.Max(1, Math.Min(ushort.MaxValue, Math.Round(speed * 60d))));

                _plc.WriteSymbol(forward ? plus : minus, true);
            });
        }

        public Task JogStopAsync()
        {
            string plus = Channel(_cnc.Members.JogPlus);
            string minus = Channel(_cnc.Members.JogMinus);
            return Run("Jog stop", () =>
            {
                if (plus.Length > 0)
                    _plc.WriteSymbol(plus, false);
                if (minus.Length > 0)
                    _plc.WriteSymbol(minus, false);
            });
        }

        private static void Require(string symbol, string mapEntry)
        {
            if (symbol.Length == 0)
                throw new InvalidOperationException(mapEntry + " is blank in Data\\" + PlcSymbolMap.FileName);
        }

        /// <summary>A write whose failure is logged and reported as false.</summary>
        private Task<bool> Run(string what, Action write)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!_plc.IsConnected)
                        throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

                    write();
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "CNC", ComponentId, what + " could not be written to the PLC: " + ex.Message, ex.ToString());
                    return false;
                }
            });
        }

        /// <summary>A write whose failure the caller is told: a jog that did not start must be said.</summary>
        private Task RunOrThrow(Action write)
        {
            return Task.Run(() =>
            {
                if (!_plc.IsConnected)
                    throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");

                try
                {
                    write();
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "CNC", ComponentId, "Jog could not be started: " + ex.Message, ex.ToString());
                    throw;
                }
            });
        }
    }

    /// <summary>The CNC axes the symbol map describes: every axis of every mapped channel.</summary>
    public sealed class TwincatCncAxisProvider : ICncAxisProvider
    {
        public TwincatCncAxisProvider(TwincatPlcConnection plc, PlcSubscriptions subscriptions, CncSymbols cnc, IEnumerable<string> axisNames)
        {
            var axes = new List<IAxis>();
            if (cnc != null && cnc.IsMapped)
            {
                foreach (var channel in cnc.Channels ?? new Dictionary<string, CncChannelSymbols>())
                {
                    int hmiChannel;
                    if (channel.Value == null || channel.Value.PlcChannel < 1 || !int.TryParse(channel.Key, out hmiChannel))
                        continue;

                    foreach (string name in axisNames)
                    {
                        int index;
                        if (cnc.AxisIndex != null && cnc.AxisIndex.TryGetValue(name, out index) && index >= 1 && index <= cnc.AxesPerChannel)
                            axes.Add(new TwincatCncAxisAdapter(plc, subscriptions, cnc, hmiChannel, channel.Value.PlcChannel, name, index));
                    }
                }
            }

            Axes = axes;
        }

        public IReadOnlyList<IAxis> Axes { get; }
    }
}
