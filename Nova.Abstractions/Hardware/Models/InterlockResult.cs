using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class InterlockResult
    {
        public bool CanProceed { get; set; }

        public List<String> Failures { get; set; }
    }
}

