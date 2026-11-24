using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nova.Abstractions.Machine.Enums
{
    public enum OperationMode
    {
        [Description("Dry Run")] DryRun,
        [Description("Production Run")] Production,
        [Description("GRR Mode")] GRR,
        [Description("CPK Mode")] CPK,
        [Description("Static Test Mode")] Static,
        [Description("Dynamic Test Mode")] Dynamic,
        [Description("Maintenance Mode")] Maintenance,
        [Description("Calibration Mode")] Calibration
    }
}



