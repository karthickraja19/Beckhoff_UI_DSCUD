using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nova.Wpf.App.Services.Twincat.HmiDisplay;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Where each device's live status comes from. The Beckhoff library says where the
    /// device's HMI display struct is in the PLC - its <see cref="SymbolProperty"/>, for
    /// example MAIN.servocall.servo[1].HMI_DISPLAY - and App.config names the struct's PLC
    /// type for each kind of device, which is also the name of the C# struct in
    /// Services\Twincat\HmiDisplay that reads it.
    /// </summary>
    public static class HmiDisplays
    {
        /// <summary>
        /// The property of a library device (AxisServo, the steppers, Cylinder, Vacuum,
        /// TcpDevice) that gives the full PLC path of its HMI display struct.
        /// </summary>
        /// <remarks>
        /// Read by name, so the HMI builds and runs with a library that does not have it yet:
        /// such a device shows no live status, and the startup warnings say so.
        /// </remarks>
        public const string SymbolProperty = "HmiDisplaySymbol";

        /// <summary>The device's HMI display symbol from the library, or null when the library gives none.</summary>
        public static string SymbolOf(object libraryDevice)
        {
            if (libraryDevice == null)
                return null;

            PropertyInfo property = libraryDevice.GetType().GetProperty(SymbolProperty, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(string))
                return null;

            var symbol = (string)property.GetValue(libraryDevice);
            return string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();
        }

        /// <summary>
        /// The C# struct named <paramref name="typeName"/> - the PLC type App.config gives under
        /// <paramref name="settingKey"/> - or null, reported in <paramref name="warnings"/>,
        /// when there is none that reads a <typeparamref name="TDisplay"/>. PLC names are not
        /// case sensitive, so neither is this.
        /// </summary>
        public static Type Resolve<TDisplay>(string settingKey, string typeName, ICollection<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                warnings.Add(settingKey + " is blank in Data\\PlcSymbols.json, so those devices show no live status. "
                    + "Set it to the PLC type of their HMI display struct.");
                return null;
            }

            string name = typeName.Trim();
            string ns = typeof(IAxisHmiDisplay).Namespace;
            Type type = typeof(HmiDisplays).Assembly.GetTypes()
                .FirstOrDefault(t => t.IsValueType && t.Namespace == ns && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

            if (type == null)
            {
                warnings.Add(settingKey + " in Data\\PlcSymbols.json is " + name + ", but the HMI has no struct of that name in "
                    + "Services\\Twincat\\HmiDisplay, so those devices show no live status.");
                return null;
            }

            if (!typeof(TDisplay).IsAssignableFrom(type))
            {
                warnings.Add(settingKey + " in Data\\PlcSymbols.json is " + type.Name + ", which is for another kind of device, "
                    + "so those devices show no live status.");
                return null;
            }

            return type;
        }
    }
}
