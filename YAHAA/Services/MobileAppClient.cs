using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    public enum WebhookResult
    {
        Ok,
        Failed,

        /// <summary>The webhook no longer exists on the Home Assistant side; re-registration is required.</summary>
        WebhookInvalid,
    }

    /// <summary>Describes a binary sensor reported to Home Assistant.</summary>
    public sealed record SensorDefinition(string UniqueId, string Name, string OnIcon, string OffIcon)
    {
        public string IconFor(bool state) => state ? OnIcon : OffIcon;
    }

    /// <summary>A single sensor's value for an update.</summary>
    public readonly record struct SensorReading(string UniqueId, bool State, string Icon);

    public sealed class RegistrationResult
    {
        public bool Success { get; init; }
        public string? WebhookId { get; init; }
        public string? ErrorMessage { get; init; }

        public static RegistrationResult Ok(string webhookId) => new() { Success = true, WebhookId = webhookId };
        public static RegistrationResult Fail(string message) => new() { Success = false, ErrorMessage = message };
    }

    /// <summary>
    /// Talks to Home Assistant's mobile_app integration: registers this device (returning a webhook),
    /// then registers and updates sensors through that webhook — the same protocol the official
    /// companion apps use.
    /// </summary>
    public static class MobileAppClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public static async Task<RegistrationResult> RegisterDeviceAsync(
            string baseUrl, string token, DeviceIdentity device, CancellationToken ct = default)
        {
            var url = HomeAssistantClient.NormalizeUrl(baseUrl);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
                return RegistrationResult.Fail("Invalid Home Assistant URL.");

            var payload = new
            {
                device_id = device.DeviceId,
                app_id = "yahaa",
                app_name = "YAHAA",
                app_version = device.AppVersion,
                device_name = device.DeviceName,
                manufacturer = device.Manufacturer,
                model = device.Model,
                os_name = device.OsName,
                os_version = device.OsVersion,
                supports_encryption = false,
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/api/mobile_app/registrations"))
            {
                Content = Json(payload),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

            try
            {
                using var resp = await Http.SendAsync(request, ct).ConfigureAwait(false);

                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    return RegistrationResult.Fail("The access token was rejected.");
                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return RegistrationResult.Fail("The mobile_app integration isn't available on this Home Assistant.");
                if (!resp.IsSuccessStatusCode)
                    return RegistrationResult.Fail($"Home Assistant responded with {(int)resp.StatusCode}.");

                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("webhook_id", out var wh) && wh.GetString() is { Length: > 0 } id)
                    return RegistrationResult.Ok(id);

                return RegistrationResult.Fail("Registration response did not include a webhook id.");
            }
            catch (TaskCanceledException)
            {
                return RegistrationResult.Fail("The registration request timed out.");
            }
            catch (HttpRequestException ex)
            {
                return RegistrationResult.Fail($"Couldn't reach Home Assistant: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes the device registration (device name, app version, etc.) so Home Assistant's
        /// device diagnostics stay accurate. Uses the mobile_app "update_registration" webhook.
        /// </summary>
        public static Task<WebhookResult> UpdateRegistrationAsync(
            string baseUrl, string webhookId, DeviceIdentity device, CancellationToken ct = default)
        {
            var payload = new
            {
                type = "update_registration",
                data = new
                {
                    app_version = device.AppVersion,
                    device_name = device.DeviceName,
                    manufacturer = device.Manufacturer,
                    model = device.Model,
                    os_version = device.OsVersion,
                },
            };
            return PostWebhookAsync(baseUrl, webhookId, payload, ct);
        }

        public static Task<WebhookResult> RegisterSensorAsync(
            string baseUrl, string webhookId, SensorDefinition def, bool state, bool disabled = false,
            CancellationToken ct = default)
        {
            var payload = new
            {
                type = "register_sensor",
                data = new
                {
                    unique_id = def.UniqueId,
                    name = def.Name,
                    type = "binary_sensor",
                    state,
                    icon = def.IconFor(state),
                    disabled,
                },
            };
            return PostWebhookAsync(baseUrl, webhookId, payload, ct);
        }

        public static Task<WebhookResult> UpdateSensorStatesAsync(
            string baseUrl, string webhookId, IReadOnlyList<SensorReading> readings, CancellationToken ct = default)
        {
            var data = new object[readings.Count];
            for (var i = 0; i < readings.Count; i++)
            {
                var r = readings[i];
                // "type" is required on every entry; Home Assistant silently drops the whole
                // update (HTTP 200, logged error) if it's missing. All YAHAA sensors are binary.
                data[i] = new { type = "binary_sensor", unique_id = r.UniqueId, state = r.State, icon = r.Icon };
            }

            var payload = new { type = "update_sensor_states", data };
            return PostWebhookAsync(baseUrl, webhookId, payload, ct);
        }

        /// <summary>
        /// Sets the given sensors to a null state, which Home Assistant renders as "unknown"
        /// (used when the app is shutting down so stale "on"/"active" values don't linger).
        /// </summary>
        public static Task<WebhookResult> SetSensorsUnknownAsync(
            string baseUrl, string webhookId, IEnumerable<string> uniqueIds, CancellationToken ct = default)
        {
            var data = uniqueIds
                .Select(id => (object)new { type = "binary_sensor", unique_id = id, state = (object?)null })
                .ToArray();

            var payload = new { type = "update_sensor_states", data };
            return PostWebhookAsync(baseUrl, webhookId, payload, ct);
        }

        private static async Task<WebhookResult> PostWebhookAsync(
            string baseUrl, string webhookId, object payload, CancellationToken ct)
        {
            var url = HomeAssistantClient.NormalizeUrl(baseUrl);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
                return WebhookResult.Failed;

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"/api/webhook/{webhookId}"))
            {
                Content = Json(payload),
            };

            try
            {
                using var resp = await Http.SendAsync(request, ct).ConfigureAwait(false);
                if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    return WebhookResult.WebhookInvalid;
                return resp.IsSuccessStatusCode ? WebhookResult.Ok : WebhookResult.Failed;
            }
            catch (TaskCanceledException)
            {
                return WebhookResult.Failed;
            }
            catch (HttpRequestException)
            {
                return WebhookResult.Failed;
            }
        }

        private static StringContent Json(object payload) =>
            new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }
}
