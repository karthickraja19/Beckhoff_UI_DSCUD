using System;
using System.Collections.Generic;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services.Twincat
{
    /// <summary>
    /// Every custom log of the HMI, one per kind of data. Anywhere in the HMI:
    /// <code>CustomLogs.Production.Log(model);</code>
    /// and the PLC's rows reach the same object through <see cref="PlcCustomLogBridge"/>.
    ///
    /// A NEW KIND OF DATA (calibration, ...) takes four small steps:
    /// 1. a model struct next to <see cref="ProductionLogModel"/>, the same as the PLC struct;
    /// 2. a <c>CustomLog&lt;T&gt;</c> field here and a line in <see cref="All"/>;
    /// 3. a target and a rule in NLog.config (see Docs\TwinCAT_Integration.md, "Custom logs");
    /// 4. for rows from the PLC: an <c>FB_CustomLog</c> instance in MAIN and its symbol
    ///    under "CustomLog" in Data\PlcSymbols.json, with the key used in <see cref="All"/>.
    /// </summary>
    public static class CustomLogs
    {
        /// <summary>The production log: one row per finished unit. NLog.config: logger "ProductionLogger", target "ProductionTarget".</summary>
        public static readonly CustomLog<ProductionLogModel> Production =
            new CustomLog<ProductionLogModel>("ProductionLogger", "ProductionTarget");

        /// <summary>The Static / Dynamic log: one row per vision command. NLog.config: logger "StaticDynamicLogger", target "StaticDynamicTarget".</summary>
        public static readonly CustomLog<StaticDynamicLogModel> StaticDynamic =
            new CustomLog<StaticDynamicLogModel>("StaticDynamicLogger", "StaticDynamicTarget");

        /// <summary>The logs by the key PlcSymbols.json "CustomLog.Logs" uses for the PLC block that feeds them.</summary>
        public static IReadOnlyDictionary<string, ICustomLog> All { get; } =
            new Dictionary<string, ICustomLog>(StringComparer.OrdinalIgnoreCase)
            {
                { "Production", Production },
                { "StaticDynamic", StaticDynamic },
            };
    }
}
