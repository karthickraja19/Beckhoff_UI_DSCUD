using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public enum CardInitStatus
    {
        Success,
        DllVersionMismatch,
        CardUnavailable,
        OpenFailure,
        Failure
    }
}

