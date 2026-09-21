using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class RobotErrorEventArgs : EventArgs
    {
        public string Code { get; }
        public string Description { get; }
    }
}

