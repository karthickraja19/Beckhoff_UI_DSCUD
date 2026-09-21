using Nova.Abstractions.Data;

namespace Nova.Abstractions.Services
{
    /// <summary>
    /// Contract for any page that supports navigation lifecycle events.
    /// </summary>
    public interface INavigablePage
    {
        AppPageNames PageName { get; }
        void OnNavigatedTo();
        void OnNavigatedFrom();
    }
}
