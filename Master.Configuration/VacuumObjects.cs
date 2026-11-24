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

        /// <summary>As with the cylinders, the sensor and output names are Data\Config.csv points, kept for the labels.</summary>
        internal static void Load(TwincatCard card, List<string> problems)
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
                        VacuumSense = DeviceObjects.IoName(config.VacuumSense),
                        VacuumOutput = DeviceObjects.IoName(config.VacuumOutput),
                        BlowOutput = DeviceObjects.IoName(config.BlowOutput),
                    };

                    return new Vacuum(card, properties);
                }, problems);
            }
        }
    }
}
