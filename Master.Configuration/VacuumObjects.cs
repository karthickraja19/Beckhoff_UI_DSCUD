using Master.Configuration.Model;
using System.Collections.Generic;
using Teal.Library.Contracts;
using Teal.Library.Twincat;
using Teal.Library.Vacuum;

namespace Master.Configuration
{
    /// <summary>
    /// The library vacuum for each member of <see cref="Vacuums"/> configured in
    /// Data\Vacuums.json. Created by <see cref="DeviceObjects.Create"/>.
    /// </summary>
    public static class VacuumObjects
    {
        public static readonly Dictionary<Vacuums, IVacuum> VacuumObjs = new Dictionary<Vacuums, IVacuum>();

        /// <summary>As with the cylinders, the sensor and output names are PLC variable names under {ioGvl}.</summary>
        internal static void Load(TwincatCard card, string ioGvl, List<string> problems)
        {
            VacuumObjs.Clear();

            foreach (var vacuum in MachineConfiguration.Configured<Vacuums, VacuumConfiguration>(ConfigGlobalClass.VacuumConfigurations, c => c.EnumName))
            {
                VacuumConfiguration config = vacuum.Value;
                DeviceObjects.Add(VacuumObjs, vacuum.Key, () =>
                {
                    var properties = new VacuumProperties
                    {
                        Name = config.Name,
                        EnumName = config.EnumName,
                        Id = config.Id,
                        VacuumSense = DeviceObjects.Symbol(ioGvl, config.VacuumSense),
                        VacuumOutput = DeviceObjects.Symbol(ioGvl, config.VacuumOutput),
                        BlowOutput = DeviceObjects.Symbol(ioGvl, config.BlowOutput),
                    };

                    return new Vacuum(card, properties);
                }, problems);
            }
        }
    }
}
