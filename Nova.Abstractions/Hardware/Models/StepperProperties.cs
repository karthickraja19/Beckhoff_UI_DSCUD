using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class StepperProperties
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public ICard Card { get; set; }
        public bool AxisEnabled { get; set; }
        public int ForwardCommand { get; set; }
        public int ReverseCommand { get; set; }
        public int AlarmResetCommand { get; set; }
        public int AlarmSignal { get; set; }
        public double Pitch { get; set; }
        public double Pulse { get; set; }
        public double PPR { get; set; }
        public double Speed { get; set; }
        public double Acceleration { get; set; }
        public double Deacceleration { get; set; }
        
    }
}

