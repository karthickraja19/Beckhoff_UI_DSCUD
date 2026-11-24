using System;
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
    /// magnifies image and displays information for capture." So: a grid of the newest
    /// images the vision software saved (one folder, App.config VisionImageFolder), newest
    /// first, and a detail pane that opens once one is clicked.
    /// </para>
    /// <para>
    /// The grid follows the running units: each new image takes the first tile and the older
    /// ones move along. The detail pane holds a copy of the image that was clicked, so the
    /// image being inspected stays put while new ones arrive. Clicking it again closes the
    /// pane. A tile with no image yet says so rather than showing an empty frame, which on a
    /// vision page reads as a dead camera.
    /// </para>
    /// <para>
    /// The values listed beside the magnified image are the ones the vision software wrote
    /// in a text file next to it (angle, gaps, brightness, exposure, ...). An image without
    /// one lists the slide's fields as "—": a measured gap that was never measured must not
    /// be invented.
    /// </para>
    /// </remarks>
    public partial class AudioVisionViewModel : AudioPageViewModel
    {
        public override AppPageNames PageName => AppPageNames.Vision;

        /// <summary>How many images are shown when none is configured: the slide's 3x2 grid.</summary>
        public const int DefaultTileCount = 6;

        /// <summary>The newest images, newest first. Always <see cref="TileCount"/> tiles; one without an image says so.</summary>
        public ObservableCollection<CcdTile> Tiles { get; } = new ObservableCollection<CcdTile>();

        public int TileCount { get; }

        /// <summary>
        /// Columns of the tile grid, chosen from the tile count so the tiles fill the page:
        /// 1 tile one column, up to 4 two columns, up to 9 three, then four.
        /// </summary>
        public int TileColumns { get; }

        /// <summary>The image that was clicked, as it was then; null when the detail pane is closed.</summary>
        [ObservableProperty]
        private CcdTile _selectedTile;

        /// <summary>The detail pane only exists once an image is chosen.</summary>
        public bool HasSelection => SelectedTile != null;

        partial void OnSelectedTileChanged(CcdTile oldValue, CcdTile newValue)
        {
            OnPropertyChanged(nameof(HasSelection));
            MarkSelectedTile();
        }

        [RelayCommand]
        private void SelectTile(CcdTile tile)
        {
            // A tile with no image has nothing to magnify.
            if (tile == null || !tile.HasImage)
                return;

            // Clicking the image that is already open closes the detail pane.
            if (SelectedTile != null && string.Equals(SelectedTile.ImagePath, tile.ImagePath, StringComparison.OrdinalIgnoreCase))
            {
                SelectedTile = null;
                return;
            }

            SelectedTile = new CcdTile(tile.Name, tile.Information)
            {
                ImagePath = tile.ImagePath,
                HasImage = true,
                Caption = tile.Caption,
                IsNg = tile.IsNg,
            };
        }

        private readonly ICcdImageSource _images;
        private readonly System.Threading.SynchronizationContext _ui = System.Threading.SynchronizationContext.Current;

        /// <summary>Where the images come from, or why there are none.</summary>
        public string SourceText { get; }

        /// <param name="images">The folder source; null when VisionImageFolder is not set.</param>
        /// <param name="tileCount">How many of the newest images to show (App.config VisionImageCount).</param>
        public AudioVisionViewModel(IAppSession session, ICcdImageSource images = null, int tileCount = DefaultTileCount)
            : base(session)
        {
            _images = images;
            TileCount = tileCount < 1 ? DefaultTileCount : Math.Min(tileCount, 16);
            TileColumns = TileCount <= 1 ? 1 : TileCount <= 4 ? 2 : TileCount <= 9 ? 3 : 4;

            for (int i = 0; i < TileCount; i++)
                Tiles.Add(new CcdTile("Image " + (i + 1), BuildInformation()));

            SourceText = images != null
                ? images.Description
                : "No image source is configured (App.config VisionImageFolder): set it to the folder the vision software saves its images in.";

            if (images != null)
            {
                images.ImagesChanged += OnImagesChanged;
                Refresh();
            }
        }

        private void OnImagesChanged(object sender, EventArgs e)
        {
            // Raised on the file watcher's thread; the tiles are bound.
            if (_ui != null)
                _ui.Post(_ => Refresh(), null);
            else
                Refresh();
        }

        /// <summary>Puts the newest images on the tiles, newest first.</summary>
        private void Refresh()
        {
            IReadOnlyList<CcdCapture> newest = _images != null ? _images.Newest(TileCount) : new CcdCapture[0];
            for (int i = 0; i < Tiles.Count; i++)
                Apply(Tiles[i], i < newest.Count ? newest[i] : null, i + 1);

            MarkSelectedTile();
        }

        /// <summary>Rings the tile that shows the image the detail pane has open, if it is still on screen.</summary>
        private void MarkSelectedTile()
        {
            string open = SelectedTile != null ? SelectedTile.ImagePath : null;
            foreach (CcdTile tile in Tiles)
                tile.IsSelected = open != null && string.Equals(tile.ImagePath, open, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Puts one capture on a tile, or clears the tile when there is none.</summary>
        private void Apply(CcdTile tile, CcdCapture capture, int slot)
        {
            if (capture == null)
            {
                tile.Name = "Image " + slot;
                tile.HasImage = false;
                tile.ImagePath = null;
                tile.Caption = string.Empty;
                tile.IsNg = false;
                tile.Information = BuildInformation();
                return;
            }

            // The same file again (a values file arrived) is read again, not skipped.
            tile.ImagePath = null;
            tile.ImagePath = capture.ImagePath;
            tile.HasImage = true;
            tile.Name = string.IsNullOrWhiteSpace(capture.UnitSn)
                ? System.IO.Path.GetFileNameWithoutExtension(capture.ImagePath)
                : capture.UnitSn;
            tile.IsNg = capture.Result == "NG" || capture.Result == "FAIL";
            tile.Caption = capture.Time.ToString("HH:mm:ss")
                + (string.IsNullOrWhiteSpace(capture.Result) ? string.Empty : "   " + capture.Result);

            var information = new List<SummaryRow>
            {
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
                // values file beside this image.
                foreach (SummaryRow row in BuildInformation())
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
        private static IReadOnlyList<SummaryRow> BuildInformation()
        {
            const string NotAvailable = "—";

            return new List<SummaryRow>
            {
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
