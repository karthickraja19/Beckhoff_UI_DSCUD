# DFM pending work - implementation plan

Written 2026-11-12 from Audio SW DFM v5.4, HIVE PayloadSpec 5.6 and the folders under
`2. SW Key Functional Specifications`, checked against the code as it is today.
Sizes are rough: S = under a day, M = one to three days, L = more.

**Status 2026-11-13: every step below is built on the HMI side.** Each section starts with
what was built and what is still open. Nothing here has run against the PLC, MES or the
HIVE gateway: the logic was checked on a desk against the built assemblies, and the new
screens have not been looked at on a running HMI. What still waits on the PLC is in the
table at the end; what waits on an answer is in the questions.

## Rules every item follows

- The PLC owns machine logic. Where the machine has to stop, divert a unit or sound the
  buzzer, the HMI writes a signal from `PlcSymbols.json` and the PLC acts on it.
- A blank map entry means "not available", shown with its reason. No stand-in values.
- MES calls go through `IMesClient` (`/api/Assy/GetCmdResult`). With `MesUrl` blank the
  step is skipped and logged, as the PD-04 and PD-12 procedures do today.
- ViewModels stay free of TwinCAT and library types; new services get a contract in
  `Nova.Abstractions`.
- Each item updates `AGENTS.md`, `Docs\TwinCAT_Integration.md` and the `PLC TODO`
  comments in the map.
- The HIVE error codes named below (`X01IPOO-01`, `P99DSOO-01`, `G05DSVP-01`) are
  proposals built from the DFM's code tables; AME confirms them before they ship.

## Order of work

| Step | Item | Size | Needs PLC change | Needs an answer first |
|---|---|---|---|---|
| 0.1 | Audit uploads: gateway codes 1024 / 1025 count as success | S | no | no |
| 0.2 | HMI-raised downtime must also stop the machine | S | yes (1 BOOL) | no |
| 0.3 | PD-05 and PD-10 notes | S | no | Q1 |
| 0.4 | Manual opened from HIVE Idle | S | no | Q2 |
| 1.1 | MES hash-key check on every switch to Running | M | uses 0.2 | Q3 |
| 1.2 | MES machine-state report | M | no | Q4 |
| 2.1 | NG counting: 3 consecutive / 5 total (T6) | M | unit record, 0.2 | Q5 |
| 2.2 | Refused or unconfirmed unit goes to the NG conveyor (S12) | S | yes (1 BOOL) | no |
| 1.3 | Maintenance Tracking (S11.3) | L | no | Q6 |
| 1.4 | Early warning for common defect factors (S17) | M | unit record, buzzer / lamp | Q7 |
| 2.3 | Unit picture zip: T5 count and the HIVE blob | M | no | Q8, Q9 |
| 2.4 | Glue path AOI inspection state (S16) | S | vision code per pose | Q10 |
| 3.1 | PD-04 valve SN check every 5 minutes | M | valve SN in the dispenser block | Q11 |
| 3.2 | PD-12 gaps | S | no | no |
| 3.3 | PD-13 CCD repeatability: captures and deltas | M | capture results | Q12 |
| 3.4 | PD-14 dispensing pose calibration: results and upload | M | calibration routines | Q12 |
| 3.5 | 24-hour dry run: UUID.dll and UMSID.dll (S4.1) | M | no | Q13 |

Steps 0.x are small and unblock the rest. 1.1, 1.2, 1.3 and 2.3 can be built and tested
on a desk today. 2.1, 1.4, 2.4 and 3.x can be built now but only run once the PLC gives
the unit record (`Status.UnitDoneCount`, `UnitRecord`) or the signals listed at the end.

---

## 0. Small fixes

### 0.1 Audit uploads: 1024 / 1025 are a success  (S3.4, S3.5)

**Done.** `HiveUnitData.IsAudit`, `HiveClient.RefusedBecause`, `HiveService.AuditUploadFailed`
and a message box in `HiveStatePrompt`; tested with the mock gateway (1024 on an audit
unit accepted, on a normal unit refused, 1500 on an audit unit refused and shown).

- **Spec.** During LAD, CCD repeatability and pose calibration the gateway answers
  `"Result":"FAIL"` with `ErrorCode` 1024 (unexpected keys such as `Mode`,
  `Operator_ID`, `TestSeriesID`) or 1025. Both mean the upload succeeded. Any other
  error code is a failure and must be shown to the OSS.
