# TwinCAT integration - Odyssey Master (Beckhoff) HMI

How the WPF HMI talks to the D-SCUD TwinCAT project, where every PLC variable name
lives, and what the PLC still has to provide.

Basis: `TwincatStandardDSCUD-master\BeckhoffSTD\BeckhoffTwincatSDBlocks`, read on
10 Nov 2026. Nothing here was run against a PLC: it is from reading both code bases.

## 1. Who does what

The PLC owns the machine: sequences, motion, interlocks, alarms, modes. The HMI is
presentation, recipes and persistence. The HMI never decides whether something may
move; it asks, and shows what the PLC reports.

Three ways of talking, all over one ADS connection (`Data\CardsConfiguration.json`):

| Pattern | Used for | How |
|---|---|---|
| Pulse | Start, Pause, Stop, Reset | BOOL set TRUE, then FALSE after `Commands.PulseMilliseconds`. The PLC acts on the rising edge. |
| Level | Manual mode, bypasses, maintenance stations | BOOL the HMI holds, read back after writing. |
| Set and wait | Storing the calibration teach tables | HMI sets `GVL_CalTeach.bSave`; the PLC clears it and answers with `bSaveDone` or `bSaveError`. |

Status comes back as ADS notifications (`PlcSubscriptions`), never by polling. The one
exception is the digital I/O image, read every 200 ms only while a screen shows it.

## 2. The one place for PLC names

**`Nova.Wpf.App\Data\PlcSymbols.json`** holds every TwinCAT variable name the HMI uses.
Rename or implement a PLC variable and only this file changes.

- An entry left `""` means "the PLC has no variable for this yet". The function it
  belongs to shows itself as not available, with the reason. Nothing is faked.
- At startup, once the PLC is live, `PlcSymbolCheck` looks every name up and lists the
  ones the PLC does not have (a warning dialog and the Error log).
- The file is read by `Services\Twincat\Mapping\PlcSymbolMap.cs`; `App.xaml.cs` hands
  each service only the section it needs. View models never see a PLC name.

| Section | Direction | What it drives | Service |
|---|---|---|---|
| `Commands` | HMI to PLC | Start / Pause / Stop / Reset, manual mode, home command | `TwincatMachineOrchestrator`, `TwincatHomingService` |
| `Status` | PLC to HMI | Machine state, homing done, unit entered, unit done | orchestrator, `Nova.Hive`, `PlcUnitDoneWatcher` |
| `RunMode` | both | Config > Device Settings mode | `TwincatMachineModeLink` |
| `Bypass` | both | Bypass switches, maintenance stations | `TwincatMachineModeLink` |
| `Alarms` | PLC to HMI | Alarm list | `Nova.Alarm\PlcAlarmReader` |
| `SequenceLog` | PLC to HMI | Running Log | `PlcSequenceLog` (`ST_SeqLog`) |
| `CustomLog` | PLC to HMI | One CSV row per struct the PLC logs (production, calibration, ...) | `PlcCustomLogBridge` (`FB_CustomLog`) |
| `Recipe` | HMI to PLC | Gantry and CNC positions | `TwincatRecipeService` |
| `Calibration` | both | Config > Calibration: teach tables and the calibration manager | `TwincatCalibrationService` |
| `Cnc` | both | Recipe > CNC jog, teach, positions | `TwincatCncAxisAdapter`, `TwincatRecipeService` |
| `Io` | both | I/O Diagnostics, sensor dashboard | `TwincatDeviceIOService` |
| `Process` | HMI to PLC | Glue alert level, buzzer mute, picture-check stop | `GlueWarningMonitor`, `PictureQuantityCheck` |
| `Devices` | - | PLC type names of the device status structs | `HmiDisplays` |
| `UnitRecord` | PLC to HMI | One [ProductionData] row per finished unit, and its HIVE machine_data payload | `PlcUnitDoneWatcher`, `ProductionRecorder`, `HiveUnitDataBuilder` |
| `Coordinates` | PLC to HMI | Data > Axis, recorded per finished unit | `PlcUnitDoneWatcher`, `RecordedCoordinateDataSource` |
| `Glue` | both | Data > Glue and Health, Home glue panel, glue warnings | `TwincatDispenserDataService` |
| `ThirdPartyDevices` | both | Manual > Dispenser, Weighing, Vibration | `TwincatPlcDeviceService` |
| `Parameters` | PLC to HMI | Home > Parameter Dashboard | `TwincatParameterValueSource` |
| `SensorDisable` | HMI to PLC | Home > Sensor Dashboard disable switch | `TwincatSensorService` |

