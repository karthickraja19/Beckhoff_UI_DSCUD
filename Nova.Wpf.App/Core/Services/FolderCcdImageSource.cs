using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nova.Abstractions.Services;

namespace Nova.Core.Services
{
    /// <summary>
    /// <see cref="ICcdImageSource"/> over the folders the vision software saves its images
    /// to: one sub-folder per camera under a root (App.config VisionImageFolder), e.g.
    /// <c>D:\Images\CCD1\FVMDJ20X6G_20260928101500_OK.jpg</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>The newest image file of each camera folder (or any folder below it) is the camera's capture.</item>
    /// <item>Unit SN, time and OK/NG are read from a file name of the form SN_yyyyMMddHHmmss[_OK|_NG].</item>
    /// <item>A text file next to the image with the same name (<c>.txt</c>) holding
    /// <c>key=value</c> lines gives the capture's information (Angle, X_Gap, T1_Exposure, ...).</item>
    /// </list>
    /// The folders are watched, so the page follows the running units without polling.
    /// </remarks>
    public sealed class FolderCcdImageSource : ICcdImageSource, IDisposable
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };

        private static readonly Regex NamePattern = new Regex(
            @"^(?<sn>[^_]+)_(?<time>\d{14})(?:_(?<result>OK|NG|PASS|FAIL))?", RegexOptions.IgnoreCase);

        private readonly string _root;
        private readonly Dictionary<string, CcdCapture> _latest = new Dictionary<string, CcdCapture>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();
        private readonly FileSystemWatcher _watcher;

        public FolderCcdImageSource(string root, IEnumerable<string> cameraNames)
        {
            _root = root;
            var cameras = new List<string>(cameraNames ?? new string[0]);

            if (Directory.Exists(root))
            {
                foreach (string folder in Directory.GetDirectories(root).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    if (!cameras.Contains(folder, StringComparer.OrdinalIgnoreCase))
                        cameras.Add(folder);
                }

                foreach (string camera in cameras)
                    Rescan(camera);

                _watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                };
                _watcher.Created += OnFileChanged;
                _watcher.Renamed += OnFileChanged;
                _watcher.Changed += OnFileChanged;
                _watcher.EnableRaisingEvents = true;
            }

            Cameras = cameras;
        }

        public event EventHandler<string> CaptureChanged;

        public IReadOnlyList<string> Cameras { get; }

        public string Description
        {
            get
            {
                return Directory.Exists(_root)
                    ? "Images from " + _root
                    : "The image folder " + _root + " does not exist (App.config VisionImageFolder).";
            }
        }

        public CcdCapture Latest(string camera)
        {
            lock (_gate)
            {
                CcdCapture capture;
                return _latest.TryGetValue(camera ?? string.Empty, out capture) ? capture : null;
            }
        }

        public void Dispose()
        {
            if (_watcher != null)
                _watcher.Dispose();
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // On the watcher's thread: an exception here would end the application.
            try
            {
                Take(e);
            }
            catch (Exception)
            {
                // The next file the vision software writes is picked up as usual.
            }
        }

        private void Take(FileSystemEventArgs e)
        {
            if (!IsImage(e.FullPath) && !string.Equals(Path.GetExtension(e.FullPath), ".txt", StringComparison.OrdinalIgnoreCase))
                return;

            string camera = CameraOf(e.FullPath);
            if (camera == null)
                return;

            string image = IsImage(e.FullPath) ? e.FullPath : ImageBeside(e.FullPath);
            if (image == null || !WaitUntilReadable(image))
                return;

            CcdCapture capture = Read(camera, image);
            lock (_gate)
            {
                CcdCapture current;
                if (_latest.TryGetValue(camera, out current) && current.Time > capture.Time
                    && !string.Equals(current.ImagePath, image, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _latest[camera] = capture;
            }

            CaptureChanged?.Invoke(this, camera);
        }

        private void Rescan(string camera)
        {
            try
            {
                string folder = Path.Combine(_root, camera);
                if (!Directory.Exists(folder))
                    return;

                FileInfo newest = new DirectoryInfo(folder)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => IsImage(f.FullName))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (newest != null)
                {
                    lock (_gate)
                    {
                        _latest[camera] = Read(camera, newest.FullName);
                    }
                }
            }
            catch (Exception)
            {
                // A folder that cannot be read shows its camera without a frame.
            }
        }

        /// <summary>The camera a file belongs to: the first folder below the root.</summary>
        private string CameraOf(string path)
        {
            string relative = path.Length > _root.Length ? path.Substring(_root.Length).TrimStart('\\', '/') : string.Empty;
            int slash = relative.IndexOfAny(new[] { '\\', '/' });
            return slash > 0 ? relative.Substring(0, slash) : null;
        }

        private static CcdCapture Read(string camera, string image)
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
                Camera = camera,
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
