using Nova.Abstractions.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Asks the operator to swipe a badge, for view models that need one - the card-scan
    /// window of Audio SW DFM v5.4 slides 37 and 50, checked against the access list.
    /// </summary>
    /// <remarks>
    /// Both calls block until the window closes and return null when it is cancelled.
    /// The badge always comes from the reader: slide 37 forbids typing it.
    /// </remarks>
    public interface IBadgePrompt
    {
        /// <summary>A badge from the access list at <paramref name="minimumLevel"/> or higher.</summary>
        /// <param name="header">The red line at the top of the window: what the badge is for.</param>
        AccessCheckResult RequestBadge(string header, AccessLevel minimumLevel);

        /// <summary>
        /// The second step of two-step verification (slide 45): an approver's badge - see
        /// <see cref="IAccessControlService.CheckApproverAsync"/> - that is not the signed-in
        /// operator's own card.
        /// </summary>
        AccessCheckResult RequestSecondApproval(string header);
    }
}
