using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Auth
{
    /// <summary>
    /// The login pop-up - Audio SW DFM v5.4 slide 44, and the two-step verification
    /// on slide 45.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slide 44 states the flow in four lines: "Swipe/Scan ID card to auto load login
    /// info", "Data are read only", "Refresh access level for login confirmation",
    /// "Two step verification required for level 3 login". There is no username field
    /// and no password. The operator swipes, the access check (slide 52) returns a
    /// name, a level and a function team, and those are displayed for confirmation.
    /// </para>
    /// <para>
    /// Nothing here accepts typed credentials. <see cref="SubmitCardAsync"/> is the
    /// only way in, and it takes a UID that came from the reader - which is what
    /// "Data are read only" means in practice. Slide 62 says the same thing about the
    /// Settings page: "Manually key-in is NOT allowed".
    /// </para>
    /// <para>
    /// A Level 3 SI login does not complete on the first swipe. The view model moves
    /// to <see cref="IsAwaitingVerification"/> and waits for a second badge, which
    /// must itself be Level 3 and CM. No session is opened until that second badge
    /// passes, so there is never a moment where an unapproved Level 3 operator is
    /// looking at a working machine GUI.
    /// </para>
    /// </remarks>
    public partial class LoginViewModel : ViewModelBase
    {
        private readonly IAccessControlService _access;
        private readonly IAppSession _session;
        private readonly IAccessLog _log;

        /// <summary>
        /// A granted Level 3 SI check waiting for its second badge (slide 45). Held
        /// here rather than in the session, so no half-open session exists while the
        /// second person is being fetched.
        /// </summary>
        private AccessCheckResult _pending;

        /// <summary>Which terminal the operator is signing in to (slide 52).</summary>
        public string TerminalName
        {
            get { return _access == null ? string.Empty : _access.TerminalName; }
        }

        public string SoftwareType
        {
            get { return _access == null ? string.Empty : _access.SoftwareType; }
        }

        // ==================================================================
        // What the access check returned - all read-only to the operator
        // ==================================================================

        [ObservableProperty]
        private string _operatorName = string.Empty;

        [ObservableProperty]
        private string _accessLevelText = string.Empty;

        [ObservableProperty]
        private string _functionTeam = string.Empty;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        /// <summary>
        /// True between a Level 3 SI badge and the Level 3 CM badge that countersigns
        /// it (slide 45).
        /// </summary>
        [ObservableProperty]
        private bool _isAwaitingVerification;

        /// <summary>Prompt under the card graphic; changes for the second step.</summary>
        public string Prompt
        {
            get
            {
                return IsAwaitingVerification
                    ? "Level 3 access by an SI user needs a second approval. "
                      + "Swipe a Level 3 CM badge to continue."
                    : "Swipe or scan your ID card to sign in.";
            }
        }

        partial void OnIsAwaitingVerificationChanged(bool value)
        {
            OnPropertyChanged(nameof(Prompt));
        }

        /// <summary>True once a check has returned something to show.</summary>
        public bool HasResult
        {
            get { return !string.IsNullOrEmpty(OperatorName); }
        }

        partial void OnOperatorNameChanged(string value)
        {
            OnPropertyChanged(nameof(HasResult));
        }

        public LoginViewModel(IAccessControlService access, IAppSession session, IAccessLog log = null)
        {
            _access = access;
            _session = session;
            _log = log;
        }

        /// <summary>
        /// Handles one badge. Called by the view when the reader finishes a scan.
        /// </summary>
        /// <remarks>
        /// Returns a task so the view can await the MES round trip; the view disables
        /// the reader while <see cref="IsBusy"/> is set, so two fast swipes cannot
        /// interleave two checks.
        /// </remarks>
        public async Task SubmitCardAsync(string cardUid)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(cardUid))
                return;

            IsBusy = true;
            ErrorMessage = string.Empty;

            try
            {
                AccessCheckResult result = await _access.CheckAccessAsync(cardUid);

                if (result == null || !result.Granted)
                {
                    string reason = result == null ? "Access check failed." : result.FailureReason;
                    Refuse(cardUid, reason);
                    return;
                }

                if (IsAwaitingVerification)
                    CompleteSecondStep(result);
                else
                    Accept(result);
            }
            catch (Exception ex)
            {
                // A check that throws is a refusal, not a crash - the operator is
                // standing at a machine and needs to be told why they are not in.
                Refuse(cardUid, "Access check failed: " + ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// First badge. Opens the session immediately unless slide 45 applies, in
        /// which case the grant is held here until a second badge countersigns it.
        /// </summary>
        /// <remarks>
        /// A Level 3 SI grant deliberately does NOT call
        /// <see cref="IAppSession.Login"/> yet. Opening the session sets IsLoggedIn,
        /// which is what the shell watches to swap the login screen away for the
        /// machine GUI - so signing in first would close this screen and the operator
        /// would never be shown the second-badge prompt. Worse, they would be standing
        /// at an apparently working Level 3 session that no one had approved. The
        /// grant waits in <see cref="_pending"/> until the second step passes.
        /// </remarks>
        private void Accept(AccessCheckResult result)
        {
            OperatorName = result.Name;
            AccessLevelText = Describe(result.Level);
            FunctionTeam = result.Function ?? string.Empty;

            if (_log != null)
                _log.LoginAttempt(result.CardUid, true, result.Name, (int)result.Level,
                    "function=" + (result.Function ?? "-") + "; org=" + result.Organization);

            bool needsSecondStep = result.Level == AccessLevel.Level3 &&
                                   result.Organization == AccessOrganization.SI;

            if (needsSecondStep)
            {
                _pending = result;
                IsAwaitingVerification = true;
                return;
            }

            _session.Login(result);
        }

        /// <summary>
        /// Second badge. Slide 45 says to "Follow same MES access control flow in each
        /// step", so this badge went through the identical check - it only has to be
        /// the right level and the right organisation, and a different person.
        /// </summary>
        private void CompleteSecondStep(AccessCheckResult verifier)
        {
            if (_pending == null)
            {
                IsAwaitingVerification = false;
                return;
            }

            if (string.Equals((verifier.CardUid ?? string.Empty).Trim(),
                              (_pending.CardUid ?? string.Empty).Trim(),
                              StringComparison.OrdinalIgnoreCase))
            {
                // Two steps signed by one badge is one step. The whole point of the
                // requirement is that a second person is present. Checked before the
                // level so re-swiping the same L3 SI card gives the useful message.
                ErrorMessage = "The second approval must come from a different badge.";
                return;
            }

            if (verifier.Level != AccessLevel.Level3 || verifier.Organization != AccessOrganization.CM)
            {
                ErrorMessage = "The second approval must be a Level 3 CM badge. "
                    + verifier.Name + " is " + Describe(verifier.Level)
                    + " (" + verifier.Organization + ").";
                return;
            }

            // Both steps have passed, so the session opens now - as the operator who
            // swiped first, countersigned by the second badge.
            AccessCheckResult granted = _pending;
            _pending = null;
            IsAwaitingVerification = false;
            ErrorMessage = string.Empty;

            _session.Login(granted);
            _session.CompleteVerification(verifier.Name, verifier.CardUid);

            if (_log != null)
                _log.VerificationCompleted(granted.Name, verifier.Name, verifier.CardUid);
        }

        private void Refuse(string cardUid, string reason)
        {
            OperatorName = string.Empty;
            AccessLevelText = string.Empty;
            FunctionTeam = string.Empty;
            ErrorMessage = reason;

            if (_log != null)
                _log.LoginAttempt(cardUid, false, null, 0, reason);
        }

        /// <summary>
        /// Abandons a Level 3 SI login that was never countersigned, so a half-open
        /// session is not left behind when the operator walks away.
        /// </summary>
        [RelayCommand]
        private void CancelVerification()
        {
            if (!IsAwaitingVerification)
                return;

            string name = _pending == null ? string.Empty : _pending.Name;

            // No session was ever opened, so there is nothing to log out of - the
            // pending grant is simply discarded.
            _pending = null;
            IsAwaitingVerification = false;
            Clear();

            if (_log != null)
                _log.Logout(name, 3, "second approval not provided");
        }

        private static string Describe(AccessLevel level)
        {
            switch (level)
            {
                case AccessLevel.Level1: return "Level 1 - view only";
                case AccessLevel.Level2: return "Level 2 - universal change access";
                case AccessLevel.Level3: return "Level 3 - can change all parameters";
                default: return "No access";
            }
        }

        public void Clear()
        {
            OperatorName = string.Empty;
            AccessLevelText = string.Empty;
            FunctionTeam = string.Empty;
            ErrorMessage = string.Empty;
        }
    }
}
