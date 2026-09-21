using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration
{
    public struct AxisParam
    {
        public string sCardType;
        public short iCardNo;
        public short iAxisNo;
        public string sAxisName;
        public double PPR;
        public double pitch;
        public double pulse_mm;
        public double Acc;
        public double DeAcc;
        public double AutoSpeed;
        public double ManualSpeed;
        public double HomingSpeed;
    }
}
