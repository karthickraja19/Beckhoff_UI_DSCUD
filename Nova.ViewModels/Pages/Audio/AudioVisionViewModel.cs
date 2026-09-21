using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.Abstractions.Data;
using Nova.Abstractions.Services;

namespace Nova.ViewModels.Pages.Audio
{
    /// <summary>
    /// The Vision page - Audio SW DFM v5.4 slide 61.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Vision page shows all CCD images for current running units. Clicking image
    /// magnifies image and displays information for capture." So: a 3x2 grid of
    /// captures, and a detail pane that appears once one is chosen.
    /// </para>
    /// <para>
    /// The frames themselves are not available in this build. The machine's cameras are
    /// reachable as comm devices (<see cref="ICommDeviceService"/>) but nothing
    /// retrieves an image buffer from them, and there is no vision adapter behind
    /// <c>VS_SHA1</c> yet. Rather than leave six empty rectangles that read as six
    /// broken cameras, each tile reports itself as having no frame and the detail pane
    /// lists the capture fields the slide specifies with their values marked
    /// unavailable. The layout, the selection behaviour and the field list are
    /// therefore reviewable against the slide now, and only the image source is
    /// outstanding.
    /// </para>
    /// </remarks>
    public partial class AudioVisionViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Vision;

        /// <summary>
        /// Six tiles in a 3x2 grid, as drawn. Fixed at six because the slide fixes it;
        /// a machine with fewer cameras shows the spare tiles empty, which is what the
        /// DFM mockup itself does.
        /// </summary>
        public ObservableCollection<CcdTile> Tiles { get; } = new ObservableCollection<CcdTile>();

        [ObservableProperty]
        private CcdTile _selectedTile;

        /// <summary>
        /// The detail pane only exists once a tile is chosen - slide 61 shows it below
        /// the grid, not alongside an unselected grid.
        /// </summary>
        public bool HasSelection => SelectedTile != null;

        partial void OnSelectedTileChanged(CcdTile oldValue, CcdTile newValue)
        {
            if (oldValue != null)
                oldValue.IsSelected = false;

            if (newValue != null)
                newValue.IsSelected = true;

            OnPropertyChanged(nameof(HasSelection));
        }

        [RelayCommand]
        private void SelectTile(CcdTile tile)
        {
            // Clicking the selected tile again closes the detail pane, which is the
            // quickest way back to all six at full size.
            SelectedTile = ReferenceEquals(tile, SelectedTile) ? null : tile;
        }

        public AudioVisionViewModel(IAppSession session)
            : base(session)
        {
            for (int i = 1; i <= 6; i++)
                Tiles.Add(new CcdTile("CCD" + i, BuildInformation(i)));
        }

        /// <summary>
        /// The capture fields slide 61 lists in its Information panel, in its order.
        /// </summary>
        /// <remarks>
        /// Values read "—" rather than a plausible number on purpose: a measured gap or
        /// exposure that was never measured is the one thing on this page an engineer
        /// might act on, so it must not be invented.
        /// </remarks>
        private static IReadOnlyList<SummaryRow> BuildInformation(int index)
        {
            const string NotAvailable = "—";

            return new List<SummaryRow>
            {
                new SummaryRow("CCD", "Image" + index + ",1"),
                new SummaryRow("Unit", "pcs"),
                new SummaryRow("Angle", NotAvailable),
                new SummaryRow("X_Gap", NotAvailable),
                new SummaryRow("Y_Gap", NotAvailable),
                new SummaryRow("Left_Gap", NotAvailable),
                new SummaryRow("Delta_XGap", NotAvailable),
                new SummaryRow("Delta_YGap", NotAvailable),
                new SummaryRow("Delta_Left_Gap", NotAvailable),
                new SummaryRow("T1_Brightness", NotAvailable),
                new SummaryRow("T1_Exposure", NotAvailable)
            };
        }
    }
}
