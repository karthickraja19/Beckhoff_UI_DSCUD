using System;
using System.Globalization;
using Nova.Wpf.App.Services.Twincat.Mapping;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// One-off reads of mapped PLC values, for data the PLC holds for one event (a finished
    /// unit, a weighing). A blank map entry reads as null; a read that fails throws.
    /// </summary>
    public static class PlcValues
    {
        public static object Read(TwincatPlcConnection plc, string symbol)
        {
            string path = PlcSymbolMap.Of(symbol);
            return path.Length == 0 ? null : plc.ReadSymbol(path);
        }

        public static double? Number(TwincatPlcConnection plc, ParameterSymbol member)
        {
            return Number(Read(plc, member?.Symbol));
        }

        public static double? Number(object value)
        {
            if (value == null)
                return null;
            if (value is bool)
                return (bool)value ? 1 : 0;
            if (value is TimeSpan)
                return ((TimeSpan)value).TotalMilliseconds;

            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        public static bool? Flag(TwincatPlcConnection plc, ParameterSymbol member)
        {
            object value = Read(plc, member?.Symbol);
            if (value == null)
                return null;

            return value is bool ? (bool)value : Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0;
        }

        public static string Text(TwincatPlcConnection plc, ParameterSymbol member)
        {
            object value = Read(plc, member?.Symbol);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>The value as the PLC type of a scalar ("BOOL", "INT", "REAL", ...), for a write.</summary>
        public static object ToPlc(double value, string plcType)
        {
            switch ((plcType ?? "LREAL").Trim().ToUpperInvariant())
            {
                case "BOOL": return value != 0;
                case "INT": return (short)Math.Round(value);
                case "UINT": return (ushort)Math.Round(value);
                case "DINT": return (int)Math.Round(value);
                case "UDINT": return (uint)Math.Round(value);
                case "REAL": return (float)value;
                case "TIME": return (uint)Math.Max(0, Math.Round(value));
                default: return value;
            }
        }
    }
}
