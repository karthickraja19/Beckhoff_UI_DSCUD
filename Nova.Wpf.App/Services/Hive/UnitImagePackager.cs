using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nova.Abstractions.Services;
using Nova.Hive;
using Teal.Library.Contracts.Models;
using Teal.Library.Log;

namespace Nova.Wpf.App.Services
{
    /// <summary>
    /// A finished unit's AOI images for Insight (Audio Picture Upload Quantity Check T5 X7;
    /// DFM slides 19 and 20): found, counted against the picture quantity limits, and sent
    /// with the unit's machine_data as one zip.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The images are the files under App.config <c>UnitImageFolder</c> whose name, or
    /// whose folder's name, contains the unit's serial number. Where the vision software
    /// puts them and how it names them is still to be confirmed (plan Q8).</item>
    /// <item>The count goes to <see cref="IPictureQuantityCheck.ReportUnit"/> before the zip
    /// is made: it writes Pic_Upload_Record and, for a count outside the limits, puts the
    /// machine down.</item>
    /// <item>The zip is named <c>UnitSN_Project_Line_Station_Instance_yyyyMMddHHmmss.zip</c>
    /// (slide 20) and added to the unit's blobs. A unit with no image gets no zip: an empty
    /// file name makes Insight refuse the upload (slide 19).</item>
    /// <item>With <c>UnitImageFolder</c> blank nothing is counted and nothing is zipped; the
    /// startup warnings say so.</item>
    /// </list>
    /// </remarks>
    public sealed class UnitImagePackager
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        private readonly string _imageFolder;
        private readonly string _zipFolder;
        private readonly IPictureQuantityCheck _check;
        private readonly IStationProfileService _profile;
        private readonly Func<string> _project;

        /// <param name="imageFolder">Where the vision software saves the unit images; blank = not available.</param>
        /// <param name="zipFolder">Where the zips are written; blank = "HiveZip" under the image folder.</param>
        public UnitImagePackager(string imageFolder, string zipFolder, IPictureQuantityCheck check,
            IStationProfileService profile, Func<string> project)
        {
            _imageFolder = (imageFolder ?? string.Empty).Trim();
            _zipFolder = string.IsNullOrWhiteSpace(zipFolder)
                ? (_imageFolder.Length > 0 ? Path.Combine(_imageFolder, "HiveZip") : string.Empty)
                : zipFolder.Trim();
            _check = check;
            _profile = profile;
            _project = project;

            if (_imageFolder.Length == 0)
            {
                UnavailableReason = "UnitImageFolder is blank in App.config, so a unit's AOI images are not counted "
                    + "(Picture Qty. Check) and no image zip goes to HIVE with the unit.";
            }
        }

        /// <summary>Why no unit's images are handled, or null when the folder is configured.</summary>
        public string UnavailableReason { get; }

        /// <summary>
        /// Counts the unit's images, records the count, and adds their zip to the unit's
        /// payload. Never throws: a unit without its images still goes to HIVE.
        /// </summary>
        public void Attach(HiveUnitData unit)
        {
            if (unit == null || UnavailableReason != null || string.IsNullOrWhiteSpace(unit.UnitSn))
                return;

            try
            {
                List<string> images = Find(unit.UnitSn.Trim());

                // T5: the quantity is checked before the zip is made.
                if (_check != null)
                    _check.ReportUnit(unit.UnitSn, images.Count);

                if (images.Count == 0)
                {
                    Log(LogType.Error, "[Error/Alert] No AOI image of unit " + unit.UnitSn + " was found in " + _imageFolder + ": no zip goes to HIVE.");
                    return;
                }

                string zip = Zip(unit, images);
                unit.BlobFiles.Add(zip);
            }
            catch (Exception ex)
            {
                Log(LogType.Error, "[Error/Alert] The AOI images of unit " + unit.UnitSn + " could not be packed: " + ex.Message);
            }
        }

        /// <summary>The image files named after the unit, or lying in a folder named after it.</summary>
        private List<string> Find(string serial)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(_imageFolder))
                throw new DirectoryNotFoundException("The image folder " + _imageFolder + " does not exist.");

            string pattern = "*" + serial + "*";
            foreach (string file in Directory.EnumerateFiles(_imageFolder, pattern, SearchOption.AllDirectories))
            {
                if (IsImage(file))
                    found.Add(file);
            }

            foreach (string folder in Directory.EnumerateDirectories(_imageFolder, pattern, SearchOption.AllDirectories))
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    if (IsImage(file))
                        found.Add(file);
                }
            }

            return found.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsImage(string file)
        {
            string extension = Path.GetExtension(file) ?? string.Empty;
            return ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Slide 20: UnitSN_Project_Line_Station_Instance_yyyyMMddHHmmss.zip.</summary>
        private string Zip(HiveUnitData unit, List<string> images)
        {
            StationProfile profile = _profile != null ? _profile.Current : null;
            string name = string.Join("_", new[]
            {
                Clean(unit.UnitSn),
                Clean(_project != null ? _project() : null),
                Clean(profile != null ? profile.Line : null),
                Clean(profile != null ? profile.Station : null),
                profile != null ? profile.MachineNumber.ToString() : "0",
                unit.OutputTime.ToString("yyyyMMddHHmmss"),
            }) + ".zip";

            Directory.CreateDirectory(_zipFolder);
            string path = Path.Combine(_zipFolder, name);
            if (File.Exists(path))
                File.Delete(path);

            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string image in images)
                {
                    // Two images of the same name from different folders both go in.
                    string entry = Path.GetFileName(image);
                    for (int copy = 2; !used.Add(entry); copy++)
                        entry = Path.GetFileNameWithoutExtension(image) + "_" + copy + Path.GetExtension(image);

                    archive.CreateEntryFromFile(image, entry, CompressionLevel.Fastest);
                }
            }

            Log(LogType.Info, "Unit " + unit.UnitSn + ": " + images.Count + " AOI image(s) packed into " + path + ".");
            return path;
        }

        /// <summary>A name part without the characters a file name or the "_" separator cannot carry.</summary>
        private static string Clean(string part)
        {
            string text = (part ?? string.Empty).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                text = text.Replace(invalid.ToString(), string.Empty);

            return text.Replace(" ", string.Empty);
        }

        private static void Log(LogType type, string message)
        {
            Logger.WriteLog(type, "Vision", "UnitImages", message, string.Empty);
        }
    }
}
