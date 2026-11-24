using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Teal.Library.Utilities;

namespace Master.Sequences.FiveAxis
{
    /// <summary>The vision system the sequence takes its 34 pictures with.</summary>
    public interface IFiveAxisVision : IDisposable
    {
        Task ConnectAsync(CancellationToken token);

        /// <summary>Asks for image <paramref name="index"/> (1 to 34) and returns the dots found.</summary>
        Task<VisionCapture> CaptureAsync(int index, CancellationToken token);
    }

    /// <summary>
    /// The CCD1 protocol of Five-Axis Calibration.xlsx over <see cref="SocketClient"/>: the HMI sends
    /// <c>C5A,index</c> (index 1 to 34) and the vision system answers
    /// <c>C5A,1/0,index,U1,V1;U2,V2;...;U49,V49;</c> (1 = positioning OK, 0 = NG). Points are
    /// "u,v" pairs separated by ";".
    /// </summary>
    public sealed class C5AVisionClient : IFiveAxisVision
    {
        /// <summary>The plate has 7 x 7 dots (P1 .. P49).</summary>
        public const int PointCount = 49;

        private const int QuietMs = 300;

        private readonly FiveAxisSettings _settings;
        private readonly SocketClient _socket;

        public C5AVisionClient(FiveAxisSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _socket = new SocketClient(settings.VisionIp, settings.VisionPort, "CCD1");
        }

        public async Task ConnectAsync(CancellationToken token)
        {
            if (_socket.IsConnected)
                return;

            token.ThrowIfCancellationRequested();
            if (!await _socket.ConnectAsync().ConfigureAwait(false))
                throw new InvalidOperationException("Cannot connect to the vision system at " + _settings.VisionIp + ":" + _settings.VisionPort + ".");
        }

        public async Task<VisionCapture> CaptureAsync(int index, CancellationToken token)
        {
            string last = string.Empty;
            for (int attempt = 0; attempt <= Math.Max(0, _settings.VisionRetries); attempt++)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    string reply = await ExchangeAsync(index, token).ConfigureAwait(false);

                    VisionCapture capture;
                    string problem;
                    if (!TryParse(reply, index, out capture, out problem))
                        last = problem;
                    else if (!capture.Ok)
                        last = "positioning NG (result 0)";
                    else if (capture.Points.Length != PointCount)
                        last = "found " + capture.Points.Length + " points, expected " + PointCount;
                    else
                        return capture;
                }
                catch (TimeoutException ex)
                {
                    last = ex.Message;
                }

                FiveAxisLog.Error("Vision", "Image " + index + " attempt " + (attempt + 1) + ": " + last, string.Empty);
            }

            throw new InvalidOperationException("The vision system gave no usable answer for image " + index + ": " + last + ".");
        }

        public void Dispose()
        {
            _socket.Disconnect();
        }

        private async Task<string> ExchangeAsync(int index, CancellationToken token)
        {
            if (!_socket.Send("C5A," + index.ToString(CultureInfo.InvariantCulture) + (_settings.VisionTerminator ?? string.Empty)))
                throw new InvalidOperationException("The vision connection at " + _settings.VisionIp + ":" + _settings.VisionPort + " is closed.");

            var reply = new StringBuilder();
            var clock = Stopwatch.StartNew();
            long lastData = 0;

            while (clock.ElapsedMilliseconds < _settings.VisionTimeoutMs)
            {
                token.ThrowIfCancellationRequested();

                string chunk = _socket.Receive(65536);
                if (!string.IsNullOrEmpty(chunk))
                {
                    reply.Append(chunk);
                    lastData = clock.ElapsedMilliseconds;
                    if (IsComplete(reply.ToString()))
                        return reply.ToString();
                }
                else if (reply.Length > 0 && clock.ElapsedMilliseconds - lastData > QuietMs)
                {
                    // An NG answer has no points to count: it is complete when nothing more comes.
                    return reply.ToString();
                }

                await Task.Delay(10, token).ConfigureAwait(false);
            }

            if (reply.Length > 0)
                return reply.ToString();

            throw new TimeoutException("no answer within " + _settings.VisionTimeoutMs + " ms");
        }

        /// <summary>A positioning-OK answer is complete with its 49th point.</summary>
        private static bool IsComplete(string reply)
        {
            VisionCapture capture;
            string problem;
            return TryParse(reply, 0, out capture, out problem, ignoreIndex: true)
                && capture.Ok && capture.Points.Length >= PointCount;
        }

        /// <summary>Reads "C5A,result,index,u,v;u,v;...". <paramref name="problem"/> says what is wrong when it returns false.</summary>
        internal static bool TryParse(string reply, int expectedIndex, out VisionCapture capture, out string problem, bool ignoreIndex = false)
        {
            capture = null;
            problem = string.Empty;

            string text = (reply ?? string.Empty).Trim('\0', ' ', '\r', '\n', '\t');
            int at = text.IndexOf("C5A,", StringComparison.Ordinal);
            if (at < 0)
            {
                problem = "the answer does not start with C5A (\"" + Shorten(text) + "\")";
                return false;
            }
            text = text.Substring(at).Trim();

            string[] head = text.Split(new[] { ',' }, 4);
            int result, index;
            if (head.Length < 3
                || !int.TryParse(head[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                || !int.TryParse(head[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                problem = "the answer has no result and image number (\"" + Shorten(text) + "\")";
                return false;
            }

            if (!ignoreIndex && index != expectedIndex)
            {
                problem = "the answer is for image " + index + ", not " + expectedIndex;
                return false;
            }

            var points = new System.Collections.Generic.List<double[]>();
            string rest = head.Length == 4 ? head[3] : string.Empty;
            foreach (string pair in rest.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] uv = pair.Split(',');
                double u, v;
                if (uv.Length != 2
                    || !double.TryParse(uv[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out u)
                    || !double.TryParse(uv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                {
                    // The last pair of a half-received answer; the caller keeps waiting or reports the count.
                    break;
                }
                points.Add(new[] { u, v });
            }

            // Every pair ends with ";" (the protocol sheet's "U49,V49;"). One that does not may be cut short, and a cut
            // number still parses, so it is not trusted: the count then comes out short and the capture is asked for again.
            if (points.Count > 0 && !rest.TrimEnd().EndsWith(";", StringComparison.Ordinal))
                points.RemoveAt(points.Count - 1);

            capture = new VisionCapture { Index = index, Ok = result == 1, Points = points.ToArray() };
            return true;
        }

        private static string Shorten(string text)
        {
            return text.Length <= 60 ? text : text.Substring(0, 60) + "...";
        }
    }
}

