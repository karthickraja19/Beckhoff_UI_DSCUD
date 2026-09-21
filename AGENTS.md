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

## Architecture decisions

- ViewModels never reference TwinCAT or `Teal.Library.*` types. PLC access goes through adapters in `Nova.Wpf.App` that implement `Nova.Abstractions` contracts over the Beckhoff `Teal.Library.*` library.
- No C# machine sequences, interlocks or monitors - those live in the PLC.
- Recipes are owned by the HMI and written to the PLC.
- Alarms are raised by TwinCAT EventLogger; the HMI displays and acknowledges them.
- `HardwareMode=Simulated` runs the full UI with no PLC.

## Language/runtime constraints

- Keep `Nova.Wpf.App` and `Nova.Wpf.UI` C# 7.3 compatible.
- Keep `Nova.ViewModels` and `Nova.Abstractions` .NET Standard 2.0 compatible.
