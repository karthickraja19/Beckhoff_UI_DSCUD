using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Common
{
    /// <summary>
    /// Base class for all navigable screen page ViewModels.
    /// Exposes navigation lifecycle hooks and enforces page identity.
    /// </summary>
    public abstract class PageViewModel : ViewModelBase, INavigablePage
    {
        public abstract AppPageNames PageName { get; }

        public virtual void OnNavigatedTo() { }

        public virtual void OnNavigatedFrom() { }
    }
}
