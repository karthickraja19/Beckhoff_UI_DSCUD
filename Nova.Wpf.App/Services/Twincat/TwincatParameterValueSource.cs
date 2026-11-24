using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nova.Abstractions.Services;
using Nova.Alarm;
using Nova.Wpf.App.Services.Twincat.Mapping;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// <see cref="IParameterValueSource"/> from the PLC: the Parameter Dashboard's live
    /// values (DFM slide 43), each spec-limit parameter read from the PLC variable the
    /// symbol map (<c>Data\PlcSymbols.json</c>, section Parameters) gives it.
    /// </summary>
    /// <remarks>
    /// The map names a parameter (the Parameter column of Config &gt; Spec Limits) with a
    /// symbol and its PLC type, e.g.
    /// <c>"front_cartridge_temperature": { "Symbol": "GVL_HMI.rFrontCartridgeTemp", "Type": "REAL" }</c>.
    /// The symbols are ADS notifications, watched for as long as the HMI runs - the PLC is
    /// never polled.
    /// </remarks>
    public sealed class TwincatParameterValueSource : IParameterValueSource
    {
        private readonly PlcSubscriptions _subscriptions;
        private readonly Dictionary<string, string> _symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <param name="map">The symbol map's Parameters section: parameter name to PLC variable and type.</param>
        public TwincatParameterValueSource(
            PlcSubscriptions subscriptions, IDictionary<string, ParameterSymbol> map, ICollection<string> warnings)
        {
            _subscriptions = subscriptions;

            foreach (var pair in (map ?? new Dictionary<string, ParameterSymbol>())
                .Where(p => p.Value != null && !string.IsNullOrWhiteSpace(p.Value.Symbol)))
            {
                Type type = TypeOf(pair.Value.Type);
                if (type == null)
                {
                    warnings?.Add("Parameter " + pair.Key + " in " + PlcSymbolMap.FileName + " has PLC type \"" + pair.Value.Type
                                  + "\"; use LREAL, REAL, DINT, INT, UDINT, UINT or BOOL.");
                    continue;
                }

                _symbols[pair.Key] = pair.Value.Symbol.Trim();
                _subscriptions.Watch(pair.Value.Symbol.Trim(), type);
            }
        }

        /// <summary>How many parameters have a PLC variable.</summary>
        public int MappedCount
        {
            get { return _symbols.Count; }
        }

        public Task<IReadOnlyDictionary<string, double>> ReadAsync(IReadOnlyCollection<string> keys)
        {
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in keys ?? (IReadOnlyCollection<string>)new string[0])
            {
                string symbol;
                object value;
                if (_symbols.TryGetValue(key, out symbol) && _subscriptions.TryGet(symbol, out value))
                {
                    try
                    {
                        values[key] = value is bool ? ((bool)value ? 1 : 0) : Convert.ToDouble(value);
                    }
                    catch (Exception)
                    {
                        // A value that is not a number is left out, as an unread one is.
                    }
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, double>>(values);
        }

        private static Type TypeOf(string plcType)
        {
            switch ((plcType ?? "LREAL").Trim().ToUpperInvariant())
            {
                case "LREAL": return typeof(double);
                case "REAL": return typeof(float);
                case "DINT": return typeof(int);
                case "INT": return typeof(short);
                case "UDINT": return typeof(uint);
                case "UINT": return typeof(ushort);
                case "BOOL": return typeof(bool);
                default: return null;
            }
        }
    }
}
