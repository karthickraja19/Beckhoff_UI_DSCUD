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
    /// Glue data for <c>HardwareMode=Simulated</c>: weighings, calibration regression,
    /// cartridge counts, pot life and nozzle health for a D-SCUD's front and rear
    /// gantries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cartridge, pot-life and nozzle figures are the worked example of
    /// S2.4.4.4 Health &amp; Early-warning, so the Health tab can be checked against
    /// the spec line by line: front 278.24 g left (92.75 %), rear 2.69 g (8.98 % - the
    /// spec rounds the grams to 2.69 before dividing and prints 8.97 %).
    /// </para>
    /// <para>
    /// The two settings objects are real: they are saved to
    /// <c>Data\Settings\GlueSettings.json</c> and survive a restart. Nothing sends them
    /// to the PLC yet.
    /// </para>
    /// </remarks>
    public sealed class SimulatedDispenserDataService : IDispenserDataService, ISampleDataSource
    {
        private static readonly string[] Gantries = { "Front", "Rear" };

        private readonly string _settingsPath;
        private readonly object _gate = new object();
        private SettingsFile _settings;

        public SimulatedDispenserDataService(string dataDirectory)
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
                            double nominal = path == 1 ? 3.2 : 2.6;
                            double tolerance = path == 1 ? 1.0 : 0.8;

                            // Mostly close to nominal, a slow drift through the day, and
                            // now and then one well out.
                            double drift = Math.Sin(key / 6.0 + g) * 0.12;
                            double noise = Normal(key * 10 + g * 3 + path) * 0.16;
                            bool outlier = SimulatedProductionRepository.Noise(key * 10 + g * 3 + path, 21) < 0.03;
                            double weight = nominal + drift + noise + (outlier ? tolerance * 1.25 : 0);

                            records.Add(new GlueWeighingRecord
                            {
                                Time = t.AddSeconds(g * 40 + path * 12),
                                Shift = t.Hour >= 8 && t.Hour < 20 ? "DS" : "NS",
                                Gantry = Gantries[g],
                                Path = path,
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
            DateTime calibrated = DateTime.Today.AddDays(-2).AddHours(9).AddMinutes(12);

            IReadOnlyList<GlueRegression> result = new[]
            {
                Regression("Front", 0.00640, 0.052, calibrated, 1),
                Regression("Rear", 0.00628, 0.061, calibrated.AddMinutes(26), 2)
            };

            return Task.FromResult(result);
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

        private static GlueRegression Regression(string gantry, double w, double b, DateTime calibrated, int salt)
        {
            var points = new List<GlueCalibrationPoint>();
            double sumSquares = 0, totalSquares = 0;
            double[] xs = { 100, 200, 300, 400, 500 };
            double meanY = xs.Average(x => w * x + b);

            foreach (double x in xs)
            {
                double y = w * x + b + Normal((long)x * 7 + salt) * 0.018;
                points.Add(new GlueCalibrationPoint(x, Math.Round(y, 4)));
                sumSquares += Math.Pow(y - (w * x + b), 2);
                totalSquares += Math.Pow(y - meanY, 2);
            }

            return new GlueRegression
            {
                Gantry = gantry,
                W = w,
                B = b,
                R2 = totalSquares > 0 ? 1 - sumSquares / totalSquares : 1,
                CalibratedAt = calibrated,
                InputName = "Dispense time (ms)",
                Points = points
            };
        }

        /// <summary>A repeatable standard-normal number for a key (Box-Muller).</summary>
        private static double Normal(long key)
        {
            double u1 = Math.Max(1e-9, SimulatedProductionRepository.Noise(key, 31));
            double u2 = SimulatedProductionRepository.Noise(key, 32);
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

        private static GlueWeighingSettings DefaultWeighing()
        {
            return new GlueWeighingSettings
            {
                ByTime = true,
                IntervalMinutes = 120,
                ByUnitCount = false,
                IntervalUnits = 500,
                AtShiftStart = true,
                AtShiftEnd = false,
                ShotsPerWeighing = 3,
                CalibrationIntervalDays = 7
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
