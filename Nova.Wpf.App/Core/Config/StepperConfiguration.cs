using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Core.Config
{
    public class StepperConfiguration
    {
        public string EnumName { get; set; }
        public string Name { get; set; }
        public bool IsAxisEnabled { get; set; }
        public int CardId { get; set; }
        public int Id { get; set; }
        public int Pitch { get; set; }
        public int Pulse { get; set; }
        public int PPR { get; set; }
        public int Acceleration { get; set; }
        public int Deceleration { get; set; }
        public int Speed { get; set; }
        public string ForwardCommand { get; set; }
        public string ReverseCommand { get; set; }
        public string AlarmResetCommand { get; set; }
        public string AlarmSignal { get; set; }
    }
}
