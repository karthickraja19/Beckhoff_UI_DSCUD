using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Delegate to handle write operations for digital outputs.
    /// </summary>
    /// <param name="value">The boolean value to write (true = ON, false = OFF).</param>
    /// <param name="OpNum">The operation number or channel index.</param>
    /// <returns>True if successful, otherwise false.</returns>
    public delegate bool WriteOpsDelegate(bool value, int OpNum);
}

