using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="IAccessControlService"/> backed by the local access list (Audio SW DFM
    /// v5.4 slide 52; Access Control V5.1 S1.2.3 "Access Control with Local DB"), read
    /// from <c>Data\AccessDatabase.json</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used when "Access Control with MES" is off. <see cref="RoutedAccessControlService"/>
    /// picks between this and <see cref="MesAccessControlService"/>.
    /// </para>
    /// <para>
    /// The matching rules are the slide's, not looser ones: a record has to match the
    /// card UID, THIS terminal's name and THIS software type. A badge valid on the
    /// next station over, or on the vision software, is refused here - which is the
    /// point of sending all three to MES rather than just the UID.
    /// </para>
    /// <para>
    /// The file is re-read on every check rather than cached. Access is revoked by
    /// editing that file, and a cache would keep a removed badge working until the
    /// next restart.
    /// </para>
    /// </remarks>
    public sealed class JsonAccessControlService : IAccessControlService
    {
        public const string FileName = "AccessDatabase.json";

        /// <summary>V5.1: the software type of a record that may approve a two-step verification.</summary>
        public const string AuthorizeSoftwareType = "Authorize";

        private readonly string _path;

        public string TerminalName { get; }

        public string SoftwareType { get; }

        public JsonAccessControlService(string dataDirectory, string terminalName, string softwareType)
        {
            _path = Path.Combine(dataDirectory ?? string.Empty, FileName);
            TerminalName = (terminalName ?? string.Empty).Trim();
            SoftwareType = string.IsNullOrWhiteSpace(softwareType) ? "Main" : softwareType.Trim();
        }

        public Task<AccessCheckResult> CheckAccessAsync(string cardUid)
        {
            return Task.FromResult(Check(cardUid, false));
        }

        public Task<AccessCheckResult> CheckApproverAsync(string cardUid)
        {
            return Task.FromResult(Check(cardUid, true));
        }

        private AccessCheckResult Check(string cardUid, bool approver)
        {
            if (string.IsNullOrWhiteSpace(cardUid))
                return AccessCheckResult.Deny("No card was read.");

            if (string.IsNullOrWhiteSpace(TerminalName))
            {
                // Without a terminal name the check cannot be scoped to this machine,
                // and a badge from anywhere would be accepted. Refusing is the safe
                // failure; slide 52 makes terminalName part of the request.
                return AccessCheckResult.Deny(
                    "This terminal has no terminal name configured, so access cannot be checked. "
                    + "Set it in Settings before signing in.");
            }

            List<AccessRecord> records;
            try
            {
                if (!File.Exists(_path))
                    return AccessCheckResult.Deny("The access database is not available on this machine.");

                records = JsonConvert.DeserializeObject<List<AccessRecord>>(File.ReadAllText(_path));
            }
            catch (Exception ex)
            {
                return AccessCheckResult.Deny("The access database could not be read: " + ex.Message);
            }

            if (records == null || records.Count == 0)
                return AccessCheckResult.Deny("The access database is empty.");

            string uid = cardUid.Trim();
            bool uidKnown = false;
            AccessCheckResult cmLevel3 = null;

            foreach (AccessRecord record in records)
            {
                if (record == null || !Same(record.CardUid, uid))
                    continue;

                // The badge exists somewhere in the database. Remembered so a refusal
                // can say "not permitted here" rather than "unknown card", which are
                // different problems for whoever has to fix it.
                uidKnown = true;

                if (!Same(record.TerminalName, TerminalName))
                    continue;

                bool authorizeRecord = Same(record.SoftwareType, AuthorizeSoftwareType);
                if (!authorizeRecord && !Same(record.SoftwareType, SoftwareType))
                    continue;

                AccessLevel level = ToLevel(record.AccessLevel);
                if (level == AccessLevel.None)
                {
                    return AccessCheckResult.Deny(
                        "The access record for this card has an invalid level (" + record.AccessLevel + ").");
                }

                AccessCheckResult granted = AccessCheckResult.Allow(
                    string.IsNullOrWhiteSpace(record.Name) ? "(unnamed)" : record.Name.Trim(),
                    level,
                    record.Function,
                    record.Organization,
                    uid);

                if (!approver)
                {
                    // An "Authorize" record only approves; it does not sign anyone in.
                    if (!authorizeRecord)
                        return granted;
                    continue;
                }

                if (authorizeRecord)
                    return granted;

                // Before V5.1 added "Authorize" records, a Level 3 CM user was the approver.
                if (level == AccessLevel.Level3 && record.Organization == AccessOrganization.CM)
                    cmLevel3 = granted;
            }

            if (approver)
            {
                if (cmLevel3 != null)
                    return cmLevel3;

                return AccessCheckResult.Deny(uidKnown
                    ? "This card may not approve a two-step verification on " + TerminalName + "."
                    : "Card not recognised.");
            }

            return AccessCheckResult.Deny(uidKnown
                ? "This card has no access to " + TerminalName + " (" + SoftwareType + ")."
                : "Card not recognised.");
        }

        /// <summary>
        /// Slide 52's terminal names are "*All capitalized", so a case difference in
        /// the data file is a formatting slip rather than a different terminal.
        /// </summary>
        private static bool Same(string a, string b)
        {
            return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Anything outside 1-3 is not a level. It maps to None, which this service
        /// treats as a refusal - an out-of-range value must never fall through to the
        /// level that can change every parameter.
        /// </summary>
        public static AccessLevel ToLevel(int value)
        {
            switch (value)
            {
                case 1: return AccessLevel.Level1;
                case 2: return AccessLevel.Level2;
                case 3: return AccessLevel.Level3;
                default: return AccessLevel.None;
            }
        }
    }
}
