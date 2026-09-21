using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Common
{
    /// <summary>
    /// Base ViewModel class incorporating INotifyPropertyChanged support and IDisposable cleanup.
    /// </summary>
    public abstract class ViewModelBase : ObservableObject, IDisposable
    {
        private bool _isDisposed;

        public bool IsDisposed => _isDisposed;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // Release managed resources here if any
                }
                _isDisposed = true;
            }
        }

        ~ViewModelBase()
        {
            Dispose(false);
        }
    }
}