- **Today.** `HiveClient.RefusedBecause` treats every `FAIL` as refused, so audit units
  would be logged as errors.
- **Build.** `HiveUnitData.IsAudit` (set by `HiveService.SendUnit` when it adds the audit
  keys). For an audit unit, 1024 and 1025 give `Accepted`. Any other code on an audit
  unit shows a message box with the gateway's text.
- **Files.** `Nova.Hive\HiveUnitData.cs`, `HiveClient.cs`, `HiveService.cs`,
  `HiveStatePrompt.cs`.
- **Check.** Extend the mock-gateway test with a 1024 answer for an audit and a normal unit.

### 0.2 HMI-raised downtime must also stop the machine

**Done on the HMI side.** `Process.HmiStopRequest` in the map and `PlcSymbols.json`;
`HiveService.HmiStopChanged` is raised by `ReportFault` and cleared when HIVE leaves
downtime; wired in `App.xaml.cs`. Blank in the map gives a startup warning. Still to do:
the PLC variable and its stop reaction, and writing the bit again when the PLC comes back.

- **Problem.** `HiveService.ReportFault` (hash key error, HIVE upload not confirmed,
  picture count) changes the HIVE state only. The PLC keeps running, so HIVE says down
  while the machine produces. Only the picture check has its own stop bit.
- **Build.** One BOOL, `Process.HmiStopRequest`, held TRUE while HIVE is in a downtime the
  HMI raised itself, FALSE when HIVE leaves downtime. The PLC finishes the unit in
  process, stops feeding and goes to its stop state. `HiveService` raises an event with
  the root error's origin; `App.xaml.cs` wires the writer as it does for
  `PictureDispensingStop`.
- **PLC.** New variable and the stop reaction.

### 0.3 PD-05 and PD-10 notes  (slide 31)

**Done (Q1 answered: "MES switched off").** PD-05 is greyed out while `MesUrl` is set
(`PlannedDowntimeProcedures.IsMesConfigured`). PD-17 after PD-10 needs
`Glue.Weighing.IsManual` from the PLC (new, blank): TRUE allows it, FALSE or blank refuses
it with the reason. Tested for the three cases. The original notes follow.

- PD-05 Stress Test is "for non-MES stress test only": offered only while MES is not
  configured or the run mode is not Production (Q1).
- PD-17 after PD-10 only "after manual glue weighing, not auto": needs the PLC to say the
  last weighing was manual (`Glue.Weighing.IsManual`, new) - until then PD-10 does not
  allow PD-17 and the option says why.
- **Files.** `PlannedDowntimeDialog.xaml.cs`, `HiveStateMachine.CheckDowntimeChange`.

### 0.4 Manual opened from HIVE Idle  (decision Q2)

**Decided: no.** Manual opens from Idle without a prompt. Nothing to build.

- If confirmed: opening Manual, Calibration's switch or Recipe > Gantry / CNC while HIVE
  is Idle first shows the planned-downtime dialog (reason, badge). Cancel keeps the
  operator out. In downtime already: no prompt.
- **Files.** `AudioSettingsViewModel.cs`, `RecipeViewModel.cs`, `IHiveStatePrompt`
  (a method that returns whether a planned downtime was entered).

---

## 1. MES and GUI work that needs no PLC change

### 1.1 MES hash-key check on Running  (S3.6, DFM slides 77 and 80)

**Done.** `IHashKeyService.CheckWithMesAsync` (`CHECK_AUDIO_INFO`, same `results` string as
the upload), `HashKeyGuard` started with HIVE, App.config `HiveHashCheckOnMesError`
(downtime / ignore). Tested against a mock MES: true lets production run; false gives
downtime `O99OOOO-01` with MES's text and the stop request; no usable answer gives
`X02IPOO-01` (or nothing with "ignore"); no MesUrl, no check. Open: Q3 - AME to confirm the
two codes (`O99OOOO-01`, `X02IPOO-01`) and the MES-down behaviour. The original notes follow.

