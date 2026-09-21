using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class RobotStateChangedEventArgs : EventArgs
    {
        public RobotState OldState { get; }
        public RobotState NewState { get;  }
    }
}

