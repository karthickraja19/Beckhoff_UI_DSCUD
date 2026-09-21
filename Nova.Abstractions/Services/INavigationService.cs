using System;
using Nova.Abstractions.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Frame-agnostic page navigation service contract.
    /// </summary>
    public interface INavigationService
    {
        INavigablePage? CurrentPage { get; }
        void NavigateTo(AppPageNames pageName);
        event Action<INavigablePage>? Navigated;
    }
}
