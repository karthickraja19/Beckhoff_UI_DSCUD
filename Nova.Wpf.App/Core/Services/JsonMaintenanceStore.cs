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
    /// <see cref="IMaintenanceStore"/> in <c>Data\Settings\Maintenance.json</c>.
    /// </summary>
    /// <remarks>
    /// The fixtures are this machine's: S11.3.2 lists "front gantry" and "rear gantry" for a
    /// dispensing machine, which is what a new file starts with. Edit the file's Fixtures
    /// list to change them; the items are changed from the Setting page's Maintenance tab only.
    /// </remarks>
    public sealed class JsonMaintenanceStore : IMaintenanceStore
    {
        private const string FileName = "Maintenance.json";

        private readonly string _path;
        private readonly object _gate = new object();
        private Stored _stored;

        public JsonMaintenanceStore(string dataDirectory)
        {
            _path = Path.Combine(dataDirectory ?? string.Empty, "Settings", FileName);
            _stored = Read();
        }

        public event EventHandler Changed;

        public IReadOnlyList<string> Fixtures
        {
            get
            {
                lock (_gate)
                {
                    return _stored.Fixtures.ToList();
                }
            }
        }

        public IReadOnlyList<MaintenanceItem> Items
        {
            get
            {
                lock (_gate)
                {
                    return _stored.Items.Select(i => i.Clone()).ToList();
                }
            }
        }

        public bool Save(MaintenanceItem item, out string error)
        {
            error = null;
            if (item == null || string.IsNullOrWhiteSpace(item.HeadId) || string.IsNullOrWhiteSpace(item.Item))
            {
                error = "A maintenance item needs its fixture and its name.";
                return false;
            }

            lock (_gate)
            {
                Stored next = Copy(_stored);
                int index = IndexOf(next, item.HeadId, item.Item);
                if (index >= 0)
                    next.Items[index] = item.Clone();
                else
                    next.Items.Add(item.Clone());

                if (!Write(next, out error))
                    return false;

                _stored = next;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Remove(string headId, string item, out string error)
        {
            error = null;
            lock (_gate)
            {
                Stored next = Copy(_stored);
                int index = IndexOf(next, headId, item);
                if (index < 0)
                    return true;

                next.Items.RemoveAt(index);
                if (!Write(next, out error))
                    return false;

                _stored = next;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static int IndexOf(Stored stored, string headId, string item)
        {
            return stored.Items.FindIndex(i =>
                string.Equals((i.HeadId ?? string.Empty).Trim(), (headId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals((i.Item ?? string.Empty).Trim(), (item ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
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
            stored.Items = stored.Items ?? new List<MaintenanceItem>();
            if (stored.Fixtures == null || stored.Fixtures.Count == 0)
                stored.Fixtures = new List<string> { "Front Gantry", "Rear Gantry" };

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
                error = "The maintenance items could not be saved: " + ex.Message;
                return false;
            }
        }

        private static Stored Copy(Stored stored)
        {
            return new Stored
            {
                Fixtures = stored.Fixtures.ToList(),
                Items = stored.Items.Select(i => i.Clone()).ToList(),
            };
        }

        private sealed class Stored
        {
            public List<string> Fixtures { get; set; }

            public List<MaintenanceItem> Items { get; set; }
        }
    }
}
