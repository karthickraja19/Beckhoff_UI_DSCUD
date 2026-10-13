using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Master.Configuration
{
    #region List of all Steppers
    /// <summary>
    /// List of all I/O steppers. A stepper driven as an NC axis is a servo, listed in
    /// <see cref="Servos"/> and Data\Servos.json instead.
    /// </summary>
    public enum Steppers
    {
        LiftingStepper0,
        LiftingStepper1,
        LiftingStepper2,
        LiftingStepper3
    }
    #endregion
}