Not in that file, because the prebuilt Beckhoff library owns them:

- `Data\CardsConfiguration.json`: AMS Net Id, port, and the mapping block of each
  device kind (`MAIN.servocall`, `MAIN.cylindercall`, ...).
- Member names inside those blocks (`hmicmd.home`, `servoItf[n].HMI_CMD`, ...) are
  compiled into `References\Beckhoff\Teal.Library.*.dll`.

## 3. What changed in the HMI against this PLC

| Area | Before | Now |
|---|---|---|
| Start / Pause / Reset | `MAIN.modecall.bStartPb` etc. MAIN overwrites those inputs every cycle, so the writes were lost. | `MAIN.bStartPb`, `MAIN.bPausePb`, `MAIN.bResetPb` |
| Stop | Pulsed the Pause bit | `MAIN.bStopPb` |
| Homing done | Not mapped | `MAIN.modecall.fbMode.bHomed` |
| Sequence log | Struct did not match; no symbols | `ST_SeqLog`, `MAIN.seqlog.aLog[1..10]` |
| Digital I/O | Library names (`GVL.Inputs`), which this PLC does not declare | Through the library card: `GVL_Inputs.Inputs` / `GVL_Outputs.Outputs` named in `CardsConfiguration.json` (`InputArray`, `OutputArray`, `IOFirstIndex` 1, 225 / 188 points) |
| Device Settings | Saved in HMI memory only | Written to `MAIN.runmode.eRequest`, door bypass and `BypassVariables.Maintenance_Station_n`, read back; the tab follows the PLC |
| Calibration | `MAIN.calibcall` command/status structs that do not exist; then the 9 point programs `NinePointCalibration_StationN` | The calibration manager `GVL_Calibration` (select, station, Start / Abort / Reset) and the teach tables `GVL_CalTeach`, written from `Data\Calibration\*.json` |
| CNC tab | Axes unmapped; `ST_CncRecipe` that does not exist | Jog through `GVL_Channel_Interface.CNCData.Ch[n]`; positions into `MAIN.recipe.Recipe[4/5]` |
| HIVE unit counting | Never called | `PlcUnitDoneWatcher` on `Status.UnitDoneCount` (blank until the PLC has it) |
| HIVE unit data (machine_data) | Not sent | One payload per finished unit: `HiveUnitDataBuilder` reads `Status.UnitDoneSerial`, `UnitRecord` and every `Parameters` entry while the PLC holds them; `HiveService.SendUnit` posts it (multipart, 2 s x 3; no 200/201 = downtime `X03IPOO-01`) |
| Production history | Nothing wrote [ProductionData] | One row per finished unit from `UnitRecord`, on `Status.UnitDoneCount` |
| Data > Axis | Sample coordinates only | Recorded per finished unit from `Coordinates` |
| Data > Glue / Health, glue warnings | Sample glue data only | `TwincatDispenserDataService`: weighings recorded on `Glue.Weighing.Count`, live cartridge and nozzle counters, regressions, weighing settings written to the PLC |
| Sample data | `SampleData=true` showed generated alarms, units, glue and coordinates | Removed. Every page shows what the machine recorded or says it has nothing |
| No database | CSV fallback that threw on every call | No repositories; the pages say there is no history |
| Retention | Keys read by nothing | Master.Scheduler (Quartz, as in V3): every CSV under `LogFilePath` past `RetentionDaysForCSV` every 2 min, the database rows past `RetentionDaysFor*` and the OEE rows before today at 23:30 |
| Shift status / OEE | Not calculated | Master.Scheduler's `ShiftStatusRefresherJob` every `StatusRefreshSeconds`: shift row, OEE, shift change (final status and `ProductionData.csv`) |
| Removed | Config > Calibration Camera/Robot sub-tabs, Manual > Dispenser tab (placeholders with no PLC side); Setting > Calibration with its routine pages, parameters and run history | - |

