using System;
using Microsoft.Win32;

namespace YAHAA.Services
{
    /// <summary>Identity details YAHAA reports to Home Assistant when registering this device.</summary>
    public sealed record DeviceIdentity(
        string DeviceId,
        string DeviceName,
        string Manufacturer,
        string Model,
        string OsName,
        string OsVersion,
        string AppVersion);

    /// <summary>Builds the <see cref="DeviceIdentity"/> for the current machine.</summary>
    public static class DeviceInfo
    {
        private static string? _manufacturer;
        private static string? _model;
        private static string? _appVersion;

        // Rebuilt each access so a changed device name is picked up immediately; the hardware
        // and version lookups are cached.
        public static DeviceIdentity Current => new(
            DeviceId: RegistrationStore.DeviceId,
            DeviceName: AppSettings.EffectiveDeviceName,
            Manufacturer: Manufacturer,
            Model: Model,
            OsName: "Windows",
            OsVersion: Environment.OSVersion.Version.ToString(),
            AppVersion: AppVersion);

        public static string AppVersion => _appVersion ??= ResolveAppVersion();

        private static string Manufacturer => _manufacturer ??= ReadBios("SystemManufacturer");

        private static string Model => _model ??= ReadBios("SystemProductName");

        private static string ReadBios(string valueName)
        {
            try
            {
                using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                return bios?.GetValue(valueName) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ResolveAppVersion()
        {
            // Prefer the MSIX package version when packaged; fall back to the assembly version.
            try
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch
            {
                return typeof(DeviceInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            }
        }
    }
}
