using System;
using System.IO;
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
        }

        private static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YAHAA");

        private static readonly string FilePath = Path.Combine(Folder, "prefs.json");

        public static AppLogo Logo { get; private set; } = AppLogo.Ha;

        /// <summary>Raised after the selected logo changes, so live UI can refresh itself.</summary>
        public static event Action? LogoChanged;

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                if (stored is null) return;
                Logo = Enum.TryParse<AppLogo>(stored.Logo, out var parsed) ? parsed : AppLogo.Ha;
            }
            catch
            {
                Logo = AppLogo.Ha;
            }
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
                File.WriteAllText(FilePath, JsonSerializer.Serialize(new Stored { Logo = Logo.ToString() }));
            }
            catch
            {
                // Preference persistence is best-effort; a failure here shouldn't crash the app.
            }
        }
    }
}
