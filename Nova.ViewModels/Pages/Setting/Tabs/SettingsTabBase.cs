using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// One tab of the Setting page. The page passes the operator's level down, so each tab
    /// states its own rule over one shared value, as the pages do (slide 39).
    /// </summary>
    public abstract class SettingsTabBase : ObservableObject
    {
        private readonly SynchronizationContext _ui = SynchronizationContext.Current;
        private AccessLevel _level;

        /// <summary>The tab's name on the segmented control.</summary>
        public abstract string Title { get; }

        public AccessLevel Level
        {
            get { return _level; }
            set
            {
                if (!SetProperty(ref _level, value))
                    return;

                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanEditCritical));
                OnLevelChanged();
            }
        }

        public bool IsLoggedIn => Level != AccessLevel.None;

        /// <summary>Level 2 or 3.</summary>
        public bool CanEdit => Level >= AccessLevel.Level2;

        /// <summary>Level 3: "Admin ID is logged. Can change all parameters" (slide 39).</summary>
        public bool CanEditCritical => Level >= AccessLevel.Level3;

        /// <summary>Called when the operator's level changed.</summary>
        protected virtual void OnLevelChanged()
        {
        }

        /// <summary>Called when the tab is brought on screen; re-read what it shows.</summary>
        public virtual void OnShown()
        {
        }

        /// <summary>Called when the tab leaves the screen; drop unsaved edits.</summary>
        public virtual void OnHidden()
        {
        }

        /// <summary>
        /// Runs on the UI thread - for events a service raises on its own thread. Always
        /// posted: WPF may give the UI thread a different context instance, so comparing
        /// contexts cannot tell whether this already is the UI thread.
        /// </summary>
        protected void OnUi(Action action)
        {
            if (_ui == null)
                action();
            else
                _ui.Post(_ => action(), null);
        }
    }
}
