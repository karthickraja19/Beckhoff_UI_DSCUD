using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        private readonly ICcdImageSource _images;
        private readonly System.Threading.SynchronizationContext _ui = System.Threading.SynchronizationContext.Current;

        /// <summary>Where the frames come from, or why there are none.</summary>
        public string SourceText { get; }

        public AudioVisionViewModel(IAppSession session, ICcdImageSource images = null)
            : base(session)
        {
            _images = images;

            // The source's cameras first, then "CCDn" up to the slide's six tiles.
            var names = new List<string>();
            if (images != null)
                names.AddRange(images.Cameras);
            for (int i = 1; names.Count < 6; i++)
            {
                if (!names.Contains("CCD" + i))
                    names.Add("CCD" + i);
            }

            for (int i = 0; i < names.Count && i < 6; i++)
            {
                var tile = new CcdTile(names[i], BuildInformation(i + 1));
                Tiles.Add(tile);
                Apply(tile);
            }

            SourceText = images != null
                ? images.Description
                : "No image source is configured (App.config VisionImageFolder): set it to the folder the vision software saves its images in.";

            if (images != null)
                images.CaptureChanged += OnCaptureChanged;
        }

        private void OnCaptureChanged(object sender, string camera)
        {
            // Raised on the file watcher's thread; the tiles are bound.
            if (_ui != null)
                _ui.Post(_ => ApplyCamera(camera), null);
            else
                ApplyCamera(camera);
        }

        private void ApplyCamera(string camera)
        {
            foreach (CcdTile tile in Tiles)
            {
                if (string.Equals(tile.Name, camera, System.StringComparison.OrdinalIgnoreCase))
                    Apply(tile);
            }
        }

        /// <summary>Puts the camera's newest capture on its tile.</summary>
        private void Apply(CcdTile tile)
        {
            CcdCapture capture = _images != null ? _images.Latest(tile.Name) : null;
            if (capture == null)
                return;

            // Cleared first, so a file rewritten under the same name is read again.
            tile.ImagePath = null;
            tile.ImagePath = capture.ImagePath;
            tile.HasImage = true;
            tile.IsNg = capture.Result == "NG" || capture.Result == "FAIL";
            tile.Caption = capture.Time.ToString("HH:mm:ss")
                + (string.IsNullOrWhiteSpace(capture.UnitSn) ? string.Empty : "   " + capture.UnitSn)
                + (string.IsNullOrWhiteSpace(capture.Result) ? string.Empty : "   " + capture.Result);

            var information = new List<SummaryRow>
            {
                new SummaryRow("CCD", tile.Name),
                new SummaryRow("Time", capture.Time.ToString("yyyy-MM-dd HH:mm:ss")),
                new SummaryRow("Unit SN", string.IsNullOrWhiteSpace(capture.UnitSn) ? "—" : capture.UnitSn),
                new SummaryRow("Result", string.IsNullOrWhiteSpace(capture.Result) ? "—" : capture.Result),
            };

            if (capture.Information != null && capture.Information.Count > 0)
            {
                foreach (var pair in capture.Information)
                    information.Add(new SummaryRow(pair.Key, pair.Value));
            }
            else
            {
                // The slide's fields, still unmeasured: the vision software wrote no
                // information file beside this image.
                foreach (SummaryRow row in BuildInformation(Tiles.IndexOf(tile) + 1).Skip(2))
                    information.Add(row);
            }

            information.Add(new SummaryRow("File", System.IO.Path.GetFileName(capture.ImagePath)));
            tile.Information = information;
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
