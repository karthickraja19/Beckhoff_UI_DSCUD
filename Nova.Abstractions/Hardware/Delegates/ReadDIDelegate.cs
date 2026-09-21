using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    /// <summary>
    /// Delegate to handle read operations for digital inputs.
    /// </summary>
    /// <returns>An array of booleans representing input states.</returns>
    public delegate bool[] ReadDIDelegate();
}

