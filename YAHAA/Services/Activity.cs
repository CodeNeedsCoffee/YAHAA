using System;
using System.Runtime.InteropServices;

namespace YAHAA.Services
{
    /// <summary>
    /// Reports whether the user is actively using this PC, based on the time since the last
    /// keyboard/mouse input (the same signal the HA companion apps use for their "Active" sensor).
    /// </summary>
    public static partial class Activity
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

        /// <summary>Seconds since the last user input across the session.</summary>
        public static int IdleSeconds
        {
            get
            {
                var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
                if (!GetLastInputInfo(ref info)) return 0;

                // Unsigned subtraction is wrap-safe across the 32-bit tick counter.
                uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
                return (int)(idleMs / 1000);
            }
        }

        /// <summary>True when idle time is below the given threshold (i.e. the PC is in active use).</summary>
        public static bool IsActive(int thresholdSeconds) => IdleSeconds < Math.Max(1, thresholdSeconds);
    }
}
