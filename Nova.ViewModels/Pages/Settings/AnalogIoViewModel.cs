using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Hardware;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Settings
{
    /// <summary>One analog channel row: raw counts, scaled value + bar, status badge, unit.</summary>
    public partial class AnalogChannelRow : ObservableObject
    {
        public string ChannelId { get; }
        public string Name { get; }
        public string Unit { get; }

        [ObservableProperty]
        private string _rawValue;

        [ObservableProperty]
        private string _scaledValue;

        [ObservableProperty]
        private string _scaledUnit;

        /// <summary>Scaled value as a 0-100 fraction of range, drives the bar.</summary>
        [ObservableProperty]
        private double _percent;

        /// <summary>"HEALTHY" / "WARNING" / "ACTIVE" — drives the badge colour.</summary>
        [ObservableProperty]
        private string _status;

        public AnalogChannelRow(string channelId, string name, string rawValue,
            string scaledValue, string scaledUnit, double percent, string status, string unit)
        {
            ChannelId = channelId;
            Name = name;
            _rawValue = rawValue;
            _scaledValue = scaledValue;
            _scaledUnit = scaledUnit;
            _percent = percent;
            _status = status;
            Unit = unit;
        }
    }

    /// <summary>
    /// I/O Diagnostics &gt; Analog I/O — live analog channel table. Channels mirror the
    /// loadcell monitor feed and update in real time.
    /// </summary>
    public class AnalogIoViewModel : ObservableObject
    {
       private readonly ILoadCellMonitorService _loadCellMonitor;
       private readonly Dictionary<string, AnalogChannelRow> _channelsByAlias =
           new Dictionary<string, AnalogChannelRow>(StringComparer.OrdinalIgnoreCase);

       private static readonly ChannelDefinition[] Definitions =
       {
           new ChannelDefinition("AI_001", "Robot_LoadCell", "Kg", 5.0,
               "Robot_LoadCell", "AI0_0_Robot_Load_Cell", "Robot_Load_Cell", "LoadCell1", "LoadCell_1"),
           new ChannelDefinition("AI_002", "Pressing_LoadCell", "Kg", 10.0,
               "Pressing_LoadCell", "AI0_1_Roller_Module_Load_Cell_1", "Roller_Module_Load_Cell_1",
               "AI0_2_Roller_Module_Load_Cell_2", "Roller_Module_Load_Cell_2", "LoadCell2", "LoadCell_2"),
       };

       public string Title => "Analog I/O";

       public ObservableCollection<AnalogChannelRow> Channels { get; }
       public bool HasChannels => Channels.Count > 0;

       public AnalogIoViewModel(ILoadCellMonitorService loadCellMonitor)
       {
           _loadCellMonitor = loadCellMonitor ?? throw new ArgumentNullException(nameof(loadCellMonitor));
           Channels = new ObservableCollection<AnalogChannelRow>();

           var initialSnapshots = _loadCellMonitor.ReadAll();
           foreach (var snapshot in initialSnapshots)
           {
               EnsureRegisteredChannel(snapshot.ComponentId);
           }

           ApplyLoadCellSnapshots(initialSnapshots);
           _loadCellMonitor.LoadCellStatusUpdated += OnLoadCellStatusUpdated;
           UpdateAvailability();
       }

       private void OnLoadCellStatusUpdated(object sender, LoadCellStatusSnapshot snapshot)
       {
           if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ComponentId))
           {
               return;
           }

           if (_channelsByAlias.TryGetValue(snapshot.ComponentId, out var channel))
           {
               ApplySnapshot(channel, snapshot.Value);
           }
       }

       private void ApplyLoadCellSnapshots(IReadOnlyList<LoadCellStatusSnapshot> snapshots)
       {
           if (snapshots == null)
           {
               return;
           }

           foreach (var snapshot in snapshots)
           {
               if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ComponentId))
               {
                   continue;
               }

               if (_channelsByAlias.TryGetValue(snapshot.ComponentId, out var channel))
               {
                   ApplySnapshot(channel, snapshot.Value);
               }
           }
       }

       private void EnsureRegisteredChannel(string alias)
       {
           var definition = ResolveDefinition(alias);
           if (definition == null)
           {
               return;
           }

           var existing = FindByChannelId(definition.ChannelId);
           if (existing == null)
           {
               existing = new AnalogChannelRow(
                   definition.ChannelId,
                   definition.Name,
                   "0.000",
                   "0.000",
                   definition.Unit,
                   0,
                   "HEALTHY",
                   definition.Unit);
               Channels.Add(existing);
           }

           foreach (var itemAlias in definition.Aliases)
           {
               _channelsByAlias[itemAlias] = existing;
           }
       }

       private AnalogChannelRow FindByChannelId(string channelId)
       {
           foreach (var channel in Channels)
           {
               if (channel.ChannelId.Equals(channelId, StringComparison.OrdinalIgnoreCase))
               {
                   return channel;
               }
           }

           return null;
       }

       private static ChannelDefinition ResolveDefinition(string alias)
       {
           foreach (var definition in Definitions)
           {
               foreach (var candidate in definition.Aliases)
               {
                   if (candidate.Equals(alias, StringComparison.OrdinalIgnoreCase))
                   {
                       return definition;
                   }
               }
           }

           return null;
       }

       private static void ApplySnapshot(AnalogChannelRow channel, double value)
       {
           double maxRange = ResolveMaxRange(channel.ChannelId);
           double clampedPercent = maxRange <= 0 ? 0 : Math.Max(0, Math.Min(100, value / maxRange * 100.0));

           channel.RawValue = value.ToString("0.000");
           channel.ScaledValue = value.ToString("0.000");
           channel.ScaledUnit = channel.Unit;
           channel.Percent = clampedPercent;
           channel.Status = ResolveStatus(value, maxRange);
       }

       private static double ResolveMaxRange(string channelId)
       {
           foreach (var definition in Definitions)
           {
               if (definition.ChannelId.Equals(channelId, StringComparison.OrdinalIgnoreCase))
               {
                   return definition.MaxRange;
               }
           }

           return 1.0;
       }

       private static string ResolveStatus(double value, double maxRange)
       {
           if (value <= 0)
           {
               return "HEALTHY";
           }

           if (value >= maxRange * 0.8)
           {
               return "WARNING";
           }

           return "ACTIVE";
       }

       private void UpdateAvailability()
       {
           OnPropertyChanged(nameof(HasChannels));
       }

       private sealed class ChannelDefinition
       {
           public string ChannelId { get; }
           public string Name { get; }
           public string Unit { get; }
           public double MaxRange { get; }
           public IReadOnlyList<string> Aliases { get; }

           public ChannelDefinition(string channelId, string name, string unit, double maxRange, params string[] aliases)
           {
               ChannelId = channelId;
               Name = name;
               Unit = unit;
               MaxRange = maxRange;
               Aliases = aliases ?? Array.Empty<string>();
           }
       }
    }
}
