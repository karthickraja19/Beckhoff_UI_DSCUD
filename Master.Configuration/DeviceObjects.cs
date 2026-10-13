using System;
using System.Collections.Generic;
using System.Linq;
using Master.Configuration.Model;
using Teal.Library.Contracts;
using Teal.Library.Log;
using Teal.Library.Twincat;

namespace Master.Configuration
{
    /// <summary>
    /// Creates the Beckhoff library object for every device of the machine - steppers,
    /// servos, cylinders, vacuums, CCDs, scanners and robots - all on the one PLC card. Each
    /// kind is held by its member of the enum that lists it (<see cref="Steppers"/>,
    /// <see cref="Servos"/>, ...) in <see cref="StepperObjects"/>, <see cref="ServoObjects"/>,
    /// <see cref="CylinderObjects"/>, <see cref="VacuumObjects"/>, <see cref="CCDObjects"/>,
    /// <see cref="ScannerObjects"/> and <see cref="RobotObjects"/>.
    /// </summary>
    /// <remarks>
    /// A device is created for each member of its enum that has an entry, with the same
    /// EnumName, in its Data\*.json file (<see cref="MachineConfiguration.Configured"/>);
    /// <see cref="MachineConfiguration.Load"/> reports where an enum and its file disagree.
    /// SFC and PDCA have no library class, so nothing is created for them.
    ///
    /// As with <see cref="MachineConfiguration.Load"/>, nothing here throws or shows a
    /// message box. A device that cannot be created is reported in the list
    /// <see cref="Create"/> returns and left out; the others are still created.
    /// </remarks>
    public static class DeviceObjects
    {
        /// <summary>
        /// Creates every device on the card from CardsConfiguration.json, replacing any created
        /// before. Returns the problems found; an empty list means every device was created.
        /// </summary>
        /// <param name="ioGvl">
        /// The GVL the cylinder and vacuum I/O names in Cylinders.json and Vacuums.json are
        /// declared in, e.g. GVL_IO for GVL_IO.Light_Mask_ZAxis_Cyl_FW. Empty when the PLC
        /// declares them at the top level.
        /// </param>
        public static List<string> Create(string ioGvl)
        {
            var problems = new List<string>();

            TwincatCard card = ConfigGlobalClass.cardManager.Card;
            if (card == null)
            {
                StepperObjects.StepperMotorObj.Clear();
                ServoObjects.ServoMotorObj.Clear();
                CylinderObjects.CylinderObjs.Clear();
                VacuumObjects.VacuumObjs.Clear();
                CCDObjects.CCDs.Clear();
                ScannerObjects.Scanners.Clear();
                RobotObjects.Robots.Clear();

                problems.Add("There is no PLC card in Data\\" + MachineConfiguration.CardsFile + ", so no devices were created.");
                return problems;
            }

            StepperObjects.Load(card, problems);
            ServoObjects.Load(card, problems);
            CylinderObjects.Load(card, ioGvl, problems);
            VacuumObjects.Load(card, ioGvl, problems);
            CCDObjects.Load(card, problems);
            ScannerObjects.Load(card, problems);
            RobotObjects.Load(card, problems);
            return problems;
        }

        /// <summary>Creates one device and adds it under its enum member, or reports why it could not.</summary>
        internal static void Add<TEnum, TDevice>(Dictionary<TEnum, TDevice> devices, TEnum device, Func<TDevice> create, List<string> problems)
            where TEnum : struct, Enum
        {
            try
            {
                devices[device] = create();
            }
            catch (Exception ex)
            {
                string name = typeof(TEnum).Name + "." + device;
                problems.Add(name + " could not be created: " + ex.Message);
                Logger.WriteLog(LogType.Error, "Configuration", "DeviceObjects", "Could not create " + name + ": " + ex, "");
            }
        }

        /// <summary>The PLC symbol of an I/O name: {ioGvl}.{name}, or the name alone when there is no GVL. Empty for a blank name.</summary>
        internal static string Symbol(string ioGvl, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "";

            return string.IsNullOrWhiteSpace(ioGvl) ? name : ioGvl + "." + name;
        }

        /// <summary>The PLC symbols of a list of I/O names, blank names left out.</summary>
        internal static List<string> Symbols(string ioGvl, List<string> names)
        {
            return names == null
                ? new List<string>()
                : names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Symbol(ioGvl, n)).ToList();
        }

        /// <summary>
        /// The library properties of a TCP device: the file's values over the library's defaults
        /// for that kind of device, which a value left out of the file keeps. The device's PLC
        /// block is {DBName}.{dutName} from CardsConfiguration.json; with an empty DUT name the
        /// library cannot create the device and says so.
        /// </summary>
        internal static TProperties NetworkProperties<TProperties>(NetworkDeviceConfiguration config, TwincatCard card, string dutName)
            where TProperties : NetworkDeviceProperties, new()
        {
            var properties = new TProperties
            {
                Id = config.Id,
                Name = config.Name,
                EnumName = config.EnumName,
                StationName = config.StationName,
            };

            if (!string.IsNullOrWhiteSpace(dutName))
                properties.InstancePath = string.IsNullOrWhiteSpace(card.DBName) ? dutName.Trim() : card.DBName + "." + dutName.Trim();
            if (config.ConnectTimeout.HasValue)
                properties.ConnectTimeout = config.ConnectTimeout.Value;
            if (config.ResponseTimeout.HasValue)
                properties.ResponseTimeout = config.ResponseTimeout.Value;
            if (config.PollIntervalMs.HasValue)
                properties.PollIntervalMs = config.PollIntervalMs.Value;

            return properties;
        }
    }
}