- **Spec.** Every time HIVE switches to Running the machine posts `CHECK_AUDIO_INFO` with
  the same `results` string as the upload (`key=MS_SHA1$value=<40 hex>,key=...`, only keys
  with a path, no trailing comma).
  - `Result:true` (also "This station has not enabled hash key check") - production runs.
  - `Result:false` (hash changed, key count differs, terminalName unknown) - stop feeding
    and HIVE goes to Downtime automatically, with MES's `RetMsg` as the error detail.
- **Today.** Only `SAVE_AUDIO_INFO` exists (`HashKeyService.UploadToMesAsync`).
- **Build.**
  - `IHashKeyService.CheckWithMesAsync()` beside the upload, sharing the `results` builder.
  - `HashKeyGuard` (Nova.Wpf.App): on `HiveService.StateChanged` to Running, run the
    check off the UI thread; on false call `ReportFault` (code per Q3) and step 0.2 stops
    the machine. MES not configured: skipped and logged.
  - MES not reachable: per Q3 (proposal: downtime `X01IPOO-01`, "Failed to receive
    information from MES").
- **Files.** `IHashKeyService.cs`, `HashKeyService.cs`, new `HashKeyGuard.cs`, `App.xaml.cs`.
- **Check.** Mock MES returning true, false and no answer.

### 1.2 MES machine-state report  (S1.1.2 "Machine State Reporting to MES V5.0")

**Done.** `HiveService.StateChangeSent`, `IMesClient.PostMachineStateAsync`, `MesStateReporter`
(started with HIVE when `MesUrl` is set), App.config `MesStateUrl`, `MesMachineName`,
`MesLineName`. Tested the payloads for 2->5, 5->5 with a badge, 5->1 and 1->2, and the
post path. Open: Q4 - the real URL, machine name and line values, and whether AME still
wants it now that v5.x reports through HIVE. The original notes follow.

- **Spec.** Each HIVE state change is also posted to
  `http://<mes>/api/machine/collmachinestatefromcard`:
  `uniqueID` (station id + `_` + yyyyMMddHHmmssfff), `terminal_Name`, `machine_Name`,
  `machine_State`, `state_Change_Time` ("2024-4-23 08:30:00"), `vendor` (capitals),
  `uid` (manual changes only), `pdline_name`, and `data` with `sW_Version`, `mS_SHA1`,
  `vS_SHA1`, `CD_SHA1`, `sequence`.
  - State 1: `message`, `code`, `serverity`, `data.error_Detail` only when coming out of
    downtime; `occurrence_Time` = downtime start, `resolved_Time` = now.
  - State 2: `occurrence_Time` = change time, the rest empty.
  - State 5: the error, `occurrence_Time` = downtime start, `resolved_Time` empty.
  - Answer `{"Result":false,...}` is written to the machine log.
- **Build.** `IMesClient.PostMachineStateAsync`; `MesStateReporter` (Nova.Wpf.App)
  listening to a new `HiveService.StateChangeSent` event that carries the whole
  `HiveStateChange`; it remembers the downtime's start and error for the state 1 report.
  Settings: `MesStateUrl` (blank = same host as `MesUrl`), machine name, vendor and line
  from the station profile.
- **Files.** `IMesClient.cs`, `MesClient.cs`, new `MesStateReporter.cs`, `HiveService.cs`,
  `App.xaml.cs`, `App.config`.
- **Open.** Q4.

### 1.3 Maintenance Tracking  (S11.3 V1.2)

**Done.** `IMaintenanceStore` / `JsonMaintenanceStore` (`Data\Settings\Maintenance.json`),
`MaintenanceTabViewModel` and the Maintenance tab of the Setting page (Level 2 / 3). Tiles per
fixture (grey, green, yellow, red), the table, Edit only in PD-01 / PD-09, one line at a time,
Add New Item, Remove, Maint. / Add with `EQUIPMENT_MT` (`serial_Number` fixture_item, `empNo` and
`VALUE` the signed-in card, `MACHINE_CODE` project_line, `PROCESS` the profile's Station), the
duplicate check, the hourly refresh and the CSV export. Checked: health for good / need to
maintain / overdue / never maintained, the period rule, the store's save and remove.
Differences from the notes below: the tab shows to Level 2 / 3 at any time and only Edit is tied
to PD-01 / PD-09, as S11.3.2 says ("Edit is hidden in every other state"); the fixtures start as
"Front Gantry" and "Rear Gantry" (S11.3.2 lists front and rear gantry for dispensing machines -
"Dragon Door" is its translation) and are a list in the file. With no MesUrl the MES step is
skipped. Open: Q6 - an overdue item only shows red, it raises no alarm; MES not tried.

- **Spec.**
  - A Maintenance tab on the Setting page, for Level 2 / 3, shown only while HIVE is in
    PD-01 or PD-09 (as the Hash Key tab is tied to PD-15). Edit appears only then.
  - Overview: one tile per fixture, grey (disabled), green (all in date), yellow
    (countdown at or below the threshold), red (overdue). A tile opens its table.
  - Table: Active, Project, Line type, Head ID, Maint. Item, Maint. Period (h), Maint.
    Countdown, Alarm-triggering Threshold (h), Latest maintenance record, Scheduled Maint.
    Time, Health Status.
  - Countdown = scheduled time - now, worked out at start and every hour on the hour.
    Scheduled = latest record + period. Status: below 0 Overdue maintenance; up to the
    threshold Need to maintain; above it Good health.
  - Edit: one row at a time (row turns RGB 86,193,255, "Maint. / Add" shows). Add New Item
    (grey 213,213,213 then yellow 245,242,215), Remove, Cancel, Export (CSV).
    Fixture ID + Maint. Item must be unique; a duplicate turns both red and hides the button.
  - "Maint. / Add" and Remove post `EQUIPMENT_MT` with
    `testData = MACHINE_CODE=H36_R1$PROCESS=M_H086$ENABLE=Y|N$VALUE=<card UID>$MAX_TIME=168$ALERT_TIME=48`.
    True: green dialog, saved to the file, row locked. False: red dialog, nothing saved.
- **Build.** `IMaintenanceStore` + `JsonMaintenanceStore` (`Data\Settings\Maintenance.json`),
  `MaintenanceTabViewModel`, its view, an hourly timer, CSV export through the same
  exporter the Data page uses.
- **Open.** Q6.

### 1.4 Early warning for common defect factors  (S17 X3)

**Done.** `IEarlyWarning` / `EarlyWarningService` (called for every finished unit),
`EarlyWarningWindow` and `MesCardDialog` (background 255,149,202, always on top, the GUI goes to
the Alarm page), the Early Warning tab of the Setting page (test box, Reset, the disable box),
`Process.EarlyWarningLamp` / `EarlyWarningBuzzer` in the map. Close and the switch check the
card with MES through `GET_OP_ACCESS` ("UID,Close" / "UID,Authorize"), as S17 pages 8 and 10
give it. App.config `EarlyWarningClose` = "card" (AE / HIVE) or "confirm" (URP / ME). Disabled
is kept in memory only, so it is enabled again at every start. Open: Q7 - which of the two this
station is ("card" is the default), and the PLC's two signals; the 2 s on / 1 s off is timed by
the PLC. Not tried against MES.

- **Spec.**
  - After each unit passes the station the machine posts `GET_SMART_FA_ALTER_MSG` with the
    unit SN and the terminal name.
  - `Result:false` opens a warning pop-up with the text out of `RetMsg`. The machine does
    not stop. The window stays on top of every other window, background RGB 255,149,202.
  - Tower light red on, buzzer 2 s on / 1 s off, Mute button. Both end when the pop-up closes.
  - Closing needs a Level 3 card, checked with MES (`<Card_UID>,Close`).
  - Setting: an enable / disable checkbox, changed only with a card (`<Card_UID>,Authorize`),
    enabled again at every start. Disabled silences the pop-up and buzzer only - the MES
    request is still sent.
  - A test box: SN and terminal name, Test, Reset, with MES's answer shown.
- **Build.** `EarlyWarningService` (called from `PlcUnitDoneWatcher`), `EarlyWarningWindow`
  (after `GlueAlertWindow`), an Early Warning tab on the Setting page, two PLC signals
  (`Process.EarlyWarningLamp`, `Process.EarlyWarningBuzzer`).
- **Open.** Q7.

---

## 2. Unit quality logic

### 2.1 NG counting: 3 consecutive or 5 total  (T6, DFM slide 32 code P)

**Done.** `NgStatistics` (Nova.Hive), fed by `PlcUnitDoneWatcher` (result, station, failure code)
and by `HiveService.UnitDataResult` for a unit HIVE's own check failed. Below the limits: a
pop-up and a pulse on `Process.NgWarningBuzzer`. At 3 in a row or 5 in total: `ReportFault` with
`P99DSOO-01` "Consecutive Error", which also raises the stop request of 0.2. Nothing counts
while HIVE is in downtime or in manual (T6 rule 2.1). The counts are in
`Data\Settings\NgStatistics.json`. App.config: `HiveNgScope` (gantry / machine),
`HiveNgConsecutiveLimit`, `HiveNgTotalLimit`. Needs `UnitRecord.Pass`. Open: Q5 - the "5 total"
count starts again only when HIVE leaves the downtime it raised; a shift or day reset is not
built. "gantry" is the default scope (T6 row 3: Dispense, 1x glue type). Not run with units.

- **Spec.**
  - A unit's final NG counts 1 (retries inside the PLC do not count). First NG: pop-up and
    buzzer, no stop. 3 NG in a row, or 5 NG in total, puts the machine down: pop-up,
    buzzer, machine stop after the unit in process, HIVE Downtime.
  - A good unit resets the consecutive counter.
  - Counted: MES errors, vision errors, scanning, laser, upload-to-HIVE errors, value out
    of spec, recheck NG, positioning NG. Not counted: communication failures - those are a
    downtime at once.
  - Scope: per gantry for single-glue dispensing, per machine for dual glue.
- **Build.** `NgStatistics` in Nova.Hive, fed by `PlcUnitDoneWatcher` with the unit's
  result, station and failure code, and by `HiveService.UnitDataResult` for refused
  uploads. At a threshold: `ReportFault` with `P99DSOO-01`, "Consecutive Error"
  (slide 32), detail naming the last units. Counters kept in a small state file so a
  restart does not clear them. App.config: scope (gantry / machine) and both thresholds.
- **Open.** Q5.

### 2.2 Refused or unconfirmed unit to the NG conveyor  (S12)

**Done on the HMI side.** `UnitUploadNgSignal` writes the SN to `Process.UnitUploadNgSerial`,
then pulses `Process.UnitUploadNg`, on `Refused` and `NotConfirmed`. Blank in the map gives a
startup warning. Still to do: the two PLC variables and the divert.

- **Spec.** A unit HIVE failed, or did not confirm after three tries, must not go to the
  next station: it goes to the NG conveyor.
- **Build.** `Process.UnitUploadNg` (BOOL pulse, with the unit's SN in
  `Process.UnitUploadNgSerial`) written on `HiveUnitOutcome.Refused` and `NotConfirmed`.
  Note the DFM's own rule that the wait "does not gate the unit": the PLC decides whether
  it holds the unit for the answer.
- **PLC.** The two variables and the divert.

### 2.3 Unit picture zip: T5 count and the HIVE blob  (T5 X7, DFM slides 19 and 20)

**Done.** `UnitImagePackager` (App.config `UnitImageFolder`, `UnitImageZipFolder`): the images
whose file or folder name contains the SN are counted, `ReportUnit` is called, and one zip with
the DFM's name goes into `BlobFiles`. "Enable Picture Quantity Check" (Level 3) is on the
Picture Qty. Check tab: off, the count is still recorded but does not stop the machine. The
count is the first row of Home's Parameter Dashboard. Checked: two images found among others, the
zip's name and contents, the record file, the switch surviving a restart. Open: Q8 - where the
vision software really puts the images (the folder is blank, so nothing is counted yet); Q9 -
the code still does both (downtime and dispensing stop). T5 page 7 lists SCUD under Option 1,
machine to downtime.

- **Spec.** A unit's AOI images go to Insight as one zip named
  `UnitSN_Project_Line_Station_Instance_yyyyMMddHHmmss.zip`. Before the zip is made the
  number of images is checked against the Pic_Upload_quantity limits; the count is logged
  to `D:\Pic_Upload_Record`.
- **Today.** `PictureQuantityCheck.ReportUnit` exists but nothing calls it, and
  `HiveUnitData.BlobFiles` is always empty.
- **Build.**
  - `UnitImagePackager`: finds the finished unit's images (Q8), counts them, calls
    `ReportUnit`, writes the zip with the DFM's name and adds it to `BlobFiles`.
  - "Enable Picture Quantity Check" (Level 3) on the Picture Qty tab.
  - The count as the first row of Home's Parameter Dashboard.
  - Option 1 or 2 per Q9: today the code does both (downtime and dispensing stop).
- **Files.** New `UnitImagePackager.cs`, `PictureQuantityCheck.cs`,
  `PictureQtyTabViewModel.cs`, `AudioHomeViewModel.cs`, `PlcUnitDoneWatcher.cs`.

### 2.4 Glue path AOI inspection state  (S16 X4)

**Done.** `UnitRecord.AoiStates` in the map: a pose listed there is sent under its key; one that
reads 0 or cannot be read goes out as 0 with both limits 1111111 (rule 1); leading zeros drop by
being a number (rule 2); other limits are the Spec Limits row. A pose not listed is not sent, so
nothing changes until the map names one. Open: Q10 - how many poses and how the code reaches the
PLC. S16's example matrix gives TE010 one pose with three checks (1112222), marked "only for
example".

- **Spec.** One key per dispensing pose, `poseN_aoi_inspection_state`: seven digits, one
  per check (no glue, shift, missing, broken, keep-out, hole, less glue), each 1 enabled,
  0 disabled, 2 not applicable. No code from the vision software: send 0 with limits
  1111111 / 1111111. Leading zeros are dropped (0111222 is sent as 111222). Limits are the
  station's matrix value, both sides the same.
- **Build.** If the PLC publishes each pose's code as a DINT, the existing `Parameters`
  entries carry it (leading zeros drop by themselves) and the limits come from Spec Limits
  rows. Code needed only for "no code = 0 with limits 1111111": a list of AOI keys in the
  map (`UnitRecord.AoiStates`) that are sent as 0 when the PLC value is absent.
- **Open.** Q10.

---

## 3. Planned downtime procedures

### 3.1 PD-04 valve SN check every 5 minutes  (S3.1 V4.1)

**Done on the HMI side.** `ValveSerialMonitor` (App.config `ValveSnCheckMinutes`, on the clock):
for a gantry whose valve model has an internal SN it reads the dispenser block's `SerialNumber`
and compares it with `GantryConsumables.ValveInternalSn`, which PD-04 now records. A mismatch or
a failed read is `ReportFault` `G05DSVP-01`. Paused in downtime except LAD. Without the map
member, a startup warning. Open: Q11; and S3.1.2 wants PD-04 itself to read the new SN from the
controller ("readings") instead of a scan - PD-04 still scans it, not changed here.

- **Spec.** For valves with an internal SN the software reads it from the controller every
  5 minutes on the clock and compares it with the SN in the configuration file. A
  mismatch or a failed read: pop-up, buzzer, the shutdown logic and state change, and no
  new feeding (discharge only). Paused while HIVE is in Downtime, except in LAD.
- **Build.** `ValveSerialMonitor` (Nova.Wpf.App): reads the SN through `IThirdPartyDevices`
  (key `SerialNumber` on the Dispenser block), for gantries whose `ValveModel.InternalSn`
  is true; on a mismatch `ReportFault` (`G05DSVP-01`, Controller Unresponsive, or per AME).
- **PLC.** The controller's valve SN as a STRING member of the dispenser block.
- **Open.** Q11.

### 3.2 PD-12 Nozzle Change gaps  (S3.3 V3.0)

**Done.** MES order status, valve confirmation, link; the valve scan for a valve with a QR label
(a different SN sends nothing to MES); the typed nozzle SN behind a "no QR label" box; old SN
"0000"; `MAINTAIN_TIME` / `REMAINING_TIME` kept per gantry and shown as `Front_Health_Status` /
`Rear_Health_Status`; `NozzleHealthGuard` asks MES again at the next switch to Running and puts
HIVE back into PD-12 when MES fails the nozzle or it has no health left. Not built: S3.3 2.2's
hourly health request with its 4 h / 2 h prompts from MES's value - `GlueWarningMonitor` prompts
from the PLC's nozzle hours today. MES not tried.

- Order of the MES calls: `GET_RUBBER_VALVE_STATUS` first, then the valve double
  confirmation, then `UPDATE_RUBBER_BODY_SN`. Today the link is sent first.
- Valve double confirmation: where the valve has a QR label the operator scans it and it
  must equal the recorded SN, or no link request is sent. Today the recorded SN is used
  without a scan.
- Nozzle without a QR label: the SN may be typed (the one place typing is allowed).
- First change after install shows the old SN as "0000".
- `MAINTAIN_TIME` and `REMAINING_TIME` from MES's answer shown on the Parameter Dashboard
  as `Front_Health_Status` / `Rear_Health_Status` (display only, never sent to HIVE).
- First unit after the change: MES health check before the machine runs.
- **Files.** `PlannedDowntimeProcedures.cs`, `ProcedureDialog.xaml.cs`, `IConsumableStore`.

### 3.3 PD-13 CCD repeatability  (S3.4 V3.0)

**Done on the HMI side.** `Calibration.Repeatability` in the map (capture counter, X, Y, station,
SN), `RepeatabilityRun` (first capture kept, then max - min so far), `AuditResultWatcher` uploads
`{gantry}_delta_x_test{n}` / `_y_` through `SendUnit` and counts the round. `AuditSession`: 100
uploads for CPK / SCS, GRR 30 / 30 / 30 and 10 more as operator 3. Cancel at the end of a round
ends the run: HIVE stays down and no further capture is taken. Checked: the delta arithmetic, the
key name and the round sizes. Open: Q12 - the PLC has to publish the captures; S3.4 says a SCUD
gets the SN from MES, here it is the PLC's (`Repeatability.Serial`, else `Status.UnitDoneSerial`).

- **Spec.**
  - 100 captures per test; the first capture is kept in memory only, from the second on
    `delta = max - min` so far is uploaded, key `{gantry}_delta_{x|y}_test{1|2|3}`
    (`front_delta_x_test1`; test 1 static, 2 and 3 dynamic).
  - GRR: 30 captures each for operators 1, 2 and 3, then 10 more as operator 3.
  - When the test is done and Cancel is pressed the machine stays in planned downtime and
    will not start another test; a re-test needs Running and then PD-13 again.
- **Today.** `AuditSession` counts 100 "units" per round and nothing produces the deltas.
- **Build.** A capture feed (`Calibration` / vision results from the PLC, Q12),
  `RepeatabilityRun` holding the min and max per axis, the upload through `SendUnit`,
  and the GRR 30 / 30 / 30 / 10 sequence in `AuditSession`.

### 3.4 PD-14 dispensing pose calibration  (S3.5 V3.0, DFM slides 74 and 75)

**Done on the HMI side.** PD-14 now scans a test SN with the badge. `Calibration.Pose` in the map
(a done counter and the results by HIVE's key); `AuditResultWatcher` checks each result against
its Spec Limits row: all in spec is one upload with the audit keys, one out of spec is no upload
and a pop-up. A machine alarm while PD-14 is on marks the run failed, and its results are
refused. Results go through the map by name, not through `ICalibrationService.aResult`. Not
built: blocking manual HIVE changes while the calibration runs; the pop-up between flatness and
pose is left to the PLC routine's own operator prompt. Open: Q12 and the PLC routines.

- **Spec.**
  - Fully automatic flatness and pose calibration; Level 3 only (done).
  - Any value out of spec ends the calibration: nothing is uploaded, a pop-up tells the
    OSS to check and tune.
  - A machine alarm during the calibration means it has to start again.
  - Between flatness and pose a pop-up asks the OSS to change the laser module mounting.
  - On success one upload with the audit keys and the 16 results, named
    `conveyor_L&front_dispensing_pose1_C`, `conveyor_L&front_base_flatness`, ... (S3.5 P-12).
  - Cancel at the end leaves HIVE in downtime; a re-test needs Running and PD-14 again.
- **Build.** Tie the PD-14 audit session to `ICalibrationService`: start the routines,
  read `aResult`, check each against its Spec Limits row, then upload or stop. An alarm
  reported while it runs marks the session failed.
- **PLC.** The flatness and pose routines (`Calibration.Routines`, blank today).

### 3.5 24-hour dry run: UUID.dll and UMSID.dll  (S4.1, RQ DLL_X64 V2.0.0.1)

**Done, not run.** `DryRunIds` (Nova.Hive) calls `GenerateUuid` / `GenerateUmsid` exactly as the
package's own C# wrapper (`LibXdll.dll`) and the quick-start guide do: SI code as an ANSI string,
state and previous state, the answer split at `&&`. `HiveService` uses it while the HIVE Control
tab has the dry run on; unit_sn, input_time, mac_addr and unique_machine_state_id (with its
state_change_time) come from the DLLs. The app is AnyCPU and runs 64 bit. The DLLs are not in the
repository: put the package's "C#&Winform" Release files in `References\RemoteQualification` and
the build copies them beside the exe. Without them or `HiveDryRunSiCode` the dry run cannot be
enabled and the tab says why. A unit UUID.dll gives nothing for is not sent. Open: Q13 - the SI
code and the redistribution; the DLLs themselves were never called.

- **Spec.** While the 24-hour dry run is on, `unit_sn`, `input_time` and `mac_addr` of
  every machine_data payload come from `UUID.dll`, and every machine_state payload carries
  `unique_machine_state_id` from `UMSID.dll` (both called with the SI code, the state and
  the previous state).
- **Build.** P/Invoke wrappers for the two DLLs (x64 - check the app's platform target),
  used by `HiveUnitDataBuilder` and `HivePayload.MachineState` while
  `IHiveControl.IsDryRunEnabled`. The DLLs ship in `References\` and are copied to the
  output.
- **Open.** Q13.

---

## What the PLC has to add

| For | Variable | Type |
|---|---|---|
| Unit data, production rows, NG counting, early warning | `Status.UnitDoneCount`, `Status.UnitDoneSerial`, `UnitRecord.*` | already in the map, blank |
| Process parameters for HIVE | `Parameters.*`, held per unit | already in the map, empty |
| 0.2 | `Process.HmiStopRequest` | BOOL, HMI writes |
| 0.3 | `Glue.Weighing.IsManual` | BOOL |
| 2.2 | `Process.UnitUploadNg`, `Process.UnitUploadNgSerial` | BOOL pulse, STRING |
| 1.4 | `Process.EarlyWarningLamp`, `Process.EarlyWarningBuzzer` | BOOL |
| 2.1 | `Process.NgWarningBuzzer` | BOOL pulse |
| 2.4 | AOI state per pose, `UnitRecord.AoiStates` | DINT |
| 3.1 | Valve SN from the dispenser controller | STRING in the dispenser block |
| 3.3 | CCD capture X / Y per capture, capture counter, station, SN: `Calibration.Repeatability` | LREAL, UDINT, INT, STRING |
| 3.4 | Flatness and pose calibration routines, a done counter and the results: `Calibration.Pose` | UDINT, LREAL |

## Questions to settle before the step that needs them

- **Q1** PD-05: what marks a "non-MES stress test" on this machine - MES switched off, or a run mode?
- **Q2** Opening Manual from HIVE Idle: ask for a PD code and badge, or leave HIVE Idle?
- **Q3** Hash check: which error code for a failed check (the DFM log shows `O-ERR20240099`,
  which is not the v5.4 format), and what if MES does not answer - downtime or retry?
- **Q4** MES state report: the real URL, the machine name and line values, and is it still
  required now that v5.x reports through HIVE?
- **Q5** T6: when does the "5 total" counter reset (shift, day, good units, restart)? Is
  this machine single glue (per gantry) or dual glue (per machine)? Which option applies,
  1 (3 / 5) or 2 (1 NG)?
- **Q6** S11.3: the fixture list for this machine (the spec names "Front / Rear Dragon
  Door" for dispensers), what Head ID and Item Code hold, and does an overdue item only
  show red or also raise an alarm?
- **Q7** S17: is this station "AE / HIVE" (card needed to close) or "URP / ME" (Confirm
  only)? Which tower light and buzzer outputs?
- **Q8** Where does the vision software put a unit's images, and how is the SN in the file
  or folder name?
- **Q9** T5: Option 1 (downtime) or Option 2 (dispensing control) for this machine?
- **Q10** S16: does the AOI code reach the HMI through the PLC or straight from the vision
  software, how many poses, and the matrix limit of each?
- **Q11** PD-04: do this machine's valves have an internal SN the controller can report?
- **Q12** PD-13 and PD-14: do capture and calibration results come through the PLC
  (`ST_CalibrationStatus.aResult`) or from the vision software?
- **Q13** Dry run: the SI code, and whether the two DLLs may be redistributed with the HMI.
- **Carried over** `HiveLimitsVersion` value; upload timing (2 s x 3 or 3 s x 1);
  `AlarmPopupCloseLevel` (any operator or Level 2 / 3).