### Calibration (Config > Calibration)

The section is `Views\Pages\Calibration\CalibrationView.xaml` on `CalibrationViewModel`: a
manual-mode switch and one tab per calibration. Every tab is the table of that calibration's
taught positions (`CalibrationTableViewModel` over a `CalibrationTable`), with Edit / Cancel /
Save, a station box, the station's **Taught** flag, and the PLC's Start / Abort / Reset.

| Tab | File in `Data\Calibration` | Columns | PLC teach table (`Calibration.Teach`) | E_Calibration |
|---|---|---|---|---|
| 9-Point | `NinePoint.json` | one row per station: X, Y (the start position; the PLC makes the other 8 from the pitch) | `GVL_CalTeach.aNinePoint[station]` : `ST_TeachNinePoint` | 1 |
| 5-Axis | `FiveAxisTranslation.json`, `FiveAxisRotation.json` | stations 1 and 2: translation point1 .. point9 (X, Y), rotation point10 .. point34 (A, C, X, Y, Z) | not written to the PLC (HMI-owned) | - |
| Camera Needle | `CameraNeedle.json` | per station, points 1..9: glue X / Y and vision X / Y | `GVL_CalTeach.aCameraNeedle[station].aGlue[n]` / `.aVision[n]` : `ST_TeachXY` | 4 |
| Needle Module | `NeedleModule.json` | one row per station: X, Y, Z | `GVL_CalTeach.aNeedleModule[station]` : `ST_TeachNeedleModule` | 3 |

- The files are the HMI's. **Save** writes the file, then one whole station record per ADS
  write into the teach table (the `ST_Teach...` types are `pack_mode 1`: `bTaught`, then
  LREALs, no padding), then sets `GVL_CalTeach.bSave` and waits for `bSaveDone` or
  `bSaveError` - `FB_CalTeachStore` writes the PLC's persistent data.
- Before writing, the size of a record is compared with what the PLC declares; a mismatch
  writes nothing and says which type differs. The number of 5 axis rows is taken from the
  PLC (15 today); the file's 10 rows fill rows 1..10 and the rest go over as zeros.
- The teach tables are written only on Save. They are persistent in the PLC, so they are
  not sent again when the PLC comes back - and a table already in the PLC is not replaced
  by the file's values until someone saves.
- **5 axis is not run by the PLC's manager.** The HMI runs it itself (`Master.Sequences\FiveAxisCalibration`,
  with its own Start / Stop, no pause; SOP "Kinematic calibration SOP", slides 4 to 12). The PLC's `FB_Cal_FiveAxis`
  is not used for it; the 5 axis tables (translation point1..9, rotation point10..34, stations 1 and 2) are HMI-only
  files (`FiveAxisTranslation.json`, `FiveAxisRotation.json`) and are not written to the PLC.
  - **Moves**: the five axes move together as one G-code block in the CNC channel's MDI mode
    (`GVL_Channel_Interface.CNCData.Ch[n].UiToPlc`: `bMDIMode` rising edge, `sMDIProgram` = `G90 G01 X1=.. Y1=.. Z1=.. B1=.. C1=.. F..`,
    `bCycleStart` rising edge; done when `PlcToUi.bCycleRunning` has been TRUE and is FALSE, `bErrorState` = error), then each
    axis's `stAxisCmdStatus[n].rActPosACS` must be within `InPositionTolerance` of its target. Stop pulses `bCycleStop` and `bReset`.
    Symbol map: `Cnc.Members.MdiModeRequest / MdiProgram / CycleStart / CycleStop / MdiModeActive`. **PLC TODO to confirm**: that the
    channel runs an MDI block while the machine is in MANUAL (the Calibration section holds manual mode), the axis names in the CNC
    channel (the block uses `X1`, `Y1`, `Z1`, `B1`, `C1`; `Seq_NC_Program.nc` itself uses `Y1`, `B1`, `C1`, `X2`, `Z2`), and that no work offset is active (the position check reads ACS).
  - **Vision**: `C5A,index` (1 to 34) over TCP to `127.0.0.1:8000` (`Five-Axis Calibration.xlsx`); the answer carries the 49 dots' pixels.
    The protocol sheet names no command terminator: `VisionTerminator` in the settings is empty.
  - **Settings** `Data\Calibration\FiveAxisSettings.json` (copied to the data folder only if missing - copy it by hand to a machine that has
    the old folder): `PlateSpacingMm`, `ImageWidth` and `ImageHeight` have no source: they are edited on the 5-Axis tab (Setup row) and Start refuses while they are 0. `CncMode` "Auto" runs the block as a one-line `.nc` file (`AutoProgramFile`) in Auto mode instead of MDI; the PLC today only uses Auto mode inside AUTO_RUNNING with `Seq_NC_Program.nc` and M-codes, and its V.E. variables are disabled (`VE_VAR_ENABLED` FALSE).
  - **Result** `Data\Calibration\FiveAxisResult_Station{n}.json`: K, D, M_hand_eye, Y_C0, rx, ry, X_A, Z_A, Lx, Lz, E per validation image,
    E max / average and warnings. The SOP gives no limit for E. Nothing is written to the PLC or to the CNC kinematics.
