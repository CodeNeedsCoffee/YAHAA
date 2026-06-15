using System;
using System.IO;
using System.Text.Json;
using YAHAA.Common;

namespace YAHAA.Services
{
    /// <summary>
    /// Persists the mobile_app registration: a stable device id, the webhook id returned by Home
    /// Assistant (encrypted with DPAPI), and whether sensors have been registered yet.
    /// Stored separately from the connection config so the device id survives reconnects.
    /// </summary>
    public static class RegistrationStore
    {
        private sealed class Stored
        {
            public string DeviceId { get; set; } = string.Empty;
            public string WebhookId { get; set; } = string.Empty; // DPAPI-encrypted
            public bool SensorsRegistered { get; set; }
        }

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YAHAA");

        private static readonly string FilePath = Path.Combine(Folder, "registration.json");

        public static string DeviceId { get; private set; } = string.Empty;
        public static string? WebhookId { get; private set; }
        public static bool SensorsRegistered { get; private set; }

        /// <summary>True once the device has a webhook (i.e. it is registered with Home Assistant).</summary>
        public static bool IsRegistered => !string.IsNullOrEmpty(WebhookId);

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                    if (stored is not null)
                    {
                        DeviceId = stored.DeviceId;
                        var webhook = string.IsNullOrEmpty(stored.WebhookId) ? null : Dpapi.Unprotect(stored.WebhookId);
                        WebhookId = string.IsNullOrEmpty(webhook) ? null : webhook;
                        SensorsRegistered = stored.SensorsRegistered;
                    }
                }
            }
            catch
            {
                DeviceId = string.Empty;
                WebhookId = null;
                SensorsRegistered = false;
            }

            if (string.IsNullOrEmpty(DeviceId))
            {
                DeviceId = Guid.NewGuid().ToString("N");
                Save();
            }
        }

        public static void SetWebhook(string webhookId)
        {
            WebhookId = webhookId;
            SensorsRegistered = false;
            Save();
        }

        public static void SetSensorsRegistered(bool value)
        {
            SensorsRegistered = value;
            Save();
        }

        /// <summary>Forgets the webhook (e.g. on sign out, or when Home Assistant reports it invalid).</summary>
        public static void ClearWebhook()
        {
            WebhookId = null;
            SensorsRegistered = false;
            Save();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var stored = new Stored
                {
                    DeviceId = DeviceId,
                    WebhookId = string.IsNullOrEmpty(WebhookId) ? string.Empty : Dpapi.Protect(WebhookId),
                    SensorsRegistered = SensorsRegistered,
                };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));
            }
            catch
            {
                // Best-effort persistence.
            }
        }
    }
}
