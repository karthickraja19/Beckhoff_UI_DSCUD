using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Master.Sequences.FiveAxis;
using Nova.Wpf.App.Services.Twincat.Mapping;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IFiveAxisMotion"/> over the PLC's CNC channel interface (ST_ChannelData, symbol map
    /// section Cnc): a pose is one G-code block written to the channel's MDI string and started with
    /// the channel's cycle start, so X, Y, Z, A and C move together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One move: MDI mode (a rising edge on <c>bMDIMode</c>, waited for on <c>bMDIModeEnabled</c>), the
    /// block into <c>sMDIProgram</c>, <c>bCycleStart</c> held TRUE for 200 ms (the PLC acts on the rising
    /// edge). It is finished when <c>bCycleRunning</c> has been TRUE and is FALSE again, and every axis
    /// is within <see cref="FiveAxisSettings.InPositionTolerance"/> of its target (its actual position,
    /// <c>stAxisCmdStatus[n].rActPosACS</c>, so the program must not use a work offset).
    /// A <c>bErrorState</c> on the channel ends the move with an error.
    /// </para>
    /// <para>
    /// The block uses the CNC program's own axis names (FiveAxisSettings.GCodeX ... GCodeC; the channel
    /// of Seq_NC_Program.nc has Y1, B1, C1, X2, Z2). Stop pulses the channel's cycle stop and reset.
    /// </para>
    /// </remarks>
    public sealed class TwincatFiveAxisMotion : IFiveAxisMotion
    {
        private const int PulseMs = 200;
        private const int PollMs = 10;
        private static readonly TimeSpan ModeTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan InPositionTimeout = TimeSpan.FromSeconds(5);

        private readonly TwincatPlcConnection _plc;
        private readonly CncSymbols _cnc;

        public TwincatFiveAxisMotion(TwincatPlcConnection plc, CncSymbols cnc)
        {
            _plc = plc ?? throw new ArgumentNullException(nameof(plc));
            _cnc = cnc ?? new CncSymbols();
        }

        public bool IsAvailable => UnavailableReason.Length == 0;

        public string UnavailableReason
        {
            get
            {
                if (!_cnc.IsMapped)
                    return "Cnc.Channel is blank in Data\\" + PlcSymbolMap.FileName + ": the CNC channel interface is not mapped.";

                CncMemberSymbols m = _cnc.Members;
                var needed = new[]
                {
                    new KeyValuePair<string, string>("MdiModeRequest", m.MdiModeRequest),
                    new KeyValuePair<string, string>("MdiModeActive", m.MdiModeActive),
                    new KeyValuePair<string, string>("MdiProgram", m.MdiProgram),
                    new KeyValuePair<string, string>("CycleStart", m.CycleStart),
                    new KeyValuePair<string, string>("CycleRunning", m.CycleRunning),
                    new KeyValuePair<string, string>("ErrorState", m.ErrorState),
                    new KeyValuePair<string, string>("AxisActualPosition", m.AxisActualPosition),
                };
                foreach (KeyValuePair<string, string> member in needed)
                {
                    if (!PlcSymbolMap.Has(member.Value))
                        return "Cnc.Members." + member.Key + " is blank in Data\\" + PlcSymbolMap.FileName + ": the PLC has no variable for it yet.";
                }
                return string.Empty;
            }
        }

        public FiveAxisPose ReadPose(int station, FiveAxisSettings settings)
        {
            RequireLive();
            int channel = PlcChannel(station);
            return new FiveAxisPose
            {
                X = ReadPosition(channel, settings.AxisX),
                Y = ReadPosition(channel, settings.AxisY),
                Z = ReadPosition(channel, settings.AxisZ),
                A = ReadPosition(channel, settings.AxisA),
                C = ReadPosition(channel, settings.AxisC),
            };
        }

        public Task MoveAsync(int station, FiveAxisPose target, FiveAxisSettings settings, CancellationToken token)
        {
            if (!IsAvailable)
                return Task.FromException(new InvalidOperationException(UnavailableReason));

            return Task.Run(() => Move(station, target, settings, token), token);
        }

        public Task StopAsync(int station, bool reset)
        {
            return Task.Run(() =>
            {
                try
                {
                    int channel = PlcChannel(station);
                    Pulse(_cnc.PathOf(channel, _cnc.Members.CycleStop));
                    if (reset)
                        Pulse(_cnc.PathOf(channel, _cnc.Members.Reset));
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "CNC", "FiveAxis", "The CNC channel could not be stopped: " + ex.Message, ex.ToString());
                }
            });
        }

        // ==================================================================
        // One move
        // ==================================================================

        private void Move(int station, FiveAxisPose target, FiveAxisSettings s, CancellationToken token)
        {
            RequireLive();
            int channel = PlcChannel(station);
            CncMemberSymbols m = _cnc.Members;

            string running = _cnc.PathOf(channel, m.CycleRunning);
            string error = _cnc.PathOf(channel, m.ErrorState);

            if (ReadBool(running))
                throw new InvalidOperationException("a CNC program is already running in channel " + channel);
            if (ReadBool(error))
                throw new InvalidOperationException("the CNC channel " + channel + " is in error: reset it first");

            string block = Block(target, s);
            if (s.IsAuto)
            {
                SelectAutoProgram(channel, block, s, token);
            }
            else
            {
                SelectMdiMode(channel, token);
                _plc.WriteSymbol(_cnc.PathOf(channel, m.MdiProgram), block);
            }
            Logger.WriteLog(LogType.Info, "CNC", "FiveAxis", "Channel " + channel + " (" + (s.IsAuto ? "Auto" : "MDI") + "): " + block, string.Empty);

            Pulse(_cnc.PathOf(channel, m.CycleStart));

            // The program is under way when the channel says it is running, and finished when it says it is not.
            DateTime startDeadline = DateTime.UtcNow + StartTimeout;
            bool started = false;
            DateTime moveDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Max(1000, s.MoveTimeoutMs));

            while (true)
            {
                Check(token, station);

                if (ReadBool(error))
                {
                    StopNow(channel);
                    throw new InvalidOperationException("the CNC channel " + channel + " went to error during the move to " + target);
                }

                bool isRunning = ReadBool(running);
                if (isRunning)
                    started = true;
                else if (started)
                    break;
                else if (DateTime.UtcNow > startDeadline)
                    break;   // a block that was over before the first look; the position check decides

                if (DateTime.UtcNow > moveDeadline)
                {
                    StopNow(channel);
                    throw new TimeoutException("the move to " + target + " took longer than " + s.MoveTimeoutMs + " ms");
                }

                Thread.Sleep(PollMs);
            }

            if (s.InPositionTolerance > 0)
                WaitInPosition(channel, target, s, token, station);
        }

        private void SelectMdiMode(int channel, CancellationToken token)
        {
            string active = _cnc.PathOf(channel, _cnc.Members.MdiModeActive);
            if (ReadBool(active))
                return;

            string request = _cnc.PathOf(channel, _cnc.Members.MdiModeRequest);
            _plc.WriteSymbol(request, false);
            _plc.WriteSymbol(request, true);

            DateTime deadline = DateTime.UtcNow + ModeTimeout;
            while (!ReadBool(active))
            {
                token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("channel " + channel + " did not go to MDI mode");
                Thread.Sleep(20);
            }
        }

        /// <summary>
        /// Auto mode: the block and an M30 are written to FiveAxisSettings.AutoProgramFile, the program path is
        /// cleared and set again so the CNC loads the new text, and the channel is put in Auto mode.
        /// </summary>
        private void SelectAutoProgram(int channel, string block, FiveAxisSettings s, CancellationToken token)
        {
            CncMemberSymbols m = _cnc.Members;
            string path = _cnc.PathOf(channel, m.ProgramPath);
            string request = _cnc.PathOf(channel, m.AutoModeRequest);
            string active = _cnc.PathOf(channel, m.AutoModeActive);
            if (path.Length == 0 || request.Length == 0)
                throw new InvalidOperationException("Cnc.Members.ProgramPath / AutoModeRequest are blank in Data\\" + PlcSymbolMap.FileName + ": Auto mode is not mapped.");

            System.IO.File.WriteAllText(s.AutoProgramFile, block + "\r\nM30\r\n");

            _plc.WriteSymbol(path, string.Empty);
            Thread.Sleep(100);
            _plc.WriteSymbol(path, s.AutoProgramFile);

            if (active.Length > 0 && ReadBool(active))
                return;

            _plc.WriteSymbol(request, false);
            _plc.WriteSymbol(request, true);

            if (active.Length == 0)
                return;

            DateTime deadline = DateTime.UtcNow + ModeTimeout;
            while (!ReadBool(active))
            {
                token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("channel " + channel + " did not go to Auto mode");
                Thread.Sleep(20);
            }
        }

        private void WaitInPosition(int channel, FiveAxisPose target, FiveAxisSettings s, CancellationToken token, int station)
        {
            var axes = new[]
            {
                new KeyValuePair<string, double>(s.AxisX, target.X),
                new KeyValuePair<string, double>(s.AxisY, target.Y),
                new KeyValuePair<string, double>(s.AxisZ, target.Z),
                new KeyValuePair<string, double>(s.AxisA, target.A),
                new KeyValuePair<string, double>(s.AxisC, target.C),
            };

            DateTime deadline = DateTime.UtcNow + InPositionTimeout;
            string worst;
            do
            {
                Check(token, station);

                worst = string.Empty;
                foreach (KeyValuePair<string, double> axis in axes)
                {
                    double actual = ReadPosition(channel, axis.Key);
                    if (Math.Abs(actual - axis.Value) > s.InPositionTolerance)
                    {
                        worst = axis.Key + " is at " + actual.ToString("0.###", CultureInfo.InvariantCulture)
                            + " but should be at " + axis.Value.ToString("0.###", CultureInfo.InvariantCulture);
                        break;
                    }
                }

                if (worst.Length == 0)
                    return;

                Thread.Sleep(20);
            }
            while (DateTime.UtcNow < deadline);

            throw new InvalidOperationException("the move to " + target + " did not finish: " + worst
                + " (tolerance " + s.InPositionTolerance.ToString(CultureInfo.InvariantCulture) + "). Check the CNC program's axis names and that no work offset is active.");
        }

        /// <summary>G90 G01 X2=.. Y1=.. Z2=.. B1=.. C1=.. F..: the CNC program's own axis names, absolute, one block.</summary>
        internal static string Block(FiveAxisPose pose, FiveAxisSettings s)
        {
            return "G90 G01"
                + " " + s.GCodeX + "=" + Number(pose.X)
                + " " + s.GCodeY + "=" + Number(pose.Y)
                + " " + s.GCodeZ + "=" + Number(pose.Z)
                + " " + s.GCodeA + "=" + Number(pose.A)
                + " " + s.GCodeC + "=" + Number(pose.C)
                + " F" + Number(s.FeedMmPerMin);
        }

        private static string Number(double value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        // ==================================================================
        // PLC access
        // ==================================================================

        private int PlcChannel(int station)
        {
            CncChannelSymbols channel = _cnc.ChannelMap(station);
            if (channel == null || channel.PlcChannel < 1)
                throw new InvalidOperationException("Station " + station + " has no CNC channel (Cnc.Channels in Data\\" + PlcSymbolMap.FileName + ").");
            return channel.PlcChannel;
        }

        private double ReadPosition(int channel, string axisName)
        {
            int index;
            if (_cnc.AxisIndex == null || !_cnc.AxisIndex.TryGetValue(axisName ?? string.Empty, out index) || index < 1)
                throw new InvalidOperationException("The CNC axis '" + axisName + "' has no number (Cnc.AxisIndex in Data\\" + PlcSymbolMap.FileName + ").");

            string symbol = _cnc.PathOf(channel, _cnc.Members.AxisActualPosition, index);
            return Convert.ToDouble(_plc.ReadSymbol(symbol), CultureInfo.InvariantCulture);
        }

        private bool ReadBool(string symbol)
        {
            return symbol.Length > 0 && Convert.ToBoolean(_plc.ReadSymbol(symbol));
        }

        /// <summary>The channel acts on the rising edge, so the bit is held TRUE for 200 ms.</summary>
        private void Pulse(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return;

            _plc.WriteSymbol(symbol, true);
            try
            {
                Thread.Sleep(PulseMs);
            }
            finally
            {
                _plc.WriteSymbol(symbol, false);
            }
        }

        private void StopNow(int channel)
        {
            try
            {
                Pulse(_cnc.PathOf(channel, _cnc.Members.CycleStop));
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, "CNC", "FiveAxis", "The CNC cycle stop could not be written: " + ex.Message, ex.ToString());
            }
        }

        private void Check(CancellationToken token, int station)
        {
            if (!token.IsCancellationRequested)
                return;

            StopAsync(station, reset: true).Wait(TimeSpan.FromSeconds(2));
            token.ThrowIfCancellationRequested();
        }

        private void RequireLive()
        {
            if (!_plc.IsConnected)
                throw new InvalidOperationException("the PLC at " + _plc.Address + " is not reachable or not in Run");
        }
    }
}
