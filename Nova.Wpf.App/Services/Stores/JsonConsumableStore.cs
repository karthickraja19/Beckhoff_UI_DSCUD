using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="IConsumableStore"/> in <c>Data\Settings\Consumables.json</c> - the
    /// "configuration file" S3.1 records the valve SN in.
    /// </summary>
    public sealed class JsonConsumableStore : IConsumableStore
    {
        private const string FileName = "Consumables.json";

        private readonly string _path;
        private readonly object _gate = new object();
        private Stored _stored;

        public JsonConsumableStore(string dataDirectory)
        {
            _path = Path.Combine(dataDirectory ?? string.Empty, "Settings", FileName);
            _stored = Read();
        }

        public event EventHandler Changed;

        public IReadOnlyList<GantryConsumables> Gantries
        {
            get
            {
                lock (_gate)
                {
                    return _stored.Gantries.Select(g => g.Clone()).ToList();
                }
            }
        }

        public IReadOnlyList<ValveModel> ValveModels
        {
            get
            {
                lock (_gate)
                {
                    return _stored.ValveModels.ToList();
                }
            }
        }

        public IReadOnlyDictionary<string, string> Materials
        {
            get
            {
                lock (_gate)
                {
                    return new Dictionary<string, string>(_stored.Materials, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public IReadOnlyList<string> MaterialNames
        {
            get
            {
                lock (_gate)
                {
                    return _stored.MaterialNames.ToList();
                }
            }
        }

        public bool SaveGantry(GantryConsumables gantry, out string error)
        {
            error = null;
            if (gantry == null || string.IsNullOrWhiteSpace(gantry.Gantry))
            {
                error = "No gantry to save.";
                return false;
            }

            lock (_gate)
            {
                Stored next = Copy(_stored);
                int index = next.Gantries.FindIndex(g => string.Equals(g.Gantry, gantry.Gantry, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    next.Gantries[index] = gantry.Clone();
                else
                    next.Gantries.Add(gantry.Clone());

                if (!Write(next, out error))
                    return false;

                _stored = next;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool SaveMaterials(IDictionary<string, string> materials, out string error)
        {
            error = null;
            lock (_gate)
            {
                Stored next = Copy(_stored);
                foreach (var pair in materials ?? new Dictionary<string, string>())
                    next.Materials[pair.Key] = pair.Value ?? string.Empty;

                if (!Write(next, out error))
                    return false;

                _stored = next;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private Stored Read()
        {
            Stored stored = null;
            try
            {
                if (File.Exists(_path))
                    stored = JsonConvert.DeserializeObject<Stored>(File.ReadAllText(_path));
            }
            catch (Exception)
            {
                // Unreadable: start from the defaults below rather than refuse to start.
            }

            stored = stored ?? new Stored();
            if (stored.Gantries == null || stored.Gantries.Count == 0)
            {
                stored.Gantries = new List<GantryConsumables>
                {
                    new GantryConsumables { Gantry = "Front" },
                    new GantryConsumables { Gantry = "Rear" },
                };
            }

            if (stored.ValveModels == null || stored.ValveModels.Count == 0)
            {
                // S3.1: the three models whose SN the software checks, and Others.
                stored.ValveModels = new List<ValveModel>
                {
                    new ValveModel { Name = "EFD-PICO" },
                    new ValveModel { Name = "ANDA-PV30A" },
                    new ValveModel { Name = "KOTL-PH30" },
                    new ValveModel { Name = "Others" },
                };
            }

            if (stored.MaterialNames == null || stored.MaterialNames.Count == 0)
                stored.MaterialNames = new List<string> { "Material" };

            stored.Materials = new Dictionary<string, string>(stored.Materials ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            return stored;
        }

        private bool Write(Stored stored, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, JsonConvert.SerializeObject(stored, Formatting.Indented), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = "The consumables file could not be saved: " + ex.Message;
                return false;
            }
        }

        private static Stored Copy(Stored stored)
        {
            return new Stored
            {
                Gantries = stored.Gantries.Select(g => g.Clone()).ToList(),
                ValveModels = stored.ValveModels.ToList(),
                MaterialNames = stored.MaterialNames.ToList(),
                Materials = new Dictionary<string, string>(stored.Materials, StringComparer.OrdinalIgnoreCase),
            };
        }

        private sealed class Stored
        {
            public List<GantryConsumables> Gantries { get; set; }

            public List<ValveModel> ValveModels { get; set; }

            public List<string> MaterialNames { get; set; }

            public Dictionary<string, string> Materials { get; set; }
        }
    }
}
