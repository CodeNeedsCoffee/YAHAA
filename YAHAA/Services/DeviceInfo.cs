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

    /// <summary>Builds (and caches) the <see cref="DeviceIdentity"/> for the current machine.</summary>
    public static class DeviceInfo
    {
        private static DeviceIdentity? _cached;

        public static DeviceIdentity Current => _cached ??= Build();

        private static DeviceIdentity Build()
        {
            string manufacturer = string.Empty;
            string model = string.Empty;
            try
            {
                using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                manufacturer = bios?.GetValue("SystemManufacturer") as string ?? string.Empty;
                model = bios?.GetValue("SystemProductName") as string ?? string.Empty;
            }
            catch
            {
                // Hardware details are best-effort; registration works without them.
            }

            var appVersion = typeof(DeviceInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";

            return new DeviceIdentity(
                DeviceId: RegistrationStore.DeviceId,
                DeviceName: Environment.MachineName,
                Manufacturer: manufacturer,
                Model: model,
                OsName: "Windows",
                OsVersion: Environment.OSVersion.Version.ToString(),
                AppVersion: appVersion);
        }
    }
}
