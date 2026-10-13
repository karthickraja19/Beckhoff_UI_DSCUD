using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>One checkable row of the PDATA field picker.</summary>
    public partial class PdataFieldItem : ObservableObject
    {
        public string Name { get; }

        [ObservableProperty]
        private bool _isSelected;

        /// <summary>
        /// Raised when the operator ticks or unticks the item. The tab listens rather than
        /// binding straight to the collection so it can suppress the callback while it is
        /// rebuilding the list itself.
        /// </summary>
        public event EventHandler SelectionChanged;

        partial void OnIsSelectedChanged(bool value)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public PdataFieldItem(string name, bool isSelected = false)
        {
            Name = name ?? string.Empty;
            _isSelected = isSelected;
        }
    }
}