- **Start** (the other calibrations) writes `GVL_Calibration.nSelect` and `nStation`, then pulses `bStart` (200 ms).
  `FB_CalibrationManager` decides whether it runs (machine in MANUAL or AUTO_RUNNING;
  nine point on stations 2 and 3 needs `aMaintenance[station]`) and reports `bBusy`,
  `bDone`, `bError`, `nStep` and `sMessage`, which the tab shows. **Abort** and **Reset**
  pulse `bAbort` / `bReset`.
- A new calibration: its `ST_Teach...` type and array in `GVL_CalTeach` and its entry in
  `E_Calibration` (PLC); a `CalibrationKind`, a `CalibrationTable`, its file and its
  `Calibration.Teach` entry (HMI).

### Static & Dynamic (Config > Calibration)

The tab after the four tables (`StaticDynamicViewModel`, `Views\Pages\Calibration\StaticDynamicView.xaml`,
design from the BAA_SQT reference) is the CCD verification the PLC's `Static_Seq` / `Dynamic_Seq`
run (`Calibration\StaticDynamic` in the TwinCAT project, interface `GVL_StaticDynamic`, symbol map
`Calibration.StaticDynamic`, `TwincatStaticDynamicService`).

- The PLC owns everything: the camera's working position (a row of its recipe), the axis
  moves, the vision command and its answer. The HMI writes `nMode` (1 static, 2 dynamic,
  0 none), `nCcd` (1 / 2 = `Cameras`) and `nIteration`, and pulses `bStart` / `bEnd` / `bStop`
  (200 ms, rising edges: End finishes after the running vision command, Stop aborts).
- It shows `bBusy`, `bDone`, `bError`, `nStep`, `nCount`, `sStatus` (CURRENT STATUS) and the last
  `rVisionX/Y/Z`, followed with ADS notifications only while the tab is on screen. The
  reference design's Start / End X / Y inputs and its CCD 3 are not there: the PLC takes no
  coordinates, and serves two cameras.
- **`nMode` is not a UI preference.** `Dispensing_Stn_A_Auto` reads it for every unit and, while it
  is not 0, skips dispensing and the CNC cycle (the station requests `aRequest[n]` instead). So the
  tab always shows the PLC's mode, asks before leaving Off, and has an Off choice that gives the
  machine back to production. Mode, camera and iteration are written as soon as they change
  (locked during a run), as the main sequence uses them without an HMI Start.
