using System.Collections.Generic;
using Nova.Abstractions.Data.Models;

namespace Nova.Abstractions.Services
{
    public interface IMachineSettingsService
    {
        event System.EventHandler SettingsChanged;

        // The Axis Configuration tab and the Manual axis panel: Data\Servos.json, the servos' own file.
        IReadOnlyList<AxisParameterRecord> LoadAxisParameters();
        DynamicTableDocument LoadAxisParametersDynamic();
        RecipeSaveResult SaveAxisParametersDynamic(DynamicTableDocument document);

        IReadOnlyList<TcpConnectionRecord> LoadTcpConnections();
        DynamicTableDocument LoadTcpConnectionsDynamic(string category);
        void SaveTcpConnections(string category, IReadOnlyList<TcpConnectionRecord> rows);
        RecipeSaveResult SaveTcpConnectionsDynamic(string category, DynamicTableDocument document);

        IReadOnlyList<SerialPortRecord> LoadSerialPorts();
        DynamicTableDocument LoadSerialPortsDynamic();
        void SaveSerialPorts(IReadOnlyList<SerialPortRecord> rows);
        RecipeSaveResult SaveSerialPortsDynamic(DynamicTableDocument document);

        IReadOnlyList<VisionParameterRecord> LoadVisionSettings();
        DynamicTableDocument LoadVisionSettingsDynamic();
        void SaveVisionSettings(IReadOnlyList<VisionParameterRecord> rows);
        RecipeSaveResult SaveVisionSettingsDynamic(DynamicTableDocument document);

        IReadOnlyList<AeLimitRecord> LoadAeLimits(AeLimitKind kind);
        DynamicTableDocument LoadAeLimitsDynamic(AeLimitKind kind);
        void SaveAeLimits(AeLimitKind kind, IReadOnlyList<AeLimitRecord> rows);
        RecipeSaveResult SaveAeLimitsDynamic(AeLimitKind kind, DynamicTableDocument document);
    }
}
