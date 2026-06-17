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
    ///
    /// Two URLs can be configured: a required <see cref="ExternalUrl"/> (reachable from anywhere,
    /// e.g. a Nabu Casa / reverse-proxy address) and an optional <see cref="InternalUrl"/> (the
    /// local-network address). <see cref="ServerUrl"/> is whichever endpoint is currently active —
    /// <see cref="EndpointMonitor"/> prefers the internal URL when it is reachable and falls back to
    /// the external one — so all request code can keep reading <see cref="ServerUrl"/>.
    /// </summary>
    public static class ConfigStore
    {
        private sealed class Stored
        {
            public string ServerUrl { get; set; } = string.Empty;     // external / primary
            public string InternalUrl { get; set; } = string.Empty;   // optional local-network URL
            public string Username { get; set; } = string.Empty;
            public string ProtectedToken { get; set; } = string.Empty; // base64 of DPAPI-encrypted token
        }

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YAHAA");

        private static readonly string FilePath = Path.Combine(Folder, "config.json");

        /// <summary>The required external/primary URL (reachable from anywhere).</summary>
        public static string ExternalUrl { get; private set; } = string.Empty;

        /// <summary>The optional local-network URL; empty if not configured.</summary>
        public static string InternalUrl { get; private set; } = string.Empty;

        /// <summary>The endpoint currently in use (internal when reachable, else external).</summary>
        public static string ServerUrl { get; private set; } = string.Empty;

        public static string Username { get; private set; } = string.Empty;
        public static string Token { get; private set; } = string.Empty;

        /// <summary>True when the active endpoint is the internal URL (for status display).</summary>
        public static bool ActiveEndpointIsInternal { get; private set; }

        /// <summary>Raised when the active endpoint switches between internal and external.</summary>
        public static event Action? ActiveEndpointChanged;

        /// <summary>True once a server URL and token have been saved.</summary>
        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ExternalUrl) && !string.IsNullOrWhiteSpace(Token);

        /// <summary>Loads saved settings into the static properties. Safe to call on startup.</summary>
        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                if (stored is null) return;

                ExternalUrl = stored.ServerUrl;
                InternalUrl = stored.InternalUrl ?? string.Empty;
                Username = stored.Username;
                Token = Unprotect(stored.ProtectedToken);
            }
            catch
            {
                // Corrupt or unreadable config — treat as not configured rather than crash on launch.
                ExternalUrl = InternalUrl = Username = Token = string.Empty;
            }

            // Start on the external URL; EndpointMonitor switches to internal if it's reachable.
            ServerUrl = ExternalUrl;
            ActiveEndpointIsInternal = false;
        }

        /// <summary>Saves settings to disk (encrypting the token) and updates the in-memory values.</summary>
        public static void Save(string externalUrl, string username, string token, string? internalUrl = null)
        {
            Directory.CreateDirectory(Folder);

            var external = HomeAssistantClient.NormalizeUrl(externalUrl);
            var internalNormalized = string.IsNullOrWhiteSpace(internalUrl)
                ? string.Empty
                : HomeAssistantClient.NormalizeUrl(internalUrl);

            var stored = new Stored
            {
                ServerUrl = external,
                InternalUrl = internalNormalized,
                Username = username ?? string.Empty,
                ProtectedToken = Protect(token ?? string.Empty),
            };

            File.WriteAllText(FilePath, JsonSerializer.Serialize(stored));

            ExternalUrl = external;
            InternalUrl = internalNormalized;
            Username = stored.Username;
            Token = token ?? string.Empty;

            // Reset to external; the monitor re-evaluates and may switch back to internal.
            ServerUrl = ExternalUrl;
            ActiveEndpointIsInternal = false;
        }

        /// <summary>
        /// Selects which endpoint request code uses. Called by <see cref="EndpointMonitor"/> after
        /// probing the internal URL. Falls back to the external URL when internal isn't configured.
        /// </summary>
        public static void UseInternalEndpoint(bool useInternal)
        {
            var wantInternal = useInternal && !string.IsNullOrWhiteSpace(InternalUrl);
            var newUrl = wantInternal ? InternalUrl : ExternalUrl;
            if (ServerUrl == newUrl && ActiveEndpointIsInternal == wantInternal) return;

            ServerUrl = newUrl;
            ActiveEndpointIsInternal = wantInternal;
            ActiveEndpointChanged?.Invoke();
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

            ExternalUrl = InternalUrl = ServerUrl = Username = Token = string.Empty;
            ActiveEndpointIsInternal = false;
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
