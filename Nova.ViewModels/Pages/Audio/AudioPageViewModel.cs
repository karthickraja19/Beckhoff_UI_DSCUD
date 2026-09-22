using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// Base for the six pages the Audio/Home indicator bar opens.
    /// </summary>
    /// <remarks>
    /// Exists for one reason: every one of those pages is access-gated, and the DFM
    /// states the gate per page rather than globally - "Level 1 OSS - view only /
    /// Level 2 OSS - edit authority for non-critical parts / Level 3 OSS - can access
    /// Edit/Save buttons" (slide 58), "All modules on the home page are read only
    /// unless logged in" (slide 43), "For Level 1 and Level 2 users access, product
    /// type will show as read-only" (slide 62). Putting <see cref="AccessLevel"/> here
    /// means each page expresses its own rule as a property over one shared value
    /// instead of re-deriving the level from the session.
    /// </remarks>
    public abstract class AudioPageViewModel : PageViewModel
    {
        private readonly IAppSession _session;
        private readonly bool _bypassAccessLevels;

        private AccessLevel _accessLevel;

        /// <summary>Current operator's level, or <see cref="AccessLevel.None"/> before login.</summary>
        public AccessLevel AccessLevel
        {
            get { return _accessLevel; }
            private set
            {
                if (_accessLevel == value)
                    return;

                _accessLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanEditCritical));
                OnAccessLevelChanged(value);
            }
        }

        public bool IsLoggedIn => AccessLevel != AccessLevel.None;

        /// <summary>
        /// Level 2 or better: "Engineer ID is logged. Universal change access. Key
        /// parameters cannot be changed" (slide 39).
        /// </summary>
        public bool CanEdit => AccessLevel >= AccessLevel.Level2;

        /// <summary>
        /// Level 3 only: "Admin ID is logged. Can change all parameters" (slide 39).
        /// Everything the DFM calls a key or critical parameter is behind this.
        /// </summary>
        public bool CanEditCritical => AccessLevel >= AccessLevel.Level3;

        /// <summary>True when this page ignores the operator's level. See <see cref="AccessBypass"/>.</summary>
        protected bool BypassesAccessLevels => _bypassAccessLevels;

        /// <summary>Hook for pages that have to re-read or re-lock something on a level change.</summary>
        protected virtual void OnAccessLevelChanged(AccessLevel level) { }

        protected AudioPageViewModel(IAppSession session)
            : this(session, false)
        {
        }

        /// <param name="session">The operator session the level is read from.</param>
        /// <param name="bypassAccessLevels">
        /// Treat every operator, signed in or not, as Level 3 on this page. Only the
        /// commissioning switch in <see cref="AccessBypass"/> should pass true.
        /// </param>
        protected AudioPageViewModel(IAppSession session, bool bypassAccessLevels)
        {
            _session = session;
            _bypassAccessLevels = bypassAccessLevels;
            _accessLevel = CurrentLevel();

            if (_session != null)
                _session.PropertyChanged += OnSessionPropertyChanged;
        }

        private void OnSessionPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IAppSession.Level) ||
                e.PropertyName == nameof(IAppSession.IsLoggedIn))
            {
                AccessLevel = CurrentLevel();
            }
        }

        /// <summary>
        /// The level the access check returned (DFM slide 52), read straight from the
        /// session.
        /// </summary>
        /// <remarks>
        /// This used to map a role string ("Admin", "Engineer") onto a level. The
        /// access database states the level as a number, so translating it into a word
        /// and back again only created a place for the two to disagree - and the old
        /// mapping sent every unrecognised role to Level 1, silently, which meant a
        /// typo in a role name looked like a working login with reduced rights.
        /// </remarks>
        private AccessLevel CurrentLevel()
        {
            if (_bypassAccessLevels)
                return AccessLevel.Level3;

            return _session != null && _session.IsLoggedIn ? _session.Level : AccessLevel.None;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _session != null)
                _session.PropertyChanged -= OnSessionPropertyChanged;

            base.Dispose(disposing);
        }
    }
}
