using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;
using Teal.Library.Contracts;
using Teal.Library.Log;
using Teal.Library.Twincat;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// The robots' EtherCAT I/O. Each robot's inputs and outputs are read through the Beckhoff
    /// library's <see cref="TwincatRobot"/> - ReadAllInput and ReadAllOutput, the PLC's
    /// Robot{Id}_hw_IO_GVL.hwInputs and hwOutputs, the spares left out - and a BOOL output is set
    /// with its WriteOutput. The machine's digital IO (Data\Config.csv) does not include them.
    /// </summary>
    /// <remarks>
    /// As with the digital IO, by decision: no subscription. The robots are read every
    /// <see cref="PollMs"/> only while something follows them (<see cref="FollowStatus"/>) - the
    /// Robot tab, while it is on screen - and <see cref="IOChanged"/> is raised when a value
    /// changes. With no follower nothing is read, every point reads 0 and every robot reads
    /// disconnected, so a tab opened later does not show old values.
    /// </remarks>
    public sealed class TwincatRobotIOService : IRobotIOService, ILiveStatus
    {
        /// <summary>How often the robots are read while they are followed.</summary>
        private const int PollMs = 200;

        private readonly TwincatPlcConnection _plc;
        private readonly List<string> _names;
        private readonly List<TwincatRobot> _robots;
        private readonly Dictionary<string, int> _indexes;
        private readonly RobotIOSnapshot[] _idle;

        /// <summary>Guards the followers, the timer and storing what was read.</summary>
        private readonly object _sync = new object();
        private int _followers;
        private Timer _timer;
        private int _reading;                      // 1 while a read is running
        private volatile RobotIOSnapshot[] _snapshots;

        public event EventHandler IOChanged;

        /// <param name="robots">Each robot with the name it is shown by, in the order of the robot list.</param>
        public TwincatRobotIOService(IEnumerable<KeyValuePair<string, TwincatRobot>> robots, TwincatPlcConnection plc)
        {
            if (robots == null)
                throw new ArgumentNullException(nameof(robots));

            _plc = plc ?? throw new ArgumentNullException(nameof(plc));

            var list = robots.ToList();
            _names = list.Select(r => r.Key).ToList();
            _robots = list.Select(r => r.Value).ToList();
            _indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _names.Count; i++)
            {
                if (!_indexes.ContainsKey(_names[i]))
                    _indexes[_names[i]] = i;
            }

            _idle = _robots.Select(r => Snapshot(false, r.InputPoints, r.OutputPoints)).ToArray();
            _snapshots = _idle;
        }

        public IReadOnlyList<string> Robots => _names;

        public RobotIOSnapshot Read(string robot)
        {
            int index;
            return _indexes.TryGetValue(robot ?? "", out index) ? _snapshots[index] : null;
        }

        /// <summary>Sets the output through the library's WriteOutput; the next read shows what the PLC made of it.</summary>
        public Task<bool> WriteOutputAsync(string robot, string name, bool on)
        {
            int index;
            if (!_indexes.TryGetValue(robot ?? "", out index))
                return Task.FromResult(false);

            TwincatRobot device = _robots[index];
            return Task.Run(() =>
            {
                bool written;
                try
                {
                    lock (_plc.SyncRoot)
                    {
                        written = _plc.IsConnected && device.WriteOutput(name, on);
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error, "Robot", "PLC", "Could not write " + robot + " output " + name + ": " + ex.Message, ex.ToString());
                    written = false;
                }

                // Show the result straight away rather than at the next read.
                if (_followers > 0)
                    ThreadPool.QueueUserWorkItem(_ => Poll());

                return written;
            });
        }

        /// <summary>
        /// Reads the robots from the PLC every <see cref="PollMs"/> until the returned handle is
        /// disposed. Each call counts: the reading stops when the last handle is disposed.
        /// </summary>
        public IDisposable FollowStatus()
        {
            lock (_sync)
            {
                if (_followers++ == 0)
                    _timer = new Timer(_ => Poll(), null, 0, PollMs);
            }

            return new Following(this);
        }

        private void Release()
        {
            lock (_sync)
            {
                if (--_followers > 0)
                    return;

                _timer.Dispose();
                _timer = null;
                _snapshots = _idle;
            }

            Raise();
        }

        /// <summary>
        /// One read of every robot. Runs on a worker thread; a read still running when the next
        /// is due is not overlapped. With the PLC not there, everything reads 0 and disconnected.
        /// </summary>
        private void Poll()
        {
            if (Interlocked.Exchange(ref _reading, 1) == 1)
                return;

            try
            {
                var read = new RobotIOSnapshot[_robots.Count];

                lock (_plc.SyncRoot)
                {
                    bool up = _plc.IsConnected;
                    for (int i = 0; i < _robots.Count; i++)
                        read[i] = up ? ReadRobot(_robots[i]) : _idle[i];
                }

                bool changed;
                lock (_sync)
                {
                    // Let go of while reading: keep nothing, so the next visit starts clean.
                    if (_followers == 0)
                        return;

                    changed = !Same(read, _snapshots);
                    _snapshots = read;
                }

                if (changed)
                    Raise();
            }
            finally
            {
                Interlocked.Exchange(ref _reading, 0);
            }
        }

        private RobotIOSnapshot ReadRobot(TwincatRobot robot)
        {
            try
            {
                return Snapshot(robot.IsConnected, robot.ReadAllInput(), robot.ReadAllOutput());
            }
            catch (Exception ex)
            {
                // The library logs its own read failures; this is anything it let through.
                System.Diagnostics.Debug.WriteLine("Robot read failed: " + ex.Message);
                return Snapshot(false, robot.InputPoints, robot.OutputPoints);
            }
        }

        private static RobotIOSnapshot Snapshot(bool connected, IReadOnlyList<RobotIOPoint> inputs, IReadOnlyList<RobotIOPoint> outputs)
        {
            return new RobotIOSnapshot
            {
                IsConnected = connected,
                Inputs = Points(inputs),
                Outputs = Points(outputs),
            };
        }

        private static IReadOnlyList<RobotIOPointSnapshot> Points(IReadOnlyList<RobotIOPoint> points)
        {
            return (points ?? new RobotIOPoint[0])
                .Select(p => new RobotIOPointSnapshot { Name = p.Name, Kind = Kind(p.Type), Value = p.Value })
                .ToList();
        }

        private static RobotIOKind Kind(RobotIOType type)
        {
            switch (type)
            {
                case RobotIOType.Byte: return RobotIOKind.Byte;
                case RobotIOType.Word: return RobotIOKind.Word;
                default: return RobotIOKind.Bool;
            }
        }

        private static bool Same(RobotIOSnapshot[] a, RobotIOSnapshot[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].IsConnected != b[i].IsConnected
                    || !a[i].Inputs.Select(p => p.Value).SequenceEqual(b[i].Inputs.Select(p => p.Value))
                    || !a[i].Outputs.Select(p => p.Value).SequenceEqual(b[i].Outputs.Select(p => p.Value)))
                {
                    return false;
                }
            }

            return true;
        }

        private void Raise()
        {
            try
            {
                IOChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                // Raised on a worker thread: a faulty screen must not take it down.
                System.Diagnostics.Debug.WriteLine("A robot IO handler failed: " + ex.Message);
            }
        }

        /// <summary>One <see cref="FollowStatus"/> call; disposing it lets go, once.</summary>
        private sealed class Following : IDisposable
        {
            private TwincatRobotIOService _owner;

            public Following(TwincatRobotIOService owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.Release();
            }
        }
    }
}