- A run needs the machine in AUTO (`AUTO_RUNNING` of the station's module): the tab says so, and
  the PLC answers "Machine must be in AUTO (press START)". That is the opposite of the tables'
  Manual-mode switch.
- "Command Not Received" (vision answered another command) raises a MODULE alarm; after RESET
  and START the PLC sends the command again.
- One row per vision command goes to `MAIN.sdlog` (`ST_SDLog`), written by the HMI to
  `C:\TEAL_Log_Data\StaticDynamic\...csv` (`StaticDynamicLogModel`, `CustomLogs.StaticDynamic`,
  `StaticDynamicTarget` in NLog.config).
- PLC placeholders still to fill in: the vision commands and expected answers
  (`TODO_STATIC_CMD_CCD1`, ...), the recipe rows (`aRow`, `aHomeRow`, `aUnloadRow`) and the
  vision answer's field layout (`FB_SDWork` `IDX_*`).

### Calibration flow

1. Config > Calibration, the calibration's tab. **Edit**, pick the station, enter its
   positions, tick **Taught**, **Save**. The message says whether the PLC took and stored them.
2. Tick **Manual mode** (or run the machine in auto, for a calibration that runs there).
3. Pick the station and press **Start**. The status line under the table is the PLC's own
   text: the step it is in, or why it did not start.

## 4. What the PLC still has to provide

Each is marked `PLC TODO` beside its entry in `PlcSymbols.json`.

| # | Missing in the PLC | Effect in the HMI today | Map entry to fill |
|---|---|---|---|
| 1 | Home command bit. `MAIN.modecall.bMachineInHome` is never set, so `bHomed` never becomes TRUE. | Homing button disabled; homing-done never shown. | `Commands.HomingStart` |
| 2 | Module sequences are not called from `MAIN` (only the two calibration programs are). | Auto run does nothing. | - |
| 3 | `GVL_Module.MAX_MODULES` is 10 but modules are numbered up to 15 (dispensing stations 11/12, backflow 13-15): out of range in `aModule` and `aLog`. | Those modules have no state and no sequence log. | add to `SequenceLog.Entries` |
| 4 | `FB_CylinderMapping.NUM_CYLINDERS` is 3; `E_Cylinders` lists 25. | Cylinders above 3 are outside the array. | - |
| 5 | Unit-entered signal (BOOL pulse or counter). | HIVE Running/Idle follows the PLC mode and a 90 s rule. | `Status.UnitEntered` |
| 6 | Finished-unit counter (DINT/UDINT) and serial number. | HIVE debug-unit limits and audit rounds do not count; no unit data (machine_data) is sent to HIVE. | `Status.UnitDoneCount`, `Status.UnitDoneSerial` |
| 7 | Dynamic test run mode in `E_RunMode`. | "Dynamic Test Mode" chip is locked. | `RunMode.Values.Dynamic` |
| 8 | Bypass variables for scanner, buzzer, SFC/PDCA, vision. `FB_TowerLampMapping` passes `bMuteBuzzer := FALSE`. | Those switches are locked. | `Bypass.Devices.*` |
| 9 | Glue alert level, buzzer mute, dispensing stop after a short picture count, and a stop request while the HMI itself has put HIVE into downtime (`HmiStopRequest`: PLC finishes the unit in process and stops). | Glue warnings and the picture check write nothing to the PLC; a hash key error or an unconfirmed HIVE upload changes the HIVE state only, the machine keeps running. | `Process.*` |
| 9a | Whether the last glue weighing was done by hand. | PD-17 cannot follow PD-10 (DFM slide 31). | `Glue.Weighing.IsManual` |
| 10 | Process values for the parameter dashboard, held per unit and named as HIVE's machine_data keys (`front_nozzle_temperature`, `front_valve_unit_count`, ...). | No live values beside the spec limits; HIVE unit data goes out without the process parameters. | `Parameters` |
| 11 | An ARRAY OF BOOL that disables the sensors, one element per `ESensor` member in the enum's order (`SensorDisable.Array`, lower bound in `FirstIndex`). The HMI writes TRUE to disable, FALSE to enable; only non-critical sensors are disabled. | Sensors cannot be disabled from the dashboard. | `SensorDisable` |
| 12 | The calibrations do not read `GVL_CalTeach` yet: `FB_Cal_NinePoint.ReadStart` and `FB_Cal_CameraNeedle.ReadRecipe` are TODO (they return "start position / recipe positions not available"), and `FB_Cal_FiveAxis` and `FB_Cal_Needle` answer "sequence not written yet". | The HMI writes and stores the teach tables and starts the calibration; the PLC then reports that message. | `Calibration.Manager`, `Calibration.Teach` |
| 13 | Dispenser interface (purge, valve on/off, weighing). Valve on/off in `Dispensing_Stn_A_Auto` is a comment. | No dispenser screen. | new section |
| 14 | Station 2 sequence (`Dispensing_Stn_B_Auto` is empty) and its recipe table. | Channel 2 positions go to `Recipe[5]`, which nothing reads. | `Cnc.Channels.2.RecipeGantry` |
| 15 | The finished unit's result, cycle time, tossing, station, failure code and fixture id, held until the next unit. | No production rows: Home I/O summary, Data I/O / Tossing / Yield empty. No HIVE unit data without the result and the cycle time. | `UnitRecord` |
| 16 | Measured coordinates of the finished unit. | Data > Axis empty. | `Coordinates.Points` |
| 17 | Glue weighing (counter, station, path, speed, weight, nominal, limits), glue counters per gantry, pot life and nozzle hours, regressions, weighing settings. | Data > Glue / Health and Home's glue panel say not connected; no glue warnings. | `Glue` |
| 18 | NG divert: a BOOL the HMI pulses, with the unit's serial number written first, when HIVE failed a unit or did not confirm it (S12). The PLC sends that unit to the NG conveyor; whether it holds the unit for the answer is the PLC's decision. | The unit is only logged; it goes on to the next station. | `Process.UnitUploadNg`, `Process.UnitUploadNgSerial` |
| 19 | NG warning buzzer: a BOOL pulsed at an NG below the 3 / 5 limits (T6). | The NG pop-up shows without the buzzer. The stop at 3 consecutive / 5 total uses `HmiStopRequest` (row 9). | `Process.NgWarningBuzzer` |
| 20 | Early warning (S17): tower light red, and the buzzer 2 s on / 1 s off timed by the PLC, each held TRUE by the HMI while a warning pop-up is open (the buzzer goes FALSE on Mute). | The pop-up shows without light or buzzer. | `Process.EarlyWarningLamp`, `Process.EarlyWarningBuzzer` |
| 21 | The vision software's glue path AOI state code of each dispensing pose, a DINT of seven digits held with the unit (S16). | No `poseN_aoi_inspection_state` key goes to HIVE. | `UnitRecord.AoiStates` |
| 22 | The valve SN the dispenser controller reports, as a STRING member of the dispenser block (S3.1 point 4). | The 5 minute valve SN check is not available for a valve model with an internal SN. | `ThirdPartyDevices.Dispenser.Members.SerialNumber` |
| 23 | CCD repeatability captures: a counter per capture, with the capture's X and Y, its station and the SN under the CCD, held until the next capture (S3.4). | PD-13 only changes the HIVE state: no delta is uploaded. | `Calibration.Repeatability` |
| 24 | Pose calibration: the flatness and pose routines, a counter raised when a whole calibration has finished, and every result held (S3.5 P-12). The pop-up to change the laser module's mounting between flatness and pose is the routine's own operator prompt. | PD-14 only changes the HIVE state: nothing is checked or uploaded. | `Calibration.Pose` |

### What the HMI does with rows 18 to 24

- **Unit hand-off.** For each step of `Status.UnitDoneCount`, `PlcUnitDoneWatcher` now also (a) has
  `UnitImagePackager` count and zip the unit's AOI images before the payload goes to HIVE (T5; App.config
  `UnitImageFolder`), (b) gives the unit's result, station and failure code to `NgStatistics` (T6), and
  (c) asks MES for an early warning (`EarlyWarningService`, S17). (b) needs `UnitRecord.Pass`.
