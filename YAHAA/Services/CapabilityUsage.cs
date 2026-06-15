using System;
using Microsoft.Win32;

namespace YAHAA.Services
{
    /// <summary>
    /// Detects whether the camera or microphone is currently in use, by reading the Windows
    /// Capability Access Manager consent store. Each app that has used a capability records a
    /// LastUsedTimeStart and LastUsedTimeStop; an entry with a start time but no stop time
    /// (Stop == 0) is currently using the device.
    /// </summary>
    public static class CapabilityUsage
    {
        private const string ConsentStore =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

        public static bool IsCameraInUse() => IsCapabilityInUse("webcam");

        public static bool IsMicrophoneInUse() => IsCapabilityInUse("microphone");

        private static bool IsCapabilityInUse(string capability)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{capability}");
                return key is not null && AnyInUse(key);
            }
            catch
            {
                return false;
            }
        }

        private static bool AnyInUse(RegistryKey key)
        {
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub is null) continue;

                // Desktop (unpackaged) apps live one level deeper under "NonPackaged".
                if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    if (AnyInUse(sub)) return true;
                }
                else if (IsLeafInUse(sub))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLeafInUse(RegistryKey appKey)
        {
            try
            {
                var start = Convert.ToInt64(appKey.GetValue("LastUsedTimeStart") ?? 0L);
                var stop = Convert.ToInt64(appKey.GetValue("LastUsedTimeStop") ?? 0L);
                return start != 0 && stop == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
