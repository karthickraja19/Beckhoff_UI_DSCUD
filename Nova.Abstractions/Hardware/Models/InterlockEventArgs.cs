using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Hardware
{
    public class InterlockEventArgs : EventArgs
    {
        public string Name { get; set; }
        public List<String> Failures { get; set; }

        public InterlockEventArgs(string name, List<String> failures)
        {
            Name = name;
            Failures = failures;
        }
    }
}