- **Pulses.** `PlcPulse` writes TRUE, waits `Commands.PulseMilliseconds`, writes FALSE; pulses of one
  signal never overlap. `UnitUploadNgSerial` is written before its pulse.
- **Audit results.** `AuditResultWatcher` reads a capture or a calibration only while HIVE is in PD-13 /
  PD-14 with that audit run active; outside it the counters are ignored. Deltas are
  `{gantry}_delta_x_test{n}` (n: 1 static, 2 dynamic with the CCD static, 3 dynamic with the UC static),
  the gantry name from `Calibration.Repeatability.Gantries` by station. Limits are the Spec Limits row
  of the same name (the CPK or GRR table first, then Production).
- **Valve SN.** Read once per check with a plain ADS read, not a subscription: every
  `ValveSnCheckMinutes` on the clock, gantry n against dispenser instance n.

## 5. To confirm on the machine

- **CNC axis numbers** (`Cnc.AxisIndex`). They are taken from the order assumed in the
  PLC's own calibration program, which is itself marked unconfirmed. Jog one axis and
  watch which moves. A wrong number jogs the wrong axis.
- **I/O order.** A point's `Id` in `Data\Config.csv` is taken as its place in
  `GVL_Inputs.Inputs` / `GVL_Outputs.Outputs` (`E_Input` / `E_Outputs` order), counted
  from the card's `IOFirstIndex`. Config.csv
  lists 208 inputs and 160 outputs; the PLC arrays hold 225 and 188.
