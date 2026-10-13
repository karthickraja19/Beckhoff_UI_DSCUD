using System.ComponentModel;
using System.Threading.Tasks;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// The access check the GUI uses: the local list or MES, as the Access Control tab's
    /// "Access Control with MES" setting says (Access Control V5.1, 3.3.1.1).
    /// </summary>
    /// <remarks>
    /// Read on every check, so switching the setting takes effect on the next badge. In MES
    /// mode MES is also told when the operator signs out ("Close").
    /// </remarks>
    public sealed class RoutedAccessControlService : IAccessControlService
    {
        private readonly JsonAccessControlService _local;
        private readonly MesAccessControlService _mes;
        private readonly IAccessControlAdmin _admin;

        private string _signedInCard;

        public RoutedAccessControlService(
            JsonAccessControlService local,
            MesAccessControlService mes,
            IAccessControlAdmin admin,
            IAppSession session)
        {
            _local = local;
            _mes = mes;
            _admin = admin;

            if (session != null)
                session.PropertyChanged += (sender, e) => OnSessionChanged(session, e);
        }

        public string TerminalName
        {
            get { return _local.TerminalName; }
        }

        public string SoftwareType
        {
            get { return _local.SoftwareType; }
        }

        private bool UseMes
        {
            get { return _mes != null && _admin.IsMesAvailable && _admin.Settings.AccessControlWithMes; }
        }

        public Task<AccessCheckResult> CheckAccessAsync(string cardUid)
        {
            return UseMes ? _mes.CheckAccessAsync(cardUid) : _local.CheckAccessAsync(cardUid);
        }

        public Task<AccessCheckResult> CheckApproverAsync(string cardUid)
        {
            return UseMes ? _mes.CheckApproverAsync(cardUid) : _local.CheckApproverAsync(cardUid);
        }

        private void OnSessionChanged(IAppSession session, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IAppSession.IsLoggedIn))
                return;

            if (session.IsLoggedIn)
            {
                _signedInCard = session.CardUid;
                return;
            }

            string card = _signedInCard;
            _signedInCard = null;
            if (UseMes && !string.IsNullOrWhiteSpace(card))
            {
                // Fire and forget: signing out must not wait for MES.
                Task ignored = _mes.NotifySignOutAsync(card);
            }
        }
    }
}
