using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    /// <summary>
    /// Minimal Home Assistant REST client used to validate a connection during setup.
    /// Authentication uses a long-lived access token (Bearer), which is how the HA REST API works.
    /// </summary>
    public static class HomeAssistantClient
    {
        /// <summary>
        /// Cleans up a user-entered URL: trims whitespace and a trailing slash, and assumes
        /// http:// when no scheme is supplied (most local instances use http on :8123).
        /// </summary>
        public static string NormalizeUrl(string? url)
        {
            url = (url ?? string.Empty).Trim();
            if (url.Length == 0) return url;

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            return url.TrimEnd('/');
        }

        /// <summary>
        /// Lightweight reachability probe used by the "Server" step. Any HTTP response (even a 401)
        /// means a server is answering at that address; only transport failures are treated as errors.
        /// </summary>
        public static async Task<ConnectionResult> CheckReachableAsync(string url, CancellationToken ct = default)
        {
            var baseUrl = NormalizeUrl(url);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
                return ConnectionResult.Fail("That doesn't look like a valid URL.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            try
            {
                using var resp = await http.GetAsync(new Uri(baseUri, "/api/"), ct).ConfigureAwait(false);
                // 401 is expected here (no token yet) and still proves the server is reachable.
                return ConnectionResult.Ok(null, null);
            }
            catch (TaskCanceledException)
            {
                return ConnectionResult.Fail("The connection timed out. Is the address reachable from this PC?");
            }
            catch (HttpRequestException ex)
            {
                return ConnectionResult.Fail($"Couldn't reach that address: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates the full connection by calling the authenticated /api/config endpoint.
        /// On success it returns the instance's friendly name and version.
        /// </summary>
        public static async Task<ConnectionResult> ValidateAsync(string url, string token, CancellationToken ct = default)
        {
            var baseUrl = NormalizeUrl(url);
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
                return ConnectionResult.Fail("That doesn't look like a valid URL.");

            if (string.IsNullOrWhiteSpace(token))
                return ConnectionResult.Fail("A long-lived access token is required.");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/api/config"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

            try
            {
                using var resp = await http.SendAsync(request, ct).ConfigureAwait(false);

                if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                    return ConnectionResult.Fail("The access token was rejected. Double-check your long-lived access token.");

                if (!resp.IsSuccessStatusCode)
                    return ConnectionResult.Fail($"Home Assistant responded with {(int)resp.StatusCode} {resp.ReasonPhrase}.");

                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                string? location = null, version = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("location_name", out var l)) location = l.GetString();
                    if (doc.RootElement.TryGetProperty("version", out var v)) version = v.GetString();
                }
                catch (JsonException)
                {
                    // A 200 with an unexpected body is still a working connection.
                }

                return ConnectionResult.Ok(location, version);
            }
            catch (TaskCanceledException)
            {
                return ConnectionResult.Fail("The connection timed out. Is the URL reachable from this PC?");
            }
            catch (HttpRequestException ex)
            {
                return ConnectionResult.Fail($"Couldn't reach Home Assistant: {ex.Message}");
            }
        }
    }
}
