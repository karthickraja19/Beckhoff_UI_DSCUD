using System;
using System.Net.Sockets;
using System.Threading;
using Nova.Abstractions.Services;

namespace Nova.Hive
{
    /// <summary>
    /// Whether the HIVE gateway can be reached, for the station card's link row
    /// (Audio SW DFM v5.4 slide 43). Slide 39's HIVE chip also has a "HIVE is disabled
    /// or disconnected" state, which is what this reports when it is not reachable.
    /// </summary>
    /// <remarks>
    /// Payloads only go out on a state change, which can be hours apart, so the link is
    /// checked on its own: a TCP connect to the gateway's address every 30 seconds.
    /// </remarks>
    public sealed class HiveLinkStatus : IServiceLinkStatus, IDisposable
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
        private const int ConnectTimeoutMs = 3000;

        private readonly HiveSettings _settings;
        private readonly Timer _timer;
        private int _checking;

        public HiveLinkStatus(HiveSettings settings)
        {
            _settings = settings;

            if (!settings.Enabled)
            {
                State = LinkState.Disabled;
                Detail = "HIVE is switched off in App.config (HiveEnabled). Nothing is sent to the gateway.";
                return;
            }

            Detail = "Checking " + settings.GatewayUrl + "...";
            _timer = new Timer(_ => Check(), null, TimeSpan.Zero, CheckInterval);
        }

        public string Name => "HIVE";

        public LinkState State { get; private set; }

        public string Detail { get; private set; }

        public event EventHandler Changed;

        public void Dispose()
        {
            if (_timer != null)
                _timer.Dispose();
        }

        private void Check()
        {
            if (Interlocked.Exchange(ref _checking, 1) == 1)
                return;

            try
            {
                Uri gateway;
                if (!Uri.TryCreate(_settings.GatewayUrl, UriKind.Absolute, out gateway))
                {
                    Set(LinkState.Disconnected, "The gateway address in App.config is not a URL: " + _settings.GatewayUrl);
                    return;
                }

                bool reachable;
                using (var client = new TcpClient())
                {
                    try
                    {
                        reachable = client.ConnectAsync(gateway.Host, gateway.Port).Wait(ConnectTimeoutMs) && client.Connected;
                    }
                    catch (AggregateException)
                    {
                        reachable = false;
                    }
                }

                Set(reachable ? LinkState.Connected : LinkState.Disconnected,
                    (reachable ? "Gateway reachable: " : "No answer from the gateway: ") + _settings.GatewayUrl
                    + " (checked " + DateTime.Now.ToString("HH:mm:ss") + ")");
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
            }
        }

        private void Set(LinkState state, string detail)
        {
            bool changed = state != State || detail != Detail;
            State = state;
            Detail = detail;

            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
