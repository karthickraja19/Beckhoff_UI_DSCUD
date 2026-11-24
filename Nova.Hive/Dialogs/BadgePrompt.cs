using System;
using System.Windows;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>
    /// <see cref="IBadgePrompt"/> over <see cref="OperatorDialog"/>: the badge is read by
    /// <see cref="CardScan"/> and checked against the access list before OK is enabled.
    /// </summary>
    public sealed class BadgePrompt : IBadgePrompt
    {
        private readonly IAccessControlService _access;
        private readonly IAppSession _session;

        public BadgePrompt(IAccessControlService access, IAppSession session)
        {
            _access = access;
            _session = session;
        }

        public AccessCheckResult RequestBadge(string header, AccessLevel minimumLevel)
        {
            return Show(header, minimumLevel, null, null, false);
        }

        public AccessCheckResult RequestSecondApproval(string header)
        {
            string ownCard = _session != null && _session.IsLoggedIn ? _session.CardUid : null;

            // The approver check itself decides who may approve (an "Authorize" record or
            // a Level 3 CM user); here it only has to be somebody else.
            return Show(header, AccessLevel.Level1, result =>
            {
                // Two steps signed by one badge is one step (slide 45).
                if (!string.IsNullOrWhiteSpace(ownCard)
                    && string.Equals(ownCard.Trim(), (result.CardUid ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return "The second approval must come from a different badge. Please swipe another ID card.";
                }

                return null;
            }, "Two-step verification: an approver swipes their ID card.", true);
        }

        private AccessCheckResult Show(string header, AccessLevel minimumLevel, Func<AccessCheckResult, string> extraCheck, string prompt, bool approver)
        {
            Application app = Application.Current;
            if (app == null)
                return null;

            if (!app.Dispatcher.CheckAccess())
                return (AccessCheckResult)app.Dispatcher.Invoke(new Func<AccessCheckResult>(() => Show(header, minimumLevel, extraCheck, prompt, approver)));

            var dialog = new OperatorDialog(header, _access, minimumLevel, extraCheck, prompt, approver)
            {
                Owner = app.MainWindow,
            };

            return dialog.ShowDialog() == true ? dialog.Result : null;
        }
    }
}
