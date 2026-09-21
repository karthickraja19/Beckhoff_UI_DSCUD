using System;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;
using Nova.ViewModels.Common;

namespace Nova.Wpf.App.Services
{
    public class WpfNavigationService : INavigationService
    {
        private readonly Func<AppPageNames, PageViewModel> _pageFactory;
        private INavigablePage _currentPage;

        public INavigablePage CurrentPage
        {
            get { return _currentPage; }
        }

        public event Action<INavigablePage> Navigated;

        public WpfNavigationService(Func<AppPageNames, PageViewModel> pageFactory)
        {
            _pageFactory = pageFactory;
        }

        public void NavigateTo(AppPageNames pageName)
        {
            if (_currentPage != null && _currentPage.PageName == pageName)
            {
                return;
            }

            if (_currentPage != null)
            {
                _currentPage.OnNavigatedFrom();
            }

            PageViewModel nextPage = _pageFactory(pageName);
            _currentPage = nextPage;
            _currentPage.OnNavigatedTo();

            if (Navigated != null)
            {
                Navigated(nextPage);
            }
        }
    }
}
