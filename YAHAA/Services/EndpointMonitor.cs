using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    /// <summary>
    /// Keeps <see cref="ConfigStore.ServerUrl"/> pointed at the best Home Assistant endpoint:
    /// when an internal (local-network) URL is configured, it is probed periodically and used while
    /// reachable; otherwise the external URL is used. This lets the app talk to HA locally when home
    /// (so e.g. local-only webhooks work) and fall back to the external URL when away.
    /// </summary>
    public static class EndpointMonitor
    {
        private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
        private static readonly object Gate = new();

        private static CancellationTokenSource? _cts;
        private static Task? _loop;

        public static void Start()
        {
            lock (Gate)
            {
                if (_loop is { IsCompleted: false }) return;
                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => RunAsync(_cts.Token));
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                _cts?.Cancel();
                _cts = null;
            }
        }

        /// <summary>Forces an immediate re-probe (e.g. after the connection settings change).</summary>
        public static void Refresh()
        {
            Stop();
            Start();
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (ConfigStore.IsConfigured && !string.IsNullOrWhiteSpace(ConfigStore.InternalUrl))
                    {
                        var reachable = await ProbeAsync(ConfigStore.InternalUrl, ConfigStore.Token, ct).ConfigureAwait(false);
                        ConfigStore.UseInternalEndpoint(reachable);
                    }
                    else
                    {
                        ConfigStore.UseInternalEndpoint(false);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception)
                {
                    // A bad probe or a throwing endpoint-changed handler must not stop monitoring.
                }

                try { await Task.Delay(ProbeInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        // Reachable = Home Assistant answered at this URL (200 with the token, or 401 if it's there
        // but rejects auth). Transport failures / timeouts mean not reachable.
        private static async Task<bool> ProbeAsync(string url, string token, CancellationToken ct)
        {
            var baseUrl = HomeAssistantClient.NormalizeUrl(url);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return false;

            try
            {
                using var http = new HttpClient { Timeout = ProbeTimeout };
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/"));
                if (!string.IsNullOrEmpty(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

                using var resp = await http.SendAsync(request, ct).ConfigureAwait(false);
                return resp.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return false; // timeout / DNS / connection refused → not on the local network
            }
        }
    }
}
