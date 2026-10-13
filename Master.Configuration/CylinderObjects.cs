using Master.Configuration.Model;
using System.Collections.Generic;
using Teal.Library.Contracts;
using Teal.Library.Cylinder;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// The library cylinder for each member of <see cref="Cylinders"/> configured in
    /// Data\Cylinders.json. Created by <see cref="DeviceObjects.Create"/>.
    /// </summary>
    public static class CylinderObjects
    {
        public static readonly Dictionary<Cylinders, ICylinder> CylinderObjs = new Dictionary<Cylinders, ICylinder>();

        /// <summary>
        /// The valve and reed names in the file are the PLC's variable names; the library takes
        /// them as full symbols, {ioGvl}.{name}. A single-valve cylinder has no retract valve.
        /// </summary>
        internal static void Load(TwincatCard card, string ioGvl, List<string> problems)
        {
            CylinderObjs.Clear();

            foreach (var cylinder in MachineConfiguration.Configured<Cylinders, CylinderConfiguration>(ConfigGlobalClass.CylinderConfigurations, c => c.EnumName))
            {
                CylinderConfiguration config = cylinder.Value;
                DeviceObjects.Add(CylinderObjs, cylinder.Key, () =>
                {
                    var properties = new CylinderProperties
                    {
                        Name = config.Name,
                        EnumName = config.EnumName,
                        Id = config.Id,
                        ExtendOutput = DeviceObjects.Symbol(ioGvl, config.ExtendOutput),
                        RetractOutput = DeviceObjects.Symbol(ioGvl, config.RetractOutput),
                        ExtendSenses = DeviceObjects.Symbols(ioGvl, config.ExtendSenses),
                        RetractSenses = DeviceObjects.Symbols(ioGvl, config.RetractSenses),
                    };

                    return new Cylinder(card, properties);
                }, problems);
            }
        }
    }
}
