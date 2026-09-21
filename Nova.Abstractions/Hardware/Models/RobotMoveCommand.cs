using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class RobotMoveCommand
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Speed { get; }
    }
}

