# Odyssey Master (Beckhoff) Agent Rules

WPF HMI for a Beckhoff / TwinCAT machine. The PLC owns all machine logic - sequences,
motion, interlocks and device alarms. This application is presentation, recipes and
persistence only.

## Projects

- `Nova.Wpf.App` (.NET Framework 4.7.2, C# 7.3): startup/DI, views and services. `Core\` holds service implementations, simulators and config models.
- `Nova.Wpf.UI` (.NET Framework 4.7.2, C# 7.3): reusable controls, themes, view resources.
- `Nova.ViewModels` (.NET Standard 2.0, C# 8.0): platform-free ViewModels. Depends on `Nova.Abstractions` only.
- `Nova.Abstractions` (.NET Standard 2.0): interfaces/contracts only.
- `Master.Repository` (.NET Framework 4.7.2): SQL Server / CSV data access for `TEAL_Master`.
- `Nova.Alarm` (.NET Framework 4.7.2, C# 7.3): subscribes to the PLC's alarm list (`GVL_Alarm.fbAlarmManager.aAlarms`) with ADS notifications (no polling), works out when each alarm starts and ends, and saves them to the `[Alarms]` table.
- `Nova.Hive` (.NET Framework 4.7.2, C# 7.3): HIVE reporting (machine state 1/2/5 and errordata) and the alarm pop-up. Depends on `Nova.Alarm`.

## Architecture decisions

- ViewModels never reference TwinCAT or `Teal.Library.*` types. PLC access goes through adapters in `Nova.Wpf.App` that implement `Nova.Abstractions` contracts over the Beckhoff `Teal.Library.*` library.
- No C# machine sequences, interlocks or monitors - those live in the PLC.
- Recipes are owned by the HMI and written to the PLC.
- Alarms are raised by the PLC into its central alarm list (`FB_AlarmManager`) and cleared by RESET. The HMI (`Nova.Alarm`) compares that list with the alarms it is tracking to get each alarm's start and end time.
- The PLC reports what state the machine is in (`E_MachineState`); the HMI (`Nova.Hive`) turns that, plus the alarms, into the HIVE state 1/2/5. Only an operator with a badge can leave downtime (DFM slide 8) - resetting the alarm or pressing Start does not change it.
- `HardwareMode=Simulated` runs the full UI with no PLC.

## Language/runtime constraints

- Keep `Nova.Wpf.App` and `Nova.Wpf.UI` C# 7.3 compatible.
- Keep `Nova.ViewModels` and `Nova.Abstractions` .NET Standard 2.0 compatible.
