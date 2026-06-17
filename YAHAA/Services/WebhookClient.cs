using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    /// <summary>
    /// Fires Home Assistant webhooks for dashboard actions. The action's webhook may be a full URL
    /// (used as-is) or just a webhook id (resolved against the configured Home Assistant URL).
    /// </summary>
    public static class WebhookClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>POSTs (empty body) to the action's webhook. Returns true on a 2xx response.</summary>
        public static async Task<bool> TriggerAsync(string webhook, CancellationToken ct = default)
        {
            var url = ResolveUrl(webhook);
            if (url is null) return false;

            try
            {
                using var content = new StringContent(string.Empty);
                using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Turns a stored webhook value into an absolute URL: full URLs pass through, a bare id (or
        /// "/api/webhook/&lt;id&gt;" path) is combined with the configured Home Assistant URL.
        /// </summary>
        public static Uri? ResolveUrl(string webhook)
        {
            var value = webhook?.Trim() ?? string.Empty;
            if (value.Length == 0) return null;

            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.TryCreate(value, UriKind.Absolute, out var abs) ? abs : null;
            }

            var baseUrl = HomeAssistantClient.NormalizeUrl(ConfigStore.ServerUrl);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) return null;

            // Accept either a bare id or a "/api/webhook/<id>" path.
            var id = value.TrimStart('/');
            if (id.StartsWith("api/webhook/", StringComparison.OrdinalIgnoreCase))
                id = id["api/webhook/".Length..];

            return new Uri(baseUri, $"/api/webhook/{id}");
        }
    }
}
