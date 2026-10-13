using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nova.Abstractions.Data;
using Nova.Abstractions.Data.Models;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// The Data page's generated glue data (App.config <c>SampleData=true</c>): weighings,
    /// calibration regression, cartridge counts, pot life and nozzle health for a D-SCUD's
    /// front and rear gantries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cartridge, pot-life and nozzle figures are the worked example of
    /// S2.4.4.4 Health &amp; Early-warning, so the Health tab can be checked against
    /// the spec line by line: front 278.24 g left (92.75 %, 4637 more products), rear
    /// 2.69 g (8.97 %, 449 more), nozzles 156 h and 12 h, pot life 6 h and 0.4 h.
    /// </para>
    /// <para>
    /// The two settings objects are real: they are saved to
    /// <c>Data\Settings\GlueSettings.json</c> and survive a restart. Nothing sends them
    /// to the PLC yet.
    /// </para>
    /// </remarks>
    public sealed class SampleDispenserDataService : IDispenserDataService, ISampleDataSource
    {
        private static readonly string[] Gantries = { "Front", "Rear" };

        private readonly string _settingsPath;
        private readonly object _gate = new object();
        private SettingsFile _settings;

        public SampleDispenserDataService(string dataDirectory)
        {
            _settingsPath = Path.Combine(dataDirectory ?? string.Empty, "Settings", "GlueSettings.json");
        }

        public Task<IReadOnlyList<GlueWeighingRecord>> GetGlueWeighingsAsync(DateTime start, DateTime end)
        {
            return Task.Run<IReadOnlyList<GlueWeighingRecord>>(() =>
            {
                var records = new List<GlueWeighingRecord>();
                DateTime now = DateTime.Now;
                if (end > now)
                    end = now;

                // The scale runs every two hours, five minutes past the hour.
                DateTime t = new DateTime(start.Year, start.Month, start.Day, start.Hour - start.Hour % 2, 5, 0);
                for (; t <= end; t = t.AddHours(2))
                {
                    if (t < start)
                        continue;

                    long key = (long)(t - new DateTime(2026, 1, 1)).TotalHours;

                    for (int g = 0; g < Gantries.Length; g++)
                    {
                        for (int path = 1; path <= 2; path++)
                        {
                            double nominal = Nominal(path);
                            double tolerance = path == 1 ? 1.0 : 0.8;

                            // Mostly close to nominal, a slow drift through the day, and
                            // now and then one well out.
                            double drift = Math.Sin(key / 6.0 + g) * 0.12;
                            double noise = Normal(key * 10 + g * 3 + path) * 0.16;
                            bool outlier = SampleProductionRepository.Noise(key * 10 + g * 3 + path, 21) < 0.03;
                            double weight = nominal + drift + noise + (outlier ? tolerance * 1.25 : 0);

                            records.Add(new GlueWeighingRecord
                            {
                                Time = t.AddSeconds(g * 40 + path * 12),
                                Shift = t.Hour >= 8 && t.Hour < 20 ? "DS" : "NS",
                                Gantry = Gantries[g],
                                Path = path,
                                DispenseSpeed = RunSpeed(path),
                                WeightMg = Math.Round(weight, 3),
                                NominalMg = nominal,
                                LowerLimitMg = nominal - tolerance,
                                UpperLimitMg = nominal + tolerance
                            });
                        }
                    }
                }

                return records;
            });
        }

        public Task<IReadOnlyList<GlueRegression>> GetGlueRegressionsAsync()
        {
            // Calibrated every 24 hours (the default weighing settings), at 08:30.
            DateTime calibrated = DateTime.Today.AddHours(8).AddMinutes(30);
            if (calibrated > DateTime.Now)
                calibrated = calibrated.AddDays(-1);

            var result = new List<GlueRegression>();
            for (int g = 0; g < Gantries.Length; g++)
            {
                for (int path = 1; path <= 2; path++)
                {
                    // S2.4.4.5's example slope, -0.0538 mg per mm/s, a little different
                    // on each nozzle, through the path's nominal weight at its run speed.
                    double a = -0.0538 * (1 + 0.06 * g - 0.04 * (path - 1));
                    double b = Nominal(path) - a * RunSpeed(path);
                    result.Add(Regression(Gantries[g], path, a, b, calibrated.AddMinutes(g * 26 + path * 7), g * 2 + path));
                }
            }

            return Task.FromResult<IReadOnlyList<GlueRegression>>(result);
        }

        /// <summary>Nominal weight of a glue path, mg - path 1 is S2.4.4.5's 3.2 mg (2.2 to 4.2).</summary>
        private static double Nominal(int path)
        {
            return path == 1 ? 3.2 : 2.6;
        }

        /// <summary>The dispensing speed a glue path runs at, mm/s.</summary>
        private static double RunSpeed(int path)
        {
            return path == 1 ? 5 : 8;
        }

        public Task<IReadOnlyList<GlueCartridgeStatus>> GetGlueCartridgesAsync()
        {
            // S2.4.4.4's example, front and rear.
            IReadOnlyList<GlueCartridgeStatus> result = new[]
            {
                new GlueCartridgeStatus
                {
                    Gantry = "Front",
                    GlueSn = "0009711F0N+MT44837334+266927+250602+0030G",
                    ChangedAt = DateTime.Now.AddHours(-3.2),
                    ProducedQty = 346,
                    LongPurgeCycles = 9,
                    ShortPurgeCycles = 70,
                    WeighingCycles = 2,
                    RemainingPotLifeHours = 6
                },
                new GlueCartridgeStatus
                {
                    Gantry = "Rear",
                    GlueSn = "0009711F0N+MT44837335+266927+250602+0030G",
                    ChangedAt = DateTime.Now.AddHours(-21.5),
                    ProducedQty = 4328,
                    LongPurgeCycles = 67,
                    ShortPurgeCycles = 426,
                    WeighingCycles = 14,
                    RemainingPotLifeHours = 0.4
                }
            };

            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<NozzleHealthRecord>> GetNozzleHealthAsync()
        {
            IReadOnlyList<NozzleHealthRecord> result = new[]
            {
                new NozzleHealthRecord { Gantry = "Front", ValveSn = "EFD-PICO-000001_230218", NozzleId = "FX001", Value = 156, LowerLimit = 0, UpperLimit = 168 },
                new NozzleHealthRecord { Gantry = "Rear", ValveSn = "EFD-PICO-000002_230218", NozzleId = "FX002", Value = 12, LowerLimit = 0, UpperLimit = 168 }
            };

            return Task.FromResult(result);
        }

        public Task<GlueWeighingSettings> GetWeighingSettingsAsync()
        {
            return Task.FromResult(Settings().Weighing.Copy());
        }

        public Task SaveWeighingSettingsAsync(GlueWeighingSettings settings)
        {
            return Task.Run(() =>
            {
                lock (_gate)
                {
                    SettingsFile file = Settings();
                    file.Weighing = settings.Copy();
                    Write(file);
                }
            });
        }

        public Task<GlueControlSettings> GetGlueControlSettingsAsync()
        {
            return Task.FromResult(Settings().GlueControl.Copy());
        }

        public Task SaveGlueControlSettingsAsync(GlueControlSettings settings)
        {
            return Task.Run(() =>
            {
                lock (_gate)
                {
                    SettingsFile file = Settings();
                    file.GlueControl = settings.Copy();
                    Write(file);
                }
            });
        }

        /// <summary>
        /// Ten calibration points at 1 to 10 mm/s around the line a x + b, and the
        /// least-squares fit through them - S2.4.4.5's "10x points to form Regression
        /// formula", so the a, b and R² shown are the ones the points give.
        /// </summary>
        private static GlueRegression Regression(string gantry, int path, double a, double b, DateTime calibrated, int salt)
        {
            var points = new List<GlueCalibrationPoint>();
            for (int x = 1; x <= 10; x++)
            {
                double y = a * x + b + Normal(x * 7 + salt * 101) * 0.035;
                points.Add(new GlueCalibrationPoint(x, Math.Round(y, 4)));
            }

            double meanX = points.Average(p => p.X);
            double meanY = points.Average(p => p.Y);
            double sxy = points.Sum(p => (p.X - meanX) * (p.Y - meanY));
            double sxx = points.Sum(p => (p.X - meanX) * (p.X - meanX));
            double fitA = sxy / sxx;
            double fitB = meanY - fitA * meanX;

            double residual = points.Sum(p => Math.Pow(p.Y - (fitA * p.X + fitB), 2));
            double total = points.Sum(p => Math.Pow(p.Y - meanY, 2));

            return new GlueRegression
            {
                Gantry = gantry,
                Path = path,
                W = fitA,
                B = fitB,
                R2 = total > 0 ? 1 - residual / total : 1,
                CalibratedAt = calibrated,
                InputName = "Dispensing speed (mm/s)",
                Points = points
            };
        }

        /// <summary>A repeatable standard-normal number for a key (Box-Muller).</summary>
        private static double Normal(long key)
        {
            double u1 = Math.Max(1e-9, SampleProductionRepository.Noise(key, 31));
            double u2 = SampleProductionRepository.Noise(key, 32);
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }


        // ==================================================================
        // Settings file
        // ==================================================================

        private SettingsFile Settings()
        {
            lock (_gate)
            {
                if (_settings != null)
                    return _settings;

                try
                {
                    if (File.Exists(_settingsPath))
                        _settings = JsonConvert.DeserializeObject<SettingsFile>(File.ReadAllText(_settingsPath));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("GlueSettings.json could not be read: " + ex.Message);
                }

                if (_settings == null)
                    _settings = new SettingsFile();

                if (_settings.Weighing == null)
                    _settings.Weighing = DefaultWeighing();

                // A file saved before calibration was set in hours has no value for it.
                if (_settings.Weighing.CalibrationIntervalHours <= 0)
                    _settings.Weighing.CalibrationIntervalHours = DefaultWeighing().CalibrationIntervalHours;

                if (_settings.GlueControl == null || _settings.GlueControl.Gantries == null || _settings.GlueControl.Gantries.Count == 0)
                    _settings.GlueControl = DefaultGlueControl();

                return _settings;
            }
        }

        /// <summary>Written through a temp file, so a power cut mid-save cannot leave a half-written file.</summary>
        private void Write(SettingsFile file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));

            string temp = _settingsPath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(file, Formatting.Indented), new UTF8Encoding(false));

            if (File.Exists(_settingsPath))
                File.Delete(_settingsPath);

            File.Move(temp, _settingsPath);
            _settings = file;
        }

        /// <summary>S2.4.4.5's settings: weigh every 2 hours, 3 times, calibrate every 24 hours.</summary>
        private static GlueWeighingSettings DefaultWeighing()
        {
            return new GlueWeighingSettings
            {
                ByTime = true,
                IntervalMinutes = 120,
                ByUnitCount = false,
                IntervalUnits = 500,
                AtShiftStart = false,
                AtShiftEnd = false,
                ShotsPerWeighing = 3,
                CalibrationIntervalHours = 24
            };
        }

        /// <summary>The dosages of S2.4.4.4's example.</summary>
        private static GlueControlSettings DefaultGlueControl()
        {
            return new GlueControlSettings
            {
                Gantries = new List<GantryGlueDosage>
                {
                    new GantryGlueDosage { Gantry = "Front", CartridgeWeightG = 300, GluePerProductG = 0.06, LongPurgeDoseG = 0.02, ShortPurgeDoseG = 0.01 },
                    new GantryGlueDosage { Gantry = "Rear", CartridgeWeightG = 30, GluePerProductG = 0.006, LongPurgeDoseG = 0.006, ShortPurgeDoseG = 0.002 }
                }
            };
        }

        private sealed class SettingsFile
        {
            public GlueWeighingSettings Weighing { get; set; }

            public GlueControlSettings GlueControl { get; set; }
        }
    }
}