- **CNC positions.** `Load_Pos` copies `Recipe[4].Step[position, axis]` into V.E.
  variables as `(axis - 1) * 10 + position`, but `Seq_NC_Program.nc` reads `V.E.VALUE[1..3]`
  as three axes of one position. One of the two has to change in the PLC.
- **Device lists.** `Servos.json`, `Vacuums.json`, `Steppers.json`, `Robots.json` and
  `Gantry.json` still describe the earlier pressing/inspection machine. The PLC
  has five PTP servos (loading X/Z, unloading X/Z, weighing) in gantries 1-3, three
  steppers, one vacuum, one scanner, two CCDs and no robot.
- **Struct sizes.** `ST_SeqLog`, `ST_NinePointStatus` and `ST_NinePointPoints` are laid
  out by hand to match the PLC; a mismatch is reported when the symbol is subscribed.

## 5a. Third-party devices (Manual > Dispenser, Weighing, Vibration)

Each device is one PLC data block (`Instances[].Path`); `Members` maps every key the tab
uses to a member of the block, with its PLC type and scale. A key left out is simply not
shown on the tab. The three tabs share one layout (`PlcDevicePanelView`): status lamps,
switches and buttons, settings with "new value / in use", and the live values.

| Device | Blocks today | The operator sets | The operator sees |
|---|---|---|---|
| Dispenser (SV series, RS-232) | `MAIN.dispenser.dispenserdata[1..2]` | Recipe, ratio (dual fluid) or time + volume (single fluid), speed, then Save; Start, Stop, Reset; Read status switch | Powered / ready / busy / dispensing / done / error, mode, side A and B speed, output, pressure, glue level, current, turns, the settings as the dispenser reports them, alarm code and text |
| Weighing scale (LY-60005003, RS-232) | `MAIN.weighing.weighingdata[1]` | Min / max weight; Weigh, Read once, Tare, Stop, Reset | Weight, stable / unstable, pass / fail, weighing done, last reply, alarm texts |
| Vibration sensor (QM30VT3, Modbus RS-485) | `MAIN.vibration.vibsen_data[1..3]` | Monitoring switch, Read sensor settings, Fmax then Write to sensor, health limits, Reset warnings | Vibration, temperature, warnings and over-limits, sensor settings read back, link and error id |

Found in the PLC projects, to be fixed there:

1. The blocks take HMI commands only while their own `in.automode` is FALSE, which the
   mapping sets every cycle - not from the machine's MANUAL state.
2. The PLC overwrites every cycle: the vibration sensor's `config.naxisselection` and
   `config.nmodbusaddress`, the scale's `config.command` and `config.tstabletimeout`.
   The tabs only show them.
3. `status.Rbaudrate`, `Rparity`, `Rmodbusaddress`, `Rfmax` of the vibration sensor hold
   register addresses, not values; the map uses `status.baudrate` etc. instead.
4. The vibration sensor scales temperature by the same `par.fscale` as vibration.
5. The vibration warnings and the scale's pass / fail latch until Reset.
6. The vibration config read runs only while `hmicmd.breadconfig` is TRUE, so the map
   holds that button for 2 s (`PulseMilliseconds`).

The paths are those of the standalone projects (`rs485_final_30102026`,
`DISPENSER_WEIGHING_RS232_final`). Until the blocks are merged into the D-SCUD PLC, the
startup check lists them as missing.

## 6. Records the HMI keeps

The PLC holds only the last unit and the last weighing; the HMI keeps the history.

