using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Hive
{
    /// <summary>What a procedure produced: who applies the downtime, and what HIVE is told.</summary>
    public sealed class ProcedureOutcome
    {
        /// <summary>The badge sent as u_id.</summary>
        public string OperatorId { get; set; }

        /// <summary>The planned downtime error, with the procedure's detail.</summary>
        public HiveError Error { get; set; }

        /// <summary>Runs once the state change was accepted, e.g. to start an audit run.</summary>
        public Action AfterApplied { get; set; }
    }

    /// <summary>
    /// The procedures behind the planned-downtime codes that have one (Audio SW DFM v5.4
    /// S1.4 and S3): the operator is walked through them before the HIVE state changes,
    /// and what they record is kept in the consumables file.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>PD-02 Glue Change / PD-08 Material Replacement: scan the new glue / material SN (slide 37).</item>
    /// <item>PD-04 Valve Change: re-badge, scan the new valve SN; MES gives the standard SN and the
    /// valve's usage (S3.1). On an MES error nothing is recorded and HIVE is not changed.</item>
    /// <item>PD-12 Nozzle Change: scan the nozzle SN, then "To MES Check" links it to the gantry's
    /// valve and checks its health; OK only after MES says true (S3.3).</item>
    /// <item>PD-11 LAD, PD-13 CCD Repeatability, PD-14 Dispensing Pose Calibration: choose the
    /// analysis (and the repeatability type), then the audit run starts (slides 68-74).</item>
    /// <item>PD-15 Uploaded Hash Keys to MES: a Level 3 badge; the Hash Key tab then shows on the
    /// Setting page (slide 80).</item>
    /// </list>
    /// With no MES configured the MES steps are skipped and written to the machine log, so a
    /// machine on a desk can still go through them; with MES configured, a refusal stops them.
    /// </remarks>
    public sealed class PlannedDowntimeProcedures
    {
        /// <summary>The valve models whose SN the software checks (S3.1.1).</summary>
        private static readonly string[] CheckedValveModels = { "EFD-PICO", "ANDA-PV30A", "KOTL-PH30" };

        /// <summary>XXX-YYY-001_YYMMDD (slide 18).</summary>
        private static readonly Regex StandardValveSn = new Regex(@"^[^_\s]+-[^_\s]+-[^_\s]+_\d{6}$");

        private readonly IAccessControlService _access;
        private readonly IConsumableStore _store;
        private readonly IMesClient _mes;
        private readonly HiveService _hive;

        public PlannedDowntimeProcedures(IAccessControlService access, IConsumableStore store, IMesClient mes, HiveService hive)
        {
            _access = access;
            _store = store;
            _mes = mes;
            _hive = hive;
        }

        /// <summary>True when an MES address is configured: the machine is "with MES" (PD-05 is for non-MES runs only).</summary>
        public bool IsMesConfigured
        {
            get { return _mes != null && _mes.IsConfigured; }
        }

        /// <summary>True when this code has its own procedure here.</summary>
        public static bool HasProcedure(string code)
        {
            switch (code)
            {
                case "PD-02":
                case "PD-04":
                case "PD-08":
                case "PD-11":
                case "PD-12":
                case "PD-13":
                case "PD-14":
                case "PD-15":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Runs the procedure; null when the operator cancelled or it could not finish.</summary>
        public ProcedureOutcome Run(PlannedDowntimeReason reason)
        {
            switch (reason.Code)
            {
                case "PD-02": return GlueChange(reason);
                case "PD-08": return MaterialReplacement(reason);
                case "PD-04": return ValveChange(reason);
                case "PD-12": return NozzleChange(reason);
                case "PD-11": return Audit(reason, null, AccessLevel.Level2);
                case "PD-13": return CcdRepeatability(reason);
                case "PD-14": return Audit(reason, null, AccessLevel.Level3);
                case "PD-15": return HashKeys(reason);
                default: return null;
            }
        }

        // ------------------------------------------------------------------
        // PD-02, PD-08: scan the SN in (slide 37)
        // ------------------------------------------------------------------

        private ProcedureOutcome GlueChange(PlannedDowntimeReason reason)
        {
            if (_store == null)
                return NotAvailable(reason);

            List<GantryConsumables> gantries = _store.Gantries.ToList();
            var dialog = new ProcedureDialog(reason.Label, _access, AccessLevel.Level2, null, gantry =>
                gantries.Select(g => new ProcedureField(g.Gantry, g.Gantry + " glue SN", g.GlueSn)
                {
                    Required = false,
                    MinimumLength = 10,
                    MaximumLength = 80,
                }).Cast<ProcedureField>().ToList())
            {
                AtLeastOne = true,
                Owner = Application.Current.MainWindow,
            };
            dialog.Info = "Scan the new cartridge SN of each gantry whose glue was changed. purge_idx goes back to 0 for it.";
            dialog.OkAction = d =>
            {
                var changed = new List<string>();
                foreach (GantryConsumables gantry in gantries)
                {
                    string sn = d.ValueOf(gantry.Gantry);
                    if (sn == null)
                        continue;

                    gantry.GlueSn = sn;
                    gantry.GlueChanged = DateTime.Now;
                    gantry.PurgeIndexResetPending = true;

                    string error;
                    if (!_store.SaveGantry(gantry, out error))
                        return Task.FromResult(error);

                    changed.Add(gantry.Gantry + " glue SN " + sn);
                }

                d.Tag = string.Join(", ", changed);
                return Task.FromResult<string>(null);
            };

            if (dialog.ShowDialog() != true)
                return null;

            return Outcome(reason, dialog, "Glue change: " + dialog.Tag);
        }

        private ProcedureOutcome MaterialReplacement(PlannedDowntimeReason reason)
        {
            if (_store == null)
                return NotAvailable(reason);

            IReadOnlyDictionary<string, string> current = _store.Materials;
            var dialog = new ProcedureDialog(reason.Label, _access, AccessLevel.Level2, null, gantry =>
                _store.MaterialNames.Select(name =>
                {
                    string old;
                    current.TryGetValue(name, out old);
                    return new ProcedureField(name, name + " SN", old ?? string.Empty) { Required = false };
                }).ToList())
            {
                AtLeastOne = true,
                Owner = Application.Current.MainWindow,
            };
            dialog.OkAction = d =>
            {
                var materials = d.Fields.Where(f => f.Value != null).ToDictionary(f => f.Key, f => f.Value);
                string error;
                if (!_store.SaveMaterials(materials, out error))
                    return Task.FromResult(error);

                d.Tag = string.Join(", ", materials.Select(m => m.Key + " SN " + m.Value));
                return Task.FromResult<string>(null);
            };

            if (dialog.ShowDialog() != true)
                return null;

            return Outcome(reason, dialog, "Material replacement: " + dialog.Tag);
        }

        // ------------------------------------------------------------------
        // PD-04 Valve Change (S3.1)
        // ------------------------------------------------------------------

        private ProcedureOutcome ValveChange(PlannedDowntimeReason reason)
        {
            if (_store == null)
                return NotAvailable(reason);

            List<string> gantryNames = _store.Gantries.Select(g => g.Gantry).ToList();
            ProcedureDialog dialog = null;
            dialog = new ProcedureDialog(reason.Label, _access, AccessLevel.Level2, gantryNames, name =>
            {
                GantryConsumables gantry = Gantry(name);
                if (gantry == null)
                    return new List<ProcedureField>();

                ValveModel model = _store.ValveModels.FirstOrDefault(m => m.Name == gantry.ValveModel);
                if (dialog != null)
                    dialog.Info = ValveInfo(gantry);

                return new List<ProcedureField>
                {
                    new ProcedureField("valve", "valve SN", gantry.ValveSn)
                    {
                        MinimumLength = 4,
                        MaximumLength = 60,
                        Validate = sn => CheckValveSn(gantry, model, sn),
                    },
                };
            })
            {
                Owner = Application.Current.MainWindow,
            };
            dialog.Info = ValveInfo(Gantry(dialog.SelectedGantry));
            dialog.OkAction = d => RecordValveAsync(d);

            if (dialog.ShowDialog() != true)
                return null;

            return Outcome(reason, dialog, (string)dialog.Tag);
        }

        /// <summary>S3.1.1: the scanned SN has to belong to the valve model chosen on the Setting page.</summary>
        private static string CheckValveSn(GantryConsumables gantry, ValveModel model, string sn)
        {
            if (string.IsNullOrWhiteSpace(gantry.ValveModel))
                return "No valve model is set for this gantry. Choose it on Setting > Valve Model first.";

            if (model != null && model.InternalSn)
                return null;

            string upper = sn.ToUpperInvariant();
            bool known = CheckedValveModels.Contains(gantry.ValveModel, StringComparer.OrdinalIgnoreCase);
            bool mismatch = known
                ? upper.IndexOf(gantry.ValveModel.ToUpperInvariant(), StringComparison.Ordinal) < 0
                : CheckedValveModels.Any(m => upper.IndexOf(m, StringComparison.Ordinal) >= 0);
            if (mismatch)
                return "Valve model selection error, pls select the correct valve model on the settings page.";

            if (!StandardValveSn.IsMatch(sn))
                return "Not a standard valve SN (XXX-YYY-001_YYMMDD). Please scan again.";

            return null;
        }

        private async Task<string> RecordValveAsync(ProcedureDialog dialog)
        {
            GantryConsumables gantry = Gantry(dialog.SelectedGantry);
            string scanned = dialog.ValueOf("valve");
            if (gantry == null || scanned == null)
                return "Choose the gantry and scan the valve SN.";

            ValveModel model = _store.ValveModels.FirstOrDefault(m => m.Name == gantry.ValveModel);
            string standardSn = scanned;
            int units = 0, cycles = 0, seconds = 0;
            string note = string.Empty;

            if (_mes != null && _mes.IsConfigured)
            {
                if (model != null && model.InternalSn)
                {
                    // S3.1.2: MES turns the internal SN into the standard one.
                    MesResult sn = await _mes.GetCmdResultAsync("GET_RUBBERBODY_SN", scanned);
                    if (!sn.Reached || !sn.Ok)
                        return "MES could not give the standard valve SN: " + sn.Message;

                    standardSn = Field(sn.Message, "RubberBodySn") ?? sn.Message;
                }

                MesResult usage = await _mes.GetCmdResultAsync("GET_RUBBERBODY_USED_INFO", standardSn);
                if (!usage.Reached || !usage.Ok)
                    return "MES could not give the valve's usage: " + usage.Message;

                int.TryParse(Field(usage.Message, "Units_Produced"), out units);
                int.TryParse(Field(usage.Message, "Valve_Cycles"), out cycles);
                int.TryParse(Field(usage.Message, "Total_Dispense_Time"), out seconds);
            }
            else
            {
                note = " (MES not configured: the SN was not checked with MES)";
                Logger.WriteLog(LogType.Info, "Hive", "PD-04", "MES is not configured - valve SN " + scanned + " recorded without the MES check.", "");
            }

            string oldSn = gantry.ValveSn;
            gantry.ValveSn = standardSn;

            // The controller's own SN, which the 5 minute check compares against (S3.1 point 4).
            gantry.ValveInternalSn = model != null && model.InternalSn ? scanned : string.Empty;
            gantry.ValveChanged = DateTime.Now;
            gantry.ValveUnitsProduced = units;
            gantry.ValveCycles = cycles;
            gantry.ValveDispenseSeconds = seconds;

            string error;
            if (!_store.SaveGantry(gantry, out error))
                return error;

            dialog.Tag = "Valve change on the " + gantry.Gantry + " gantry: " + (string.IsNullOrWhiteSpace(oldSn) ? "(none)" : oldSn)
                + " to " + standardSn + note;
            return null;
        }

        // ------------------------------------------------------------------
        // PD-12 Nozzle Change (S3.3)
        // ------------------------------------------------------------------

        private ProcedureOutcome NozzleChange(PlannedDowntimeReason reason)
        {
            if (_store == null)
                return NotAvailable(reason);

            List<string> gantryNames = _store.Gantries.Select(g => g.Gantry).ToList();
            ProcedureDialog dialog = null;
            dialog = new ProcedureDialog(reason.Label, _access, AccessLevel.Level2, gantryNames, name =>
            {
                GantryConsumables gantry = Gantry(name);
                if (gantry == null)
                    return new List<ProcedureField>();

                ValveModel model = ModelOf(gantry);
                if (dialog != null)
                    dialog.Info = NozzleInfo(gantry, model);

                var fields = new List<ProcedureField>
                {
                    // "The first change needle/tunnel after EIH, old SN use 0000" (S3.3 2.1.2).
                    // A nozzle or runner with no physical QR label is the one SN that may be typed.
                    new ProcedureField("nozzle", "nozzle SN", gantry.NozzleSn)
                    {
                        MinimumLength = 4,
                        MaximumLength = 60,
                        AllowTyping = true,
                        OldValueWhenNone = "0000",
                        Validate = sn => string.IsNullOrWhiteSpace(gantry.ValveSn)
                            ? "No valve SN is recorded for this gantry. Do a Valve Change (PD-04) first."
                            : null,
                    },
                };

                // Valve double confirmation: a valve with a QR label is scanned again and has to
                // be the one the machine recorded. A valve with an internal SN has no label; its
                // standard SN is read from the configuration file (S3.3.2).
                if (model == null || !model.InternalSn)
                {
                    fields.Add(new ProcedureField("valve", "valve SN")
                    {
                        NewLabel = "Valve SN (scan to confirm)",
                        MinimumLength = 4,
                        MaximumLength = 60,
                        Validate = sn => string.Equals(sn.Trim(), (gantry.ValveSn ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
                            ? null
                            : "This is not the valve recorded for the " + gantry.Gantry + " gantry (" + gantry.ValveSn
                              + "). Nothing was sent to MES. Scan the valve fitted on this gantry, or do a Valve Change (PD-04).",
                    });
                }

                return fields;
            })
            {
                Owner = Application.Current.MainWindow,
                CheckLabel = "To MES Check",
            };
            dialog.Info = NozzleInfo(Gantry(dialog.SelectedGantry), ModelOf(Gantry(dialog.SelectedGantry)));

            NozzleHealth health = null;
            dialog.CheckAction = async d =>
            {
                health = null;
                var checkedHealth = new NozzleHealth();
                string failed = await CheckNozzleAsync(d, checkedHealth);
                if (failed == null)
                    health = checkedHealth;
                return failed;
            };
            dialog.OkAction = d =>
            {
                GantryConsumables gantry = Gantry(d.SelectedGantry);
                ProcedureField nozzle = d.Fields.FirstOrDefault(f => f.Key == "nozzle");
                string sn = d.ValueOf("nozzle");
                if (gantry == null || sn == null)
                    return Task.FromResult("Choose the gantry and enter the nozzle SN.");

                string oldSn = gantry.NozzleSn;
                gantry.NozzleSn = sn;
                gantry.NozzleChanged = DateTime.Now;

                // The first unit after the change: MES is asked for the nozzle's health again
                // before the machine runs (S3.3 "Input of the first product").
                gantry.NozzleCheckPending = true;
                if (health != null && health.Checked)
                {
                    gantry.NozzleMaintainHours = health.MaintainHours;
                    gantry.NozzleRemainingHours = health.RemainingHours;
                    gantry.NozzleHealthChecked = DateTime.Now;
                }
                else
                {
                    // A new nozzle, and MES gave nothing for it: the old nozzle's health is not its.
                    gantry.NozzleMaintainHours = null;
                    gantry.NozzleRemainingHours = null;
                    gantry.NozzleHealthChecked = null;
                }

                string error;
                if (!_store.SaveGantry(gantry, out error))
                    return Task.FromResult(error);

                bool typed = nozzle != null && nozzle.WasTyped;
                if (typed)
                    Logger.WriteLog(LogType.Info, "Hive", "PD-12", "Nozzle SN " + sn + " of the " + gantry.Gantry + " gantry was typed (no QR label).", "");

                d.Tag = "Nozzle change on the " + gantry.Gantry + " gantry: " + (string.IsNullOrWhiteSpace(oldSn) ? "0000" : oldSn)
                    + " to " + sn + (typed ? " (typed, no QR label)" : "") + ", linked to valve " + gantry.ValveSn;
                return Task.FromResult<string>(null);
            };

            if (dialog.ShowDialog() != true)
                return null;

            return Outcome(reason, dialog, (string)dialog.Tag);
        }

        /// <summary>MAINTAIN_TIME and REMAINING_TIME of one GET_RUBBER_VALVE_STATUS answer, hours.</summary>
        public sealed class NozzleHealth
        {
            /// <summary>True when MES answered true and gave a remaining time.</summary>
            public bool Checked { get; set; }

            public double? MaintainHours { get; set; }

            public double? RemainingHours { get; set; }
        }

        /// <summary>
        /// S3.3, in the spec's order: MES checks the nozzle and gives its health
        /// (GET_RUBBER_VALVE_STATUS), the valve fitted is confirmed to be the recorded one
        /// (the dialog's valve scan, or the configuration file for an internal SN), and only
        /// then is the nozzle linked to the valve (UPDATE_RUBBER_BODY_SN). OK is enabled only
        /// when MES returns true to both.
        /// </summary>
        private async Task<string> CheckNozzleAsync(ProcedureDialog dialog, NozzleHealth health)
        {
            GantryConsumables gantry = Gantry(dialog.SelectedGantry);
            string nozzle = dialog.ValueOf("nozzle");
            if (gantry == null || nozzle == null)
                return "Enter the nozzle SN first.";

            if (string.IsNullOrWhiteSpace(gantry.ValveSn))
                return "The valve SN could not be read from the configuration file. Do a Valve Change (PD-04) first.";

            // 5.1: "When the info is inconsistent, the machine does not send a link request".
            ValveModel model = ModelOf(gantry);
            if (model == null || !model.InternalSn)
            {
                string confirmed = dialog.ValueOf("valve");
                if (confirmed == null || !string.Equals(confirmed.Trim(), gantry.ValveSn.Trim(), StringComparison.OrdinalIgnoreCase))
                    return "Scan the valve SN to confirm it first.";
            }

            if (_mes == null || !_mes.IsConfigured)
            {
                dialog.ShowMessage("MES is not configured on this machine: the nozzle is recorded without the MES check.", false);
                Logger.WriteLog(LogType.Info, "Hive", "PD-12", "MES is not configured - nozzle " + nozzle + " recorded without the MES check.", "");
                return null;
            }

            MesResult status = await NozzleStatusAsync(gantry.Gantry, nozzle);
            if (!status.Reached || !status.Ok)
                return "MES check NG: " + status.Message;

            ReadHealth(status.Message, health);

            MesResult link = await _mes.GetCmdResultAsync("UPDATE_RUBBER_BODY_SN",
                "RubberSn=" + nozzle + "$RubberBodySn=" + gantry.ValveSn + "$Station_Id=" + _mes.TerminalName);
            if (!link.Reached || !link.Ok)
                return "MES check NG - the nozzle was not linked to the valve: " + link.Message;

            dialog.ShowMessage("MES check OK: " + status.Message + " / " + link.Message, false);
            return null;
        }

        /// <summary>GET_RUBBER_VALVE_STATUS: RubberSn, Gate (Front / Back) and Station_Id.</summary>
        private Task<MesResult> NozzleStatusAsync(string gantryName, string nozzleSn)
        {
            string gate = string.Equals(gantryName, "Rear", StringComparison.OrdinalIgnoreCase) ? "Back" : "Front";
            return _mes.GetCmdResultAsync("GET_RUBBER_VALVE_STATUS",
                "RubberSn=" + nozzleSn + "$Gate=" + gate + "$Station_Id=" + _mes.TerminalName);
        }

        /// <summary>"OK:CHECK_OK,MAINTAIN_TIME=168,REMAINING_TIME=75".</summary>
        private static void ReadHealth(string message, NozzleHealth health)
        {
            double maintain, remaining;
            if (double.TryParse(Field(message, "MAINTAIN_TIME"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out maintain))
                health.MaintainHours = maintain;

            if (double.TryParse(Field(message, "REMAINING_TIME"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out remaining))
            {
                health.RemainingHours = remaining;
                health.Checked = true;
            }
        }

        /// <summary>
        /// The health check before the first unit after a nozzle change (S3.3 "MES check
        /// Nozzle / tunnel Health status data (Input of the first product)"): asks MES for every
        /// gantry whose nozzle was changed since the last check and records the answer.
        /// </summary>
        /// <returns>Null when the machine may run; otherwise why not, with MES's own text.</returns>
        public async Task<string> CheckChangedNozzlesAsync()
        {
            if (_store == null)
                return null;

            foreach (GantryConsumables gantry in _store.Gantries.Where(g => g.NozzleCheckPending).ToList())
            {
                if (_mes == null || !_mes.IsConfigured || string.IsNullOrWhiteSpace(gantry.NozzleSn))
                {
                    Logger.WriteLog(LogType.Info, "Hive", "PD-12", "MES is not configured - the health of the " + gantry.Gantry
                        + " gantry's new nozzle was not checked before the first unit.", "");
                    gantry.NozzleCheckPending = false;
                }
                else
                {
                    MesResult status = await NozzleStatusAsync(gantry.Gantry, gantry.NozzleSn).ConfigureAwait(false);
                    if (!status.Reached)
                    {
                        // No answer is not a verdict on the nozzle: asked again at the next switch to Running.
                        Logger.WriteLog(LogType.Error, "Hive", "PD-12", "[Error/Alert] MES did not answer the health check of nozzle "
                            + gantry.NozzleSn + " (" + gantry.Gantry + " gantry): " + status.Message, "");
                        continue;
                    }

                    if (!status.Ok)
                        return "Nozzle " + gantry.NozzleSn + " on the " + gantry.Gantry + " gantry failed MES's health check: " + status.Message;

                    var health = new NozzleHealth();
                    ReadHealth(status.Message, health);
                    if (health.Checked)
                    {
                        gantry.NozzleMaintainHours = health.MaintainHours;
                        gantry.NozzleRemainingHours = health.RemainingHours;
                        gantry.NozzleHealthChecked = DateTime.Now;
                    }

                    if (health.RemainingHours.HasValue && health.RemainingHours.Value <= 0)
                    {
                        string ignored;
                        _store.SaveGantry(gantry, out ignored);
                        return "Nozzle " + gantry.NozzleSn + " on the " + gantry.Gantry + " gantry has no health left (REMAINING_TIME "
                            + health.RemainingHours.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " h).";
                    }

                    gantry.NozzleCheckPending = false;
                }

                string error;
                if (!_store.SaveGantry(gantry, out error))
                    Logger.WriteLog(LogType.Error, "Hive", "PD-12", "The nozzle health of the " + gantry.Gantry + " gantry could not be saved: " + error, "");
            }

            return null;
        }

        // ------------------------------------------------------------------
        // PD-11, PD-13, PD-14: audit runs (slides 68-75)
        // ------------------------------------------------------------------

        private ProcedureOutcome CcdRepeatability(PlannedDowntimeReason reason)
        {
            int type = ChoiceDialog.Ask(reason.Label, "Select the repeatability type.",
                new Choice("Static repeatability (Part/UC and CCD are held static, 100x times)", true),
                new Choice("Dynamic repeatability_1 (CCD is held static, 100x times)"),
                new Choice("Dynamic repeatability_2 (UC is held static, 100x times)"));
            if (type < 0)
                return null;

            string repeatability = type == 0 ? "static" : type == 1 ? "dynamic_1" : "dynamic_2";
            return Audit(reason, repeatability, AccessLevel.Level2);
        }

        private ProcedureOutcome Audit(PlannedDowntimeReason reason, string repeatability, AccessLevel minimumLevel)
        {
            int analysis = ChoiceDialog.Ask(reason.Label, "Select the analysis type.",
                new Choice(AuditSession.Cpk, true), new Choice(AuditSession.Grr), new Choice(AuditSession.Scs));
            if (analysis < 0)
                return null;

            string chosen = analysis == 0 ? AuditSession.Cpk : analysis == 1 ? AuditSession.Grr : AuditSession.Scs;

            // S3.5: the pose calibration is uploaded under a test SN, scanned in here with the
            // badge ("Use scanner to auto fill in SN. Manually key in is NOT allowed").
            if (reason.Code == AuditSession.PoseCalibrationCode)
                return PoseCalibration(reason, chosen, minimumLevel);

            var badge = new OperatorDialog(reason.Code + ": " + reason.Name + " - machine will switch to audit mode",
                _access, minimumLevel)
            {
                Owner = Application.Current.MainWindow,
            };
            if (badge.ShowDialog() != true)
                return null;

            var session = new AuditSession(reason.Code, chosen, repeatability, DateTime.Now);
            return new ProcedureOutcome
            {
                OperatorId = badge.OperatorId,
                Error = reason.ToError(reason.Name + " - " + chosen + (repeatability != null ? ", " + repeatability : "")
                    + ", TestSeriesID " + session.TestSeriesId),
                AfterApplied = () => _hive.StartAudit(session),
            };
        }

        private ProcedureOutcome PoseCalibration(PlannedDowntimeReason reason, string analysis, AccessLevel minimumLevel)
        {
            var dialog = new ProcedureDialog(reason.Code + ": " + reason.Name + " - machine will switch to audit mode",
                _access, minimumLevel, null, gantry => new List<ProcedureField>
                {
                    new ProcedureField("unit", "test SN")
                    {
                        NewLabel = "Scanning SN",
                        MinimumLength = 4,
                        MaximumLength = 80,
                    },
                })
            {
                Owner = Application.Current.MainWindow,
            };
            dialog.Info = "Scan the test SN (virtual work order) the calibration results are uploaded under.";

            if (dialog.ShowDialog() != true)
                return null;

            var session = new AuditSession(reason.Code, analysis, null, DateTime.Now) { TestSn = dialog.ValueOf("unit") };
            return new ProcedureOutcome
            {
                OperatorId = dialog.OperatorId,
                Error = reason.ToError(reason.Name + " - " + analysis + ", TestSeriesID " + session.TestSeriesId + ", test SN " + session.TestSn),
                AfterApplied = () => _hive.StartAudit(session),
            };
        }

        // ------------------------------------------------------------------
        // PD-15 Uploaded Hash Keys to MES (slides 77-80)
        // ------------------------------------------------------------------

        private ProcedureOutcome HashKeys(PlannedDowntimeReason reason)
        {
            var badge = new OperatorDialog(reason.Label, _access, AccessLevel.Level3)
            {
                Owner = Application.Current.MainWindow,
            };
            if (badge.ShowDialog() != true)
                return null;

            return new ProcedureOutcome
            {
                OperatorId = badge.OperatorId,
                Error = reason.ToError("Hash Key check with MES"),
                AfterApplied = () => MessageBox.Show(Application.Current.MainWindow,
                    HiveText.T("The machine is in PD-15. A Level 3 user now finds the Hash Key tab on the Setting page."),
                    HiveText.T("PD-15 Uploaded Hash Keys to MES"), MessageBoxButton.OK, MessageBoxImage.Information),
            };
        }

        // ------------------------------------------------------------------

        private ValveModel ModelOf(GantryConsumables gantry)
        {
            return gantry == null ? null : _store.ValveModels.FirstOrDefault(m => m.Name == gantry.ValveModel);
        }

        private string ValveInfo(GantryConsumables gantry)
        {
            if (gantry == null)
                return null;

            if (string.IsNullOrWhiteSpace(gantry.ValveModel))
                return "No valve model is set for the " + gantry.Gantry + " gantry. Choose it on Setting > Valve Model (Level 3) first.";

            ValveModel model = ModelOf(gantry);
            return "Valve model: " + gantry.ValveModel + (model != null && model.InternalSn
                ? " - scan the valve's internal SN; MES returns its standard SN."
                : "");
        }

        private static string NozzleInfo(GantryConsumables gantry, ValveModel model)
        {
            if (gantry == null)
                return null;

            if (string.IsNullOrWhiteSpace(gantry.ValveSn))
                return "No valve SN is recorded for the " + gantry.Gantry + " gantry, so the nozzle cannot be linked. Do a Valve Change (PD-04) first.";

            return model != null && model.InternalSn
                ? "Valve SN to link the nozzle to (from the configuration file): " + gantry.ValveSn
                : "Scan the valve fitted on the gantry to confirm it is the recorded one, then To MES Check.";
        }

        private GantryConsumables Gantry(string name)
        {
            return _store.Gantries.FirstOrDefault(g => string.Equals(g.Gantry, name, StringComparison.OrdinalIgnoreCase));
        }

        private static ProcedureOutcome Outcome(PlannedDowntimeReason reason, ProcedureDialog dialog, string detail)
        {
            return new ProcedureOutcome
            {
                OperatorId = dialog.OperatorId,
                Error = reason.ToError(detail),
            };
        }

        private static ProcedureOutcome NotAvailable(PlannedDowntimeReason reason)
        {
            MessageBox.Show(Application.Current.MainWindow, HiveText.T(reason.Label + " needs the consumables file, which is not available."),
                "HIVE", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        /// <summary>"Units_Produced" out of "OK:Units_Produced=0,Valve_Cycles=0" (or "RubberBodySn=..." ).</summary>
        private static string Field(string message, string name)
        {
            foreach (string part in (message ?? string.Empty).Split(',', ':'))
            {
                int equals = part.IndexOf('=');
                if (equals > 0 && string.Equals(part.Substring(0, equals).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return part.Substring(equals + 1).Trim();
            }

            return null;
        }
    }
}
