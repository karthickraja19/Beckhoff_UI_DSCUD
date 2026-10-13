using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nova.Abstractions.Services
{
    /// <summary>What a robot I/O point holds.</summary>
    public enum RobotIOKind
    {
        Bool,
        Byte,

        /// <summary>A WORD the robot sends as two BYTEs, given as one.</summary>
        Word
    }

    /// <summary>One point of a robot's I/O image, as last read.</summary>
    public sealed class RobotIOPointSnapshot
    {
        public string Name { get; set; }
        public RobotIOKind Kind { get; set; }

        /// <summary>0 or 1 for a Bool; the number for a Byte or Word.</summary>
        public int Value { get; set; }

        public bool IsOn => Value != 0;
    }

    /// <summary>One robot's connection and I/O, as last read.</summary>
    public sealed class RobotIOSnapshot
    {
        public bool IsConnected { get; set; }
        public IReadOnlyList<RobotIOPointSnapshot> Inputs { get; set; }
        public IReadOnlyList<RobotIOPointSnapshot> Outputs { get; set; }
    }

    /// <summary>
    /// The robots' EtherCAT I/O, read from the PLC - separate from the machine's digital IO
    /// (<see cref="IDigitalIOService"/>), which does not include it. A robot is addressed by its
    /// name. The spares are not points. Only a Bool output can be written; inputs, and the Byte
    /// and Word outputs, are read only.
    /// </summary>
    /// <remarks>
    /// An implementation that only reads the PLC while a screen shows the robots is an
    /// <see cref="Hardware.ILiveStatus"/>; until then every point reads 0 and the robot reads
    /// disconnected. <see cref="IOChanged"/> may be raised on a background thread, so handlers
    /// must only set scalar INotifyPropertyChanged properties.
    /// </remarks>
    public interface IRobotIOService
    {
        /// <summary>The robots' names, in the order of the machine's robot list.</summary>
        IReadOnlyList<string> Robots { get; }

        /// <summary>The robot's connection and every point as last read; null for an unknown robot.</summary>
        RobotIOSnapshot Read(string robot);

        /// <summary>Sets one Bool output. False for an unknown robot or point, or when the PLC is not there.</summary>
        Task<bool> WriteOutputAsync(string robot, string name, bool on);

        /// <summary>Raised when a read finds something changed, and after a write (background thread).</summary>
        event EventHandler IOChanged;
    }
}
