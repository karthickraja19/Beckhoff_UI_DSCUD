using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// The glue side of a dispenser, for the Data page's Glue and Health tabs
    /// (S2.4.4.4 Health &amp; Early-warning, S2.4.4.5 Glue Weight).
    /// </summary>
    /// <remarks>
    /// Weighings and the regression come from the machine (the weighing scale and the
    /// calibration); pot life and nozzle health come from MES. The two settings objects
    /// are owned by the HMI and edited by OSS.
    /// </remarks>
    public interface IDispenserDataService
    {
        Task<IReadOnlyList<GlueWeighingRecord>> GetGlueWeighingsAsync(DateTime start, DateTime end);

        /// <summary>The regression from each gantry's last calibration.</summary>
        Task<IReadOnlyList<GlueRegression>> GetGlueRegressionsAsync();

        /// <summary>The glue in each gantry's cartridge since the last glue change.</summary>
        Task<IReadOnlyList<GlueCartridgeStatus>> GetGlueCartridgesAsync();

        Task<IReadOnlyList<NozzleHealthRecord>> GetNozzleHealthAsync();

        Task<GlueWeighingSettings> GetWeighingSettingsAsync();

        Task SaveWeighingSettingsAsync(GlueWeighingSettings settings);

        Task<GlueControlSettings> GetGlueControlSettingsAsync();

        Task SaveGlueControlSettingsAsync(GlueControlSettings settings);
    }
}
