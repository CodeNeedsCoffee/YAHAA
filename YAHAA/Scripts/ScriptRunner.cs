using System;
using System.Diagnostics;
using System.IO;

namespace YAHAA.Scripts
{
    /// <summary>Launches a script file (.ps1 via PowerShell, .bat via cmd) without a window.</summary>
    public static class ScriptRunner
    {
        public static bool Run(ScriptItem script) => Run(script.FullPath);

        public static bool Run(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return false;

            var info = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty,
            };

            if (Path.GetExtension(fullPath).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                info.FileName = "powershell.exe";
                info.Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{fullPath}\"";
            }
            else
            {
                info.FileName = "cmd.exe";
                info.Arguments = $"/c \"{fullPath}\"";
            }

            try
            {
                using var process = Process.Start(info);
                return process is not null;
            }
            catch
            {
                return false;
            }
        }
    }
}
