using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="ICcdImageSource"/> over the folder the vision software saves its images
    /// to (App.config VisionImageFolder): the newest image files in it, or in any folder
    /// below it, newest first. It does not need a folder per camera.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Unit SN, time and OK/NG are read from a file name of the form
    /// <c>SN_yyyyMMddHHmmss[_OK|_NG]</c> when it has that form; otherwise the image's time is
    /// when its file was written, and the unit and result are unknown.</item>
    /// <item>A text file next to the image with the same name (<c>.txt</c>) holding
    /// <c>key=value</c> lines gives the capture's information (Angle, X_Gap, T1_Exposure, ...).</item>
    /// </list>
    /// The folder is watched, so the page follows the running units without polling. Only
    /// the newest <see cref="Keep"/> images are held; the folder is scanned in the
    /// background at start, so a large archive does not hold up the application.
    /// </remarks>
    public sealed class FolderCcdImageSource : ICcdImageSource, IDisposable
    {
        /// <summary>How many of the newest images are held.</summary>
        private const int Keep = 60;

        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        private static readonly Regex NamePattern = new Regex(
            @"^(?<sn>[^_]+)_(?<time>\d{14})(?:_(?<result>OK|NG|PASS|FAIL))?", RegexOptions.IgnoreCase);

        private readonly string _root;
        private readonly List<CcdCapture> _newest = new List<CcdCapture>();   // newest first
        private readonly object _gate = new object();
        private readonly FileSystemWatcher _watcher;
        private int _scanning;

        public FolderCcdImageSource(string root)
        {
            _root = root ?? string.Empty;
            if (!Directory.Exists(_root))
                return;

            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.EnableRaisingEvents = true;

            Rescan();
        }

        public event EventHandler ImagesChanged;

        public string Description
        {
            get
            {
                return Directory.Exists(_root)
                    ? "Newest images from " + _root
                    : "The image folder " + _root + " does not exist (App.config VisionImageFolder).";
            }
        }

        public IReadOnlyList<CcdCapture> Newest(int count)
        {
            lock (_gate)
            {
                return _newest.Take(Math.Max(0, count)).ToList();
            }
        }

        public void Dispose()
        {
            if (_watcher != null)
                _watcher.Dispose();
        }

        /// <summary>Reads the newest images of the folder, in the background.</summary>
        private void Rescan()
        {
            if (System.Threading.Interlocked.Exchange(ref _scanning, 1) != 0)
                return;

            Task.Run(() =>
            {
                try
                {
                    List<CcdCapture> found = new DirectoryInfo(_root)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Where(f => IsImage(f.FullName))
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Take(Keep)
                        .Select(f => Read(f.FullName))
                        .ToList();

                    lock (_gate)
                    {
                        // Images that arrived while scanning are already held: keep them too.
                        foreach (CcdCapture capture in found)
                            Add(capture);
                    }
                }
                catch (Exception)
                {
                    // A folder that cannot be read shows no images until the next file arrives.
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _scanning, 0);
                }

                Raise();
            });
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // On the watcher's thread: an exception here would end the application.
            try
            {
                Take(e.FullPath);
            }
            catch (Exception)
            {
                // The next file the vision software writes is picked up as usual.
            }
        }

        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            try
            {
                if (!IsImage(e.FullPath))
                    return;

                int left;
                lock (_gate)
                {
                    _newest.RemoveAll(c => string.Equals(c.ImagePath, e.FullPath, StringComparison.OrdinalIgnoreCase));
                    left = _newest.Count;
                }

                Raise();

                // The software cleans up old images: look again for older ones to show.
                if (left < Keep / 2)
                    Rescan();
            }
            catch (Exception)
            {
                // Nothing to do: the list is corrected at the next change.
            }
        }

        private void Take(string path)
        {
            bool isImage = IsImage(path);
            if (!isImage && !string.Equals(Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
                return;

            string image = isImage ? path : ImageBeside(path);
            if (image == null || !WaitUntilReadable(image))
                return;

            CcdCapture capture = Read(image);
            lock (_gate)
            {
                Add(capture);
            }

            Raise();
        }

        /// <summary>Puts the capture in its place, replacing an earlier read of the same file. Call under the lock.</summary>
        private void Add(CcdCapture capture)
        {
            _newest.RemoveAll(c => string.Equals(c.ImagePath, capture.ImagePath, StringComparison.OrdinalIgnoreCase));
            _newest.Add(capture);
            _newest.Sort((a, b) =>
            {
                int byTime = b.Time.CompareTo(a.Time);
                return byTime != 0 ? byTime : string.Compare(b.ImagePath, a.ImagePath, StringComparison.OrdinalIgnoreCase);
            });

            if (_newest.Count > Keep)
                _newest.RemoveRange(Keep, _newest.Count - Keep);
        }

        private void Raise()
        {
            try
            {
                ImagesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // A faulty screen must not stop the watcher.
            }
        }

        private static CcdCapture Read(string image)
        {
            string name = Path.GetFileNameWithoutExtension(image) ?? string.Empty;
            Match match = NamePattern.Match(name);

            DateTime time;
            if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value, "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                time = SafeWriteTime(image);
            }

            return new CcdCapture
            {
                ImagePath = image,
                Time = time,
                UnitSn = match.Success ? match.Groups["sn"].Value : null,
                Result = match.Success && match.Groups["result"].Success ? match.Groups["result"].Value.ToUpperInvariant() : null,
                Information = ReadSidecar(Path.ChangeExtension(image, ".txt")),
            };
        }

        private static IReadOnlyList<KeyValuePair<string, string>> ReadSidecar(string path)
        {
            var information = new List<KeyValuePair<string, string>>();
            try
            {
                if (!File.Exists(path))
                    return information;

                foreach (string line in File.ReadAllLines(path))
                {
                    int split = line.IndexOfAny(new[] { '=', ':', ',' });
                    if (split > 0)
                        information.Add(new KeyValuePair<string, string>(line.Substring(0, split).Trim(), line.Substring(split + 1).Trim()));
                }
            }
            catch (IOException)
            {
                // Still being written: the next change event reads it again.
            }

            return information;
        }

        private static string ImageBeside(string textFile)
        {
            foreach (string extension in ImageExtensions)
            {
                string image = Path.ChangeExtension(textFile, extension);
                if (File.Exists(image))
                    return image;
            }

            return null;
        }

        /// <summary>
        /// The watcher reports a file as soon as it is created, while the vision software is
        /// still writing it. Waits (up to 2 s) until it can be opened on its own.
        /// </summary>
        private static bool WaitUntilReadable(string path)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        return true;
                }
                catch (FileNotFoundException)
                {
                    return false;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsImage(string path)
        {
            return ImageExtensions.Contains(Path.GetExtension(path) ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        private static DateTime SafeWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTime(path);
            }
            catch (Exception)
            {
                return DateTime.Now;
            }
        }
    }
}
