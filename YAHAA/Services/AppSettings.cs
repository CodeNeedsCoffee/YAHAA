using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace YAHAA.Services
{
    /// <summary>Which logo the app displays in its chrome.</summary>
    public enum AppLogo
    {
        /// <summary>The plain Home Assistant-style house.</summary>
        Ha,

        /// <summary>The YAHAA branded (cowboy) logo.</summary>
        Yahaa,
    }

    /// <summary>
    /// User preferences that are independent of the Home Assistant connection (so they survive
    /// "Sign out"). Stored in prefs.json alongside the connection config.
    /// </summary>
    public static class AppSettings
    {
        private sealed class Stored
        {
            public string Logo { get; set; } = nameof(AppLogo.Ha);
            public bool ReportingEnabled { get; set; } = true;
            public int IdleThresholdSeconds { get; set; } = 300;
            public int StatusDebounceSeconds { get; set; } = 5;
            public string DeviceName { get; set; } = string.Empty;
            public bool ScriptsEnabled { get; set; }
            public string ScriptsFolder { get; set; } = string.Empty;
            public bool LocationTrackingEnabled { get; set; }
            public List<string> DisabledSensors { get; set; } = new();
            public List<string> DisabledScripts { get; set; } = new();
        }

        public const int MinDebounceSeconds = 3;
        public const int MaxDebounceSeconds = 30;

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YAHAA");

        private static readonly string FilePath = Path.Combine(Folder, "prefs.json");

        public static AppLogo Logo { get; private set; } = AppLogo.Ha;

        /// <summary>Whether YAHAA registers this device and reports its status to Home Assistant.</summary>
        public static bool ReportingEnabled { get; private set; } = true;

        /// <summary>Seconds of no input after which the PC is reported as inactive.</summary>
        public static int IdleThresholdSeconds { get; private set; } = 300;

        /// <summary>
        /// Seconds a changed active/inactive state must hold steady before it is reported to Home
        /// Assistant (debounce, applied to both directions). Clamped to [3, 30].
        /// </summary>
        public static int StatusDebounceSeconds { get; private set; } = 5;

        /// <summary>
        /// User-chosen name reported to Home Assistant. Empty means "use the PC's name", which is
        /// what <see cref="EffectiveDeviceName"/> resolves to.
        /// </summary>
        public static string DeviceName { get; private set; } = string.Empty;

        /// <summary>The device name actually reported: the custom name, or the machine name.</summary>
        public static string EffectiveDeviceName =>
            string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName;

        /// <summary>Whether the Scripts feature (a folder of .ps1/.bat exposed to HA) is enabled.</summary>
        public static bool ScriptsEnabled { get; private set; }

        /// <summary>Folder scanned for .ps1/.bat scripts when <see cref="ScriptsEnabled"/> is on.</summary>
        public static string ScriptsFolder { get; private set; } = string.Empty;

        /// <summary>
        /// Whether YAHAA reports this PC's location (latitude/longitude) to Home Assistant. Off by
        /// default; turning it on requires the user to grant Windows location permission.
        /// </summary>
        public static bool LocationTrackingEnabled { get; private set; }

        /// <summary>Raised after the selected logo changes, so live UI can refresh itself.</summary>
        public static event Action? LogoChanged;

        /// <summary>Raised when the Scripts toggle or folder changes.</summary>
        public static event Action? ScriptsChanged;

        /// <summary>Unique ids of sensors the user has turned off (not reported / disabled in HA).</summary>
        private static HashSet<string> _disabledSensors = new(StringComparer.Ordinal);

        /// <summary>Raised when a sensor is enabled or disabled.</summary>
        public static event Action? SensorsEnabledChanged;

        public static bool IsSensorEnabled(string sensorId) => !_disabledSensors.Contains(sensorId);

        public static void SetSensorEnabled(string sensorId, bool enabled)
        {
            var changed = enabled ? _disabledSensors.Remove(sensorId) : _disabledSensors.Add(sensorId);
            if (!changed) return;
            Save();
            SensorsEnabledChanged?.Invoke();
        }

        /// <summary>Script file names (e.g. "backup.ps1") the user has turned off (no HA button).</summary>
        private static HashSet<string> _disabledScripts = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsScriptEnabled(string scriptName) => !_disabledScripts.Contains(scriptName);

        public static void SetScriptEnabled(string scriptName, bool enabled)
        {
            var changed = enabled ? _disabledScripts.Remove(scriptName) : _disabledScripts.Add(scriptName);
            if (!changed) return;
            Save();
            ScriptsChanged?.Invoke();
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                if (stored is null) return;
                Logo = Enum.TryParse<AppLogo>(stored.Logo, out var parsed) ? parsed : AppLogo.Ha;
                ReportingEnabled = stored.ReportingEnabled;
                IdleThresholdSeconds = stored.IdleThresholdSeconds > 0 ? stored.IdleThresholdSeconds : 300;
                StatusDebounceSeconds = Math.Clamp(stored.StatusDebounceSeconds, MinDebounceSeconds, MaxDebounceSeconds);
                DeviceName = stored.DeviceName ?? string.Empty;
                ScriptsEnabled = stored.ScriptsEnabled;
                ScriptsFolder = stored.ScriptsFolder ?? string.Empty;
                LocationTrackingEnabled = stored.LocationTrackingEnabled;
                _disabledSensors = new HashSet<string>(stored.DisabledSensors ?? new(), StringComparer.Ordinal);
                _disabledScripts = new HashSet<string>(stored.DisabledScripts ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                Logo = AppLogo.Ha;
                ReportingEnabled = true;
                IdleThresholdSeconds = 300;
                StatusDebounceSeconds = 5;
                DeviceName = string.Empty;
                ScriptsEnabled = false;
                ScriptsFolder = string.Empty;
                LocationTrackingEnabled = false;
                _disabledSensors = new HashSet<string>(StringComparer.Ordinal);
                _disabledScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Turns location reporting on/off. Permission is gated by the caller (UI).</summary>
        public static void SetLocationTrackingEnabled(bool enabled)
        {
            if (LocationTrackingEnabled == enabled) return;
            LocationTrackingEnabled = enabled;
            Save();
        }

        public static void SetScriptsEnabled(bool enabled)
        {
            if (ScriptsEnabled == enabled) return;
            ScriptsEnabled = enabled;
            Save();
            ScriptsChanged?.Invoke();
        }

        public static void SetScriptsFolder(string? folder)
        {
            var value = folder ?? string.Empty;
            if (ScriptsFolder == value) return;
            ScriptsFolder = value;
            Save();
            ScriptsChanged?.Invoke();
        }

        /// <summary>
        /// Sets the reported device name. A value equal to the machine name (or blank) is stored as
        /// empty, so it keeps tracking the PC name automatically.
        /// </summary>
        public static void SetDeviceName(string? name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            if (string.Equals(trimmed, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                trimmed = string.Empty;
            if (DeviceName == trimmed) return;
            DeviceName = trimmed;
            Save();
        }

        public static void SetStatusDebounceSeconds(int seconds)
        {
            seconds = Math.Clamp(seconds, MinDebounceSeconds, MaxDebounceSeconds);
            if (StatusDebounceSeconds == seconds) return;
            StatusDebounceSeconds = seconds;
            Save();
        }

        public static void SetReportingEnabled(bool enabled)
        {
            if (ReportingEnabled == enabled) return;
            ReportingEnabled = enabled;
            Save();
        }

        public static void SetIdleThresholdSeconds(int seconds)
        {
            seconds = Math.Max(30, seconds);
            if (IdleThresholdSeconds == seconds) return;
            IdleThresholdSeconds = seconds;
            Save();
        }

        public static void SetLogo(AppLogo logo)
        {
            if (Logo == logo) return;
            Logo = logo;
            Save();
            LogoChanged?.Invoke();
        }

        /// <summary>ms-appx URI of the image for a given logo choice.</summary>
        public static string LogoImageUri(AppLogo logo) =>
            logo == AppLogo.Yahaa
                ? "ms-appx:///Assets/Logo-YAHAA.png"
                : "ms-appx:///Assets/Logo-HA.png";

        public static string CurrentLogoImageUri => LogoImageUri(Logo);

        /// <summary>
        /// Absolute path to the .ico used for the window title bar / taskbar / tray for a given
        /// logo choice. The file is copied next to the app (see csproj CopyToOutputDirectory).
        /// </summary>
        public static string LogoIconPath(AppLogo logo) =>
            Path.Combine(AppContext.BaseDirectory, "Assets",
                logo == AppLogo.Yahaa ? "AppIcon-YAHAA.ico" : "AppIcon.ico");

        public static string CurrentLogoIconPath => LogoIconPath(Logo);

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored
                {
                    Logo = Logo.ToString(),
                    ReportingEnabled = ReportingEnabled,
                    IdleThresholdSeconds = IdleThresholdSeconds,
                    StatusDebounceSeconds = StatusDebounceSeconds,
                    DeviceName = DeviceName,
                    ScriptsEnabled = ScriptsEnabled,
                    ScriptsFolder = ScriptsFolder,
                    LocationTrackingEnabled = LocationTrackingEnabled,
                    DisabledSensors = _disabledSensors.ToList(),
                    DisabledScripts = _disabledScripts.ToList(),
                }));
            }
            catch
            {
                // Preference persistence is best-effort; a failure here shouldn't crash the app.
            }
        }
    }
}
