using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public interface IInputOutput
    {
        int InputOffsetPort { get; set; }
        int OutputOffsetPort { get; set; }
        bool[] DIValues { get; }
        bool[] DOValues { get; }

        bool ReadDIBit(ushort DIBit, ref bool Status);
        bool WriteDOBit(int OpNum, bool value);
        bool[] RefreshDI();
        bool[] RefreshDO();
        bool RefreshAISetup(bool IsInitial);
        double[] RefreshAIvalues();
    }
}

