using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Calibration
{
    /// <summary>
    /// One calibration's taught positions as a table: what its columns are, and how its
    /// file (Data\Calibration\*.json, through <see cref="ICalibrationService"/>) becomes
    /// rows and back. <see cref="CalibrationTableViewModel"/> shows any of them.
    /// </summary>
    public abstract class CalibrationTable
    {
        public abstract string Title { get; }

        public abstract string Icon { get; }

        public abstract CalibrationKind Kind { get; }

        /// <summary>
        /// True when the table on screen is one station's (5 axis, camera needle) and the
        /// station box picks which; false when every station is a row of the one table.
        /// </summary>
        public abstract bool OneTablePerStation { get; }

        public abstract IReadOnlyList<DynamicColumnSchema> Columns { get; }

        /// <summary>The stations in the file, in order.</summary>
        public abstract IReadOnlyList<int> Stations { get; }

        /// <summary>Reads the file. Throws when it cannot be read.</summary>
        public abstract void Load(ICalibrationService service);

        /// <summary>Writes what <see cref="Apply"/> last took into the file. Throws when it cannot be written.</summary>
        public abstract void Save(ICalibrationService service);

        public abstract bool GetTaught(int station);

        public abstract void SetTaught(int station, bool taught);

        /// <summary>The rows to show: the station's table, or every station when there is one table.</summary>
        public abstract List<Dictionary<string, string>> Rows(int station);

        /// <summary>Takes the rows back, in the order <see cref="Rows"/> gave them. A blank cell is 0.</summary>
        public abstract void Apply(int station, IReadOnlyList<Dictionary<string, string>> rows);

        protected const string NameKey = "Name";

        protected static DynamicColumnSchema NameColumn(string title)
        {
            return new DynamicColumnSchema { Key = NameKey, Title = title, IsIdentifier = true, IsReadOnly = true, Width = 150 };
        }

        protected static DynamicColumnSchema ValueColumn(string key, string title)
        {
            return new DynamicColumnSchema { Key = key, Title = title, DataType = ColumnDataType.Numeric };
        }

        protected static string Text(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        protected static double Number(Dictionary<string, string> row, string key)
        {
            string raw;
            if (row == null || !row.TryGetValue(key, out raw) || string.IsNullOrWhiteSpace(raw))
                return 0d;

            double value;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return value;
            }

            throw new FormatException("'" + raw + "' is not a number.");
        }

        protected static string StationName(int station)
        {
            return "Station " + station;
        }
    }

    /// <summary>9 point calibration: one row per station, the taught start position X / Y.</summary>
    public sealed class NinePointTable : CalibrationTable
    {
        private List<NinePointTeach> _stations = new List<NinePointTeach>();

        public override string Title => "9-Point Calibration";
        public override string Icon => "LayoutGrid";
        public override CalibrationKind Kind => CalibrationKind.NinePoint;
        public override bool OneTablePerStation => false;

        public override IReadOnlyList<DynamicColumnSchema> Columns { get; } = new[]
        {
            NameColumn("Station ID"),
            ValueColumn("X", "X-Axis position"),
            ValueColumn("Y", "Y-Axis position")
        };

        public override IReadOnlyList<int> Stations => _stations.Select(s => s.Station).ToList();

        public override void Load(ICalibrationService service) => _stations = service.LoadNinePoint().ToList();

        public override void Save(ICalibrationService service) => service.SaveNinePoint(_stations);

        public override bool GetTaught(int station) => _stations.Any(s => s.Station == station && s.Taught);

        public override void SetTaught(int station, bool taught)
        {
            foreach (NinePointTeach s in _stations.Where(s => s.Station == station))
                s.Taught = taught;
        }

        public override List<Dictionary<string, string>> Rows(int station)
        {
            return _stations.Select(s => new Dictionary<string, string>
            {
                { NameKey, StationName(s.Station) },
                { "X", Text(s.X) },
                { "Y", Text(s.Y) }
            }).ToList();
        }

        public override void Apply(int station, IReadOnlyList<Dictionary<string, string>> rows)
        {
            for (int i = 0; i < _stations.Count && i < rows.Count; i++)
            {
                _stations[i].X = Number(rows[i], "X");
                _stations[i].Y = Number(rows[i], "Y");
            }
        }
    }

    /// <summary>Needle module calibration: one row per station, the taught position X / Y / Z.</summary>
    public sealed class NeedleModuleTable : CalibrationTable
    {
        private List<NeedleModuleTeach> _stations = new List<NeedleModuleTeach>();

        public override string Title => "Needle Module Calibration";
        public override string Icon => "Crosshair";
        public override CalibrationKind Kind => CalibrationKind.NeedleModule;
        public override bool OneTablePerStation => false;

        public override IReadOnlyList<DynamicColumnSchema> Columns { get; } = new[]
        {
            NameColumn("Station ID"),
            ValueColumn("X", "X-Axis position"),
            ValueColumn("Y", "Y-Axis position"),
            ValueColumn("Z", "Z-Axis position")
        };

        public override IReadOnlyList<int> Stations => _stations.Select(s => s.Station).ToList();

        public override void Load(ICalibrationService service) => _stations = service.LoadNeedleModule().ToList();

        public override void Save(ICalibrationService service) => service.SaveNeedleModule(_stations);

        public override bool GetTaught(int station) => _stations.Any(s => s.Station == station && s.Taught);

        public override void SetTaught(int station, bool taught)
        {
            foreach (NeedleModuleTeach s in _stations.Where(s => s.Station == station))
                s.Taught = taught;
        }

        public override List<Dictionary<string, string>> Rows(int station)
        {
            return _stations.Select(s => new Dictionary<string, string>
            {
                { NameKey, StationName(s.Station) },
                { "X", Text(s.X) },
                { "Y", Text(s.Y) },
                { "Z", Text(s.Z) }
            }).ToList();
        }

        public override void Apply(int station, IReadOnlyList<Dictionary<string, string>> rows)
        {
            for (int i = 0; i < _stations.Count && i < rows.Count; i++)
            {
                _stations[i].X = Number(rows[i], "X");
                _stations[i].Y = Number(rows[i], "Y");
                _stations[i].Z = Number(rows[i], "Z");
            }
        }
    }

    /// <summary>5 axis calibration: per station, a row per taught angle (A-0 .. A-20, C-0 .. C-20) with A, C, X, Y, Z.</summary>
    public sealed class FiveAxisTable : CalibrationTable
    {
        private List<FiveAxisTeach> _stations = new List<FiveAxisTeach>();

        public override string Title => "5-Axis Calibration";
        public override string Icon => "Move";
        public override CalibrationKind Kind => CalibrationKind.FiveAxis;
        public override bool OneTablePerStation => true;

        public override IReadOnlyList<DynamicColumnSchema> Columns { get; } = new[]
        {
            NameColumn("Axis Name"),
            ValueColumn("A", "A"),
            ValueColumn("C", "C"),
            ValueColumn("X", "X"),
            ValueColumn("Y", "Y"),
            ValueColumn("Z", "Z")
        };

        public override IReadOnlyList<int> Stations => _stations.Select(s => s.Station).ToList();

        public override void Load(ICalibrationService service) => _stations = service.LoadFiveAxis().ToList();

        public override void Save(ICalibrationService service) => service.SaveFiveAxis(_stations);

        public override bool GetTaught(int station) => _stations.Any(s => s.Station == station && s.Taught);

        public override void SetTaught(int station, bool taught)
        {
            foreach (FiveAxisTeach s in _stations.Where(s => s.Station == station))
                s.Taught = taught;
        }

        public override List<Dictionary<string, string>> Rows(int station)
        {
            FiveAxisTeach teach = _stations.FirstOrDefault(s => s.Station == station);
            if (teach == null)
                return new List<Dictionary<string, string>>();

            return teach.Points.Select(p => new Dictionary<string, string>
            {
                { NameKey, p.Name },
                { "A", Text(p.A) },
                { "C", Text(p.C) },
                { "X", Text(p.X) },
                { "Y", Text(p.Y) },
                { "Z", Text(p.Z) }
            }).ToList();
        }

        public override void Apply(int station, IReadOnlyList<Dictionary<string, string>> rows)
        {
            FiveAxisTeach teach = _stations.FirstOrDefault(s => s.Station == station);
            if (teach == null)
                return;

            for (int i = 0; i < teach.Points.Count && i < rows.Count; i++)
            {
                teach.Points[i].A = Number(rows[i], "A");
                teach.Points[i].C = Number(rows[i], "C");
                teach.Points[i].X = Number(rows[i], "X");
                teach.Points[i].Y = Number(rows[i], "Y");
                teach.Points[i].Z = Number(rows[i], "Z");
            }
        }
    }

    /// <summary>
    /// Camera needle calibration: per station, nine rows, each a glue point (where the
    /// needle dispenses the dot) beside its vision point (where the camera looks at it).
    /// </summary>
    public sealed class CameraNeedleTable : CalibrationTable
    {
        private List<CameraNeedleTeach> _stations = new List<CameraNeedleTeach>();

        public override string Title => "Camera Needle Calibration";
        public override string Icon => "Camera";
        public override CalibrationKind Kind => CalibrationKind.CameraNeedle;
        public override bool OneTablePerStation => true;

        public override IReadOnlyList<DynamicColumnSchema> Columns { get; } = new[]
        {
            NameColumn("Point"),
            ValueColumn("GlueX", "Glue X"),
            ValueColumn("GlueY", "Glue Y"),
            ValueColumn("VisionX", "Vision X"),
            ValueColumn("VisionY", "Vision Y")
        };

        public override IReadOnlyList<int> Stations => _stations.Select(s => s.Station).ToList();

        public override void Load(ICalibrationService service) => _stations = service.LoadCameraNeedle().ToList();

        public override void Save(ICalibrationService service) => service.SaveCameraNeedle(_stations);

        public override bool GetTaught(int station) => _stations.Any(s => s.Station == station && s.Taught);

        public override void SetTaught(int station, bool taught)
        {
            foreach (CameraNeedleTeach s in _stations.Where(s => s.Station == station))
                s.Taught = taught;
        }

        public override List<Dictionary<string, string>> Rows(int station)
        {
            var rows = new List<Dictionary<string, string>>();
            CameraNeedleTeach teach = _stations.FirstOrDefault(s => s.Station == station);
            if (teach == null)
                return rows;

            for (int i = 0; i < teach.Glue.Count && i < teach.Vision.Count; i++)
            {
                rows.Add(new Dictionary<string, string>
                {
                    { NameKey, (i + 1).ToString(CultureInfo.InvariantCulture) },
                    { "GlueX", Text(teach.Glue[i].X) },
                    { "GlueY", Text(teach.Glue[i].Y) },
                    { "VisionX", Text(teach.Vision[i].X) },
                    { "VisionY", Text(teach.Vision[i].Y) }
                });
            }

            return rows;
        }

        public override void Apply(int station, IReadOnlyList<Dictionary<string, string>> rows)
        {
            CameraNeedleTeach teach = _stations.FirstOrDefault(s => s.Station == station);
            if (teach == null)
                return;

            for (int i = 0; i < teach.Glue.Count && i < teach.Vision.Count && i < rows.Count; i++)
            {
                teach.Glue[i].X = Number(rows[i], "GlueX");
                teach.Glue[i].Y = Number(rows[i], "GlueY");
                teach.Vision[i].X = Number(rows[i], "VisionX");
                teach.Vision[i].Y = Number(rows[i], "VisionY");
            }
        }
    }
}
