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

                if (dialog != null)
                    dialog.Info = NozzleInfo(gantry);

                return new List<ProcedureField>
                {
                    new ProcedureField("nozzle", "nozzle SN", gantry.NozzleSn)
                    {
                        MinimumLength = 6,
                        MaximumLength = 60,
                        Validate = sn => string.IsNullOrWhiteSpace(gantry.ValveSn)
                            ? "No valve SN is recorded for this gantry. Do a Valve Change (PD-04) first."
                            : null,
                    },
                };
            })
            {
                Owner = Application.Current.MainWindow,
                CheckLabel = "To MES Check",
            };
            dialog.Info = NozzleInfo(Gantry(dialog.SelectedGantry));
            dialog.CheckAction = CheckNozzleAsync;
            dialog.OkAction = d =>
            {
                GantryConsumables gantry = Gantry(d.SelectedGantry);
                string sn = d.ValueOf("nozzle");
                string oldSn = gantry.NozzleSn;
                gantry.NozzleSn = sn;
                gantry.NozzleChanged = DateTime.Now;

                string error;
                if (!_store.SaveGantry(gantry, out error))
                    return Task.FromResult(error);

                d.Tag = "Nozzle change on the " + gantry.Gantry + " gantry: " + (string.IsNullOrWhiteSpace(oldSn) ? "(none)" : oldSn)
                    + " to " + sn + ", linked to valve " + gantry.ValveSn;
                return Task.FromResult<string>(null);
            };

            if (dialog.ShowDialog() != true)
                return null;

            return Outcome(reason, dialog, (string)dialog.Tag);
        }

        /// <summary>
        /// S3.3: link the nozzle to the valve (UPDATE_RUBBER_BODY_SN), then check its health
        /// (GET_RUBBER_VALVE_STATUS). OK is enabled only when MES returns true.
        /// </summary>
        private async Task<string> CheckNozzleAsync(ProcedureDialog dialog)
        {
            GantryConsumables gantry = Gantry(dialog.SelectedGantry);
            string nozzle = dialog.ValueOf("nozzle");
            if (gantry == null || nozzle == null)
                return "Scan the nozzle SN first.";

            if (_mes == null || !_mes.IsConfigured)
            {
                dialog.ShowMessage("MES is not configured on this machine: the nozzle is recorded without the MES check.", false);
                Logger.WriteLog(LogType.Info, "Hive", "PD-12", "MES is not configured - nozzle " + nozzle + " recorded without the MES check.", "");
                return null;
            }

            string station = "$Station_Id=" + _mes.TerminalName;
            MesResult link = await _mes.GetCmdResultAsync("UPDATE_RUBBER_BODY_SN",
                "RubberSn=" + nozzle + "$RubberBodySn=" + gantry.ValveSn + station);
            if (!link.Reached || !link.Ok)
                return "MES check NG - the nozzle was not linked to the valve: " + link.Message;

            string gate = string.Equals(gantry.Gantry, "Rear", StringComparison.OrdinalIgnoreCase) ? "Back" : "Front";
            MesResult status = await _mes.GetCmdResultAsync("GET_RUBBER_VALVE_STATUS", "RubberSn=" + nozzle + "$Gate=" + gate + station);
            if (!status.Reached || !status.Ok)
                return "MES check NG: " + status.Message;

            dialog.ShowMessage("MES check OK: " + status.Message, false);
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
                    "The machine is in PD-15. A Level 3 user now finds the Hash Key tab on the Setting page.",
                    "PD-15 Uploaded Hash Keys to MES", MessageBoxButton.OK, MessageBoxImage.Information),
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

        private static string NozzleInfo(GantryConsumables gantry)
        {
            if (gantry == null)
                return null;

            return string.IsNullOrWhiteSpace(gantry.ValveSn)
                ? "No valve SN is recorded for the " + gantry.Gantry + " gantry, so the nozzle cannot be linked. Do a Valve Change (PD-04) first."
                : "Valve SN to link the nozzle to: " + gantry.ValveSn;
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
            MessageBox.Show(Application.Current.MainWindow, reason.Label + " needs the consumables file, which is not available.",
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