| What | Where | Kept |
|---|---|---|
| Finished units | TEAL_Master `[ProductionData]` | `RetentionDaysForProductionData` |
| Alarms | TEAL_Master `[Alarms]` | `RetentionDaysForAlarmData` |
| Glue weighings | `{LogFilePath}\MachineData\GlueWeighing\GlueWeighing_yyyyMMdd.csv` | `RetentionDaysForCSV` |
| Coordinates | `{LogFilePath}\MachineData\Coordinates\Coordinates_yyyyMMdd.csv` | `RetentionDaysForCSV` |

An event is read once, when the PLC's counter steps up. If the counter jumps by more than
one between two notifications, only the last event's values are still in the PLC: that
one is saved (and logged), and every unit is still counted for HIVE.

## 6a. Custom logs (PLC struct -> CSV row)

One custom log per kind of data (production, calibration, ...). The PLC logs a whole struct;
the HMI writes it as one row through the NLog custom logger (`Teal.Library.Log`).

```
PLC   st.sProductCode := 'ABC'; ...                 // fill your struct
      MAIN.prodlog.LogRow(ADR(st), SIZEOF(st));     // FB_CustomLog: queues one row
HMI   PlcCustomLogBridge reads the new rows, then   // ADS notification on nWriteCount
      CustomLogs.Production.Log(model);             // CustomLog<ProductionLogModel>
      and writes nAckCount back to the PLC.
```

- **PLC.** `FB_CustomLog` (`POUs\...\10_CustomLog`) is a queue of 16 rows of up to 4096 bytes
  (`GVL_CustomLog`). Every `LogRow` call is a row (the method is not called `Log`: LOG is an IEC function); nothing is lost while the HMI keeps up, and the
  HMI's acknowledge (`nAckCount`) means an HMI restart neither loses nor repeats a row. The PLC
  counts rows lost because the HMI was away (`nLost`) or refused (`nRejected`).
- **HMI.** The same data from C#: `CustomLogs.Production.Log(model)`. `CustomLog<T>` takes the CSV
  columns from the model's public properties, or its public fields when it has none, in
  declaration order; `Date_Time` is added by the logger.
- **Model = PLC struct.** `ProductionLogModel` mirrors `ST_ProductionLog`: same members, same order
  (STRING(n) = `ByValTStr` n + 1, BOOL = `U1`, INT/DINT/LINT = short/int/long, REAL/LREAL =
  float/double, TIME = uint ms, DT = uint s; flat). If the sizes differ the bridge drops the row
  and says so in the running log.

### A new kind of data (for example calibration)

1. PLC: a struct `ST_CalibrationLog`, an instance `calibrationlog : FB_CustomLog;` in `MAIN`, and
   `MAIN.calibrationlog.LogRow(ADR(st), SIZEOF(st))` where a calibration finishes.
2. HMI: `CalibrationLogModel` (same members as the struct), a `CustomLog<CalibrationLogModel>`
   in `CustomLogs` and its line in `CustomLogs.All`.
3. `Data\PlcSymbols.json`: `"CustomLog": { "Logs": { "Calibration": "MAIN.calibrationlog" } }`.
4. NLog.config: a target and a rule (below).

### NLog.config for a custom log

The target must exist before the app starts (the logger fails with "Target ... was not found"
otherwise). The rule must be `final` and come first, so a row is written only to its own log. Do not
set a layout: the logger sets the CSV layout itself.

```xml
<target xsi:type="File" name="ProductionTarget" createDirs="true" keepFileOpen="false"
        fileName="C:/TEAL_Log_Data/ProductionData/${date:format=dd-MM-yyyy}/${date:format=dd-MM-yyyy_HH}.csv" />

<logger name="ProductionLogger" minlevel="Info" writeTo="ProductionTarget" final="true" />
```

## 7. Adding a PLC variable later

1. Declare it in the PLC.
2. Put its full symbol path in the matching entry of `Data\PlcSymbols.json`.
3. Restart the HMI. The startup check confirms the PLC has it.

A new *kind* of function (for example the dispenser) needs a section in
`PlcSymbolMap.cs`, a service under `Services\Twincat` that takes that section, and a
contract in `Nova.Abstractions` for the view model.
