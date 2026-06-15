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
            public int RegisteredSensorsVersion { get; set; }
        }

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YAHAA");

        private static readonly string FilePath = Path.Combine(Folder, "registration.json");

        public static string DeviceId { get; private set; } = string.Empty;
        public static string? WebhookId { get; private set; }

        /// <summary>The sensor-set version last registered with Home Assistant (0 = none).</summary>
        public static int RegisteredSensorsVersion { get; private set; }

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
                        RegisteredSensorsVersion = stored.RegisteredSensorsVersion;
                    }
                }
            }
            catch
            {
                DeviceId = string.Empty;
                WebhookId = null;
                RegisteredSensorsVersion = 0;
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
            RegisteredSensorsVersion = 0;
            Save();
        }

        public static void SetSensorsVersion(int version)
        {
            RegisteredSensorsVersion = version;
            Save();
        }

        /// <summary>Forgets the webhook (e.g. on sign out, or when Home Assistant reports it invalid).</summary>
        public static void ClearWebhook()
        {
            WebhookId = null;
            RegisteredSensorsVersion = 0;
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
                    RegisteredSensorsVersion = RegisteredSensorsVersion,
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
