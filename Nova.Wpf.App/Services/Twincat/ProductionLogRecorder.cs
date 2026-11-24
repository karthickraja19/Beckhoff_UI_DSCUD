using System;
using Nova.Abstractions.Data.Repositories;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Saves each row of the PLC's production custom log (<see cref="ProductionLogModel"/>) as one
    /// [ProductionData] row - the rows Home and the Data page read. It is the
    /// <c>rowHandler</c> of <see cref="PlcCustomLogBridge"/>; the CSV row is written as before.
    /// </summary>
    /// <remarks>
    /// The log row carries no time: the row is saved as finished when the HMI takes it, which
    /// the bridge does within milliseconds of the PLC logging it (or on start-up, for a row
    /// logged while the HMI was away), and started one cycle time earlier - the same as
    /// <see cref="PlcUnitDoneWatcher"/> does. Shift and date come from the shift of that time.
    /// </remarks>
    public sealed class ProductionLogRecorder
    {
        public const string LogKey = "Production";

        private readonly IProductionRecorder _recorder;

        public ProductionLogRecorder(IProductionRecorder recorder)
        {
            _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        }

        /// <summary>Matches <see cref="PlcCustomLogBridge"/>'s row handler: other logs are ignored.</summary>
        public void OnRow(string key, object model)
        {
            if (!string.Equals(key, LogKey, StringComparison.OrdinalIgnoreCase) || !(model is ProductionLogModel row))
                return;

            string serial = (row.ProductCode ?? string.Empty).Trim();
            double cycle = Math.Max(0, row.CycleTime);

            _recorder.RecordAsync(new ProductionUnitRecord
            {
                ProductCode = serial,
                IsPass = row.ProductionStatus,
                CycleTimeSeconds = cycle,
                ProductionTime = DateTime.Now.AddSeconds(-cycle),
                Iteration = row.Iteration > 0 ? row.Iteration : 1,
                TossingCount = row.Tossing ? 1 : 0,
            }).GetAwaiter().GetResult();
        }
    }
}
