using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YAHAA.Services
{
    /// <summary>
    /// Persists the Home Assistant connection settings to a JSON file in the per-user
    /// LocalApplicationData folder. The long-lived access token is encrypted at rest with
    /// DPAPI (CurrentUser scope) so it is never written in plaintext.
    /// </summary>
    public static class ConfigStore
    {
        private sealed class Stored
        {
            public string ServerUrl { get; set; } = string.Empty;
            public string Username { get; set; } = string.Empty;
            public string ProtectedToken { get; set; } = string.Empty; // base64 of DPAPI-encrypted token
        }

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YAHAA");

        private static readonly string FilePath = Path.Combine(Folder, "config.json");

        public static string ServerUrl { get; private set; } = string.Empty;
        public static string Username { get; private set; } = string.Empty;
        public static string Token { get; private set; } = string.Empty;

        /// <summary>True once a server URL and token have been saved.</summary>
        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Token);

        /// <summary>Loads saved settings into the static properties. Safe to call on startup.</summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                if (stored is null) return;

                ServerUrl = stored.ServerUrl;
                Username = stored.Username;
                Token = Unprotect(stored.ProtectedToken);
            }
            catch
            {
                // Corrupt or unreadable config — treat as not configured rather than crash on launch.
                ServerUrl = Username = Token = string.Empty;
            }
        }

        /// <summary>Saves settings to disk (encrypting the token) and updates the in-memory values.</summary>
        public static void Save(string serverUrl, string username, string token)
        {
            Directory.CreateDirectory(Folder);

            var stored = new Stored
            {
                ServerUrl = serverUrl ?? string.Empty,
                Username = username ?? string.Empty,
                ProtectedToken = Protect(token ?? string.Empty),
            };

            File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));

            ServerUrl = stored.ServerUrl;
            Username = stored.Username;
            Token = token ?? string.Empty;
        }

        /// <summary>Removes all saved settings (used by "Sign out").</summary>
        public static void Clear()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch
            {
                // Best effort; clearing the in-memory copy below still signs the user out for this session.
            }

            ServerUrl = Username = Token = string.Empty;
        }

        private static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static string Unprotect(string protectedValue)
        {
            if (string.IsNullOrEmpty(protectedValue)) return string.Empty;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedValue), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Token can't be decrypted (e.g. copied to another machine/user) — force re-auth.
                return string.Empty;
            }
        }
    }
}
