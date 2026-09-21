using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public enum RobotState
    {
        Idle,
        Initializing,
        Homing,
        Ready,
        Running,
        Error,
        Stopped,
        EmergencyStopped,
        Disconnected,
    }
}

