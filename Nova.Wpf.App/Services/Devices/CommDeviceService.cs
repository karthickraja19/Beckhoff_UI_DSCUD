using System.Collections.Generic;
using System.Linq;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// Registry of TCP comm devices grouped by kind (CCD/Scanner/SFC),
    /// supplied by the composition root from the machine's *.json config.
    /// </summary>
    public sealed class CommDeviceService : ICommDeviceService
    {
        private readonly Dictionary<CommDeviceKind, List<CommDevice>> _byKind;

        public CommDeviceService(IEnumerable<KeyValuePair<CommDeviceKind, CommDevice>> devices)
        {
            _byKind = new Dictionary<CommDeviceKind, List<CommDevice>>();
            foreach (var pair in devices)
            {
                if (!_byKind.TryGetValue(pair.Key, out var list))
                    _byKind[pair.Key] = list = new List<CommDevice>();
                list.Add(pair.Value);
            }
        }

        public IReadOnlyList<CommDevice> GetDevices(CommDeviceKind kind)
            => _byKind.TryGetValue(kind, out var list) ? list : new List<CommDevice>();
    }
}
