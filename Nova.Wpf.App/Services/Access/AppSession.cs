using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// The signed-in session, opened from a granted access check (Audio SW DFM v5.4
    /// slides 44 and 52).
    /// </summary>
    /// <remarks>
    /// Every property is set from the access record, never typed in. The setters are
    /// private for that reason: the only ways into a session are <see cref="Login"/>
    /// with a granted result and <see cref="CompleteVerification"/>, so nothing in the
    /// GUI can promote itself to Level 3 by assigning a property.
    /// </remarks>
    public class AppSession : ObservableObject, IAppSession
    {
        private bool _isLoggedIn;
        private string _userName = string.Empty;
        private AccessLevel _level = AccessLevel.None;
        private string _function = string.Empty;
        private AccessOrganization _organization = AccessOrganization.Unknown;
        private string _cardUid = string.Empty;
        private bool _isVerified;
        private DateTime _loginTime;

        public bool IsLoggedIn
        {
            get { return _isLoggedIn; }
            private set { SetProperty(ref _isLoggedIn, value); }
        }

        public string UserName
        {
            get { return _userName; }
            private set { SetProperty(ref _userName, value); }
        }

        public AccessLevel Level
        {
            get { return _level; }
            private set
            {
                if (SetProperty(ref _level, value))
                    OnPropertyChanged(nameof(Role));
            }
        }

        public string Function
        {
            get { return _function; }
            private set { SetProperty(ref _function, value); }
        }

        public AccessOrganization Organization
        {
            get { return _organization; }
            private set { SetProperty(ref _organization, value); }
        }

        public string CardUid
        {
            get { return _cardUid; }
            private set { SetProperty(ref _cardUid, value); }
        }

        public bool IsVerified
        {
            get { return _isVerified; }
            private set { SetProperty(ref _isVerified, value); }
        }

        public DateTime LoginTime
        {
            get { return _loginTime; }
            private set { SetProperty(ref _loginTime, value); }
        }

        /// <summary>
        /// Display-only label for the level. Derived, so it cannot disagree with
        /// <see cref="Level"/> the way a separately assigned role string could.
        /// </summary>
        public string Role
        {
            get
            {
                switch (Level)
                {
                    case AccessLevel.Level1: return "Level 1";
                    case AccessLevel.Level2: return "Level 2";
                    case AccessLevel.Level3: return "Level 3";
                    default: return string.Empty;
                }
            }
        }

        public void Login(AccessCheckResult granted)
        {
            if (granted == null || !granted.Granted)
                throw new InvalidOperationException("A session can only be opened from a granted access check.");

            UserName = granted.Name;
            Function = granted.Function ?? string.Empty;
            Organization = granted.Organization;
            CardUid = granted.CardUid ?? string.Empty;
            Level = granted.Level;

            // Slide 45: only a Level 3 SI login is held pending a second badge.
            // Everything else is usable the moment it is granted.
            IsVerified = !(granted.Level == AccessLevel.Level3 &&
                           granted.Organization == AccessOrganization.SI);

            LoginTime = DateTime.Now;
            IsLoggedIn = true;
        }

        public void CompleteVerification(string verifierName, string verifierCardUid)
        {
            if (!IsLoggedIn)
                return;

            IsVerified = true;
        }

        public void Logout()
        {
            UserName = string.Empty;
            Function = string.Empty;
            Organization = AccessOrganization.Unknown;
            CardUid = string.Empty;
            Level = AccessLevel.None;
            IsVerified = false;
            LoginTime = default(DateTime);
            IsLoggedIn = false;
        }
    }
}
