using CommunityToolkit.Mvvm.ComponentModel;
using Nova.Abstractions.Data;
using Nova.ViewModels.Common;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// One section of the Config page's "Machine Configs" area (Audio SW DFM v5.4
    /// slide 58), together with the access level it takes to edit it.
    /// </summary>
    /// <remarks>
    /// The slide grades edit authority rather than granting it wholesale - Level 1 is
    /// view only, Level 2 may edit "non-critical parts of machine config only", and
    /// Level 3 may change anything. A single page-wide flag cannot express that, so
    /// each section carries its own <see cref="RequiredLevel"/> and the page pushes
    /// <see cref="IsEditable"/> down whenever the operator's level changes.
    /// </remarks>
    public partial class ConfigSection : ObservableObject
    {
        /// <summary>Section name shown on the segmented control.</summary>
        public string Title { get; }

        /// <summary>Lucide icon kind, matching the other Audio segmented controls.</summary>
        public string Icon { get; }

        /// <summary>
        /// The hosted page. DataTemplates maps it to its view, so the host is a plain
        /// ContentControl.
        /// </summary>
        public PageViewModel Page { get; }

        /// <summary>
        /// The hosted page wrapped as a tab, purely so <see cref="TabItemViewModel.CancelEdits"/>
        /// can walk into it and its nested tab groups on the way out.
        /// </summary>
        public TabItemViewModel Tab { get; }

        /// <summary>Minimum level that may edit anything in this section.</summary>
        public AccessLevel RequiredLevel { get; }

        /// <summary>
        /// True when the current operator is at or above <see cref="RequiredLevel"/>.
        /// The view binds IsEnabled to this, so a section the operator may not change is
        /// visibly present but inert - slide 58 grades authority, it does not hide the
        /// parts an operator cannot use.
        /// </summary>
        [ObservableProperty]
        private bool _isEditable;

        public ConfigSection(string title, string icon, PageViewModel page, AccessLevel requiredLevel)
        {
            Title = title;
            Icon = icon;
            Page = page;
            RequiredLevel = requiredLevel;
            Tab = new TabItemViewModel(title, icon, page);
        }
    }
}
