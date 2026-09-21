using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Services;
using Nova.ViewModels.Auth;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Shells
{
    public partial class AuthShellViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ViewModelBase? _currentAuthView;

        public AuthShellViewModel(LoginViewModel loginViewModel)
        {
            // By default, show the login view
            _currentAuthView = loginViewModel;
        }
    }
}
