using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="IAccessControlService"/> through MES - Access Control V5.1, API
    /// G_MACHINE_ACCESS_BY_USERNO.
    /// </summary>
    /// <remarks>
    /// serial_Number is <c>CardUID,Type,Project,Station_ID</c>, where Type is the software
    /// type for a sign-in ("Main" / "Vision"), "Authorize" for the second step of two-step
    /// verification, and "Close" when the operator signs out. MES answers
    /// <c>{"Result":true,"RetMsg":"Name=San.Zhang,Access_Level=1,Function=ICT_DFX"}</c>
    /// or Result false with the reason.
    /// </remarks>
    public sealed class MesAccessControlService : IAccessControlService
    {
        public const string Command = "G_MACHINE_ACCESS_BY_USERNO";

        private readonly IMesClient _mes;
        private readonly IAccessControlAdmin _admin;
        private readonly Func<string> _stationId;

        public MesAccessControlService(IMesClient mes, IAccessControlAdmin admin, string softwareType, Func<string> stationId)
        {
            _mes = mes;
            _admin = admin;
            _stationId = stationId;
            SoftwareType = string.IsNullOrWhiteSpace(softwareType) ? "Main" : softwareType.Trim();
        }

        public string TerminalName
        {
            get { return _mes.TerminalName; }
        }

        public string SoftwareType { get; }

        public Task<AccessCheckResult> CheckAccessAsync(string cardUid)
        {
            return AskAsync(cardUid, SoftwareType);
        }

        public Task<AccessCheckResult> CheckApproverAsync(string cardUid)
        {
            return AskAsync(cardUid, JsonAccessControlService.AuthorizeSoftwareType);
        }

        /// <summary>Tells MES the operator signed out ("Close"). The answer is only logged.</summary>
        public Task NotifySignOutAsync(string cardUid)
        {
            if (string.IsNullOrWhiteSpace(cardUid))
                return Task.FromResult(0);

            return _mes.GetCmdResultAsync(Command, SerialNumber(cardUid, "Close"));
        }

        private async Task<AccessCheckResult> AskAsync(string cardUid, string type)
        {
            if (string.IsNullOrWhiteSpace(cardUid))
                return AccessCheckResult.Deny("No card was read.");

            if (string.IsNullOrWhiteSpace(TerminalName))
                return AccessCheckResult.Deny("This terminal has no terminal name configured, so access cannot be checked.");

            MesResult answer = await _mes.GetCmdResultAsync(Command, SerialNumber(cardUid, type)).ConfigureAwait(false);
            if (!answer.Reached)
                return AccessCheckResult.Deny(answer.Message);

            if (!answer.Ok)
                return AccessCheckResult.Deny("MES refused the badge: " + answer.Message);

            Dictionary<string, string> fields = ParseFields(answer.Message);
            string name, levelText, function;
            fields.TryGetValue("Name", out name);
            fields.TryGetValue("Access_Level", out levelText);
            fields.TryGetValue("Function", out function);

            int levelNumber;
            AccessLevel level = int.TryParse(levelText, out levelNumber)
                ? JsonAccessControlService.ToLevel(levelNumber)
                : AccessLevel.None;

            // The flow on V5.1 page 8: an access level of "NULL" is a refusal.
            if (level == AccessLevel.None)
                return AccessCheckResult.Deny("MES returned no access level for this badge (" + answer.Message + ").");

            return AccessCheckResult.Allow(
                string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim(),
                level,
                function,
                OrganizationOf(function),
                cardUid.Trim());
        }

        private string SerialNumber(string cardUid, string type)
        {
            AccessControlSettings settings = _admin.Settings;
            string station = _stationId != null ? _stationId() : string.Empty;
            return cardUid.Trim() + "," + type + "," + (settings.ProjectCode ?? string.Empty) + "," + (station ?? string.Empty);
        }

        /// <summary>
        /// MES returns a function team but no organisation. The two-step rule needs one,
        /// so it is read from the function name with the prefixes in the settings.
        /// </summary>
        private AccessOrganization OrganizationOf(string function)
        {
            if (string.IsNullOrWhiteSpace(function))
                return AccessOrganization.Unknown;

            AccessControlSettings settings = _admin.Settings;
            foreach (string prefix in settings.AppleFunctionPrefixes ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(prefix) && function.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return AccessOrganization.Apple;
            }

            foreach (string prefix in settings.CmFunctionPrefixes ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(prefix) && function.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return AccessOrganization.CM;
            }

            // Everyone else on the machine is the vendor's own team, e.g. "Vendor_OSS".
            return AccessOrganization.SI;
        }

        private static Dictionary<string, string> ParseFields(string message)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (message ?? string.Empty).Split(','))
            {
                int equals = part.IndexOf('=');
                if (equals > 0)
                    fields[part.Substring(0, equals).Trim()] = part.Substring(equals + 1).Trim();
            }

            return fields;
        }
    }
}
