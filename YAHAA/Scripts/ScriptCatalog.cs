using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace YAHAA.Scripts
{
    /// <summary>A runnable script discovered in the scripts folder.</summary>
    public sealed record ScriptItem(string Name, string FullPath, string Kind);

    /// <summary>Enumerates the supported scripts (.ps1 / .bat) in the configured folder.</summary>
    public static class ScriptCatalog
    {
        public static IReadOnlyList<ScriptItem> Enumerate(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return [];

            var items = new List<ScriptItem>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    var kind = Path.GetExtension(file).ToLowerInvariant() switch
                    {
                        ".ps1" => "PowerShell",
                        ".bat" => "Batch",
                        _ => null,
                    };
                    if (kind is null) continue;
                    items.Add(new ScriptItem(Path.GetFileName(file), file, kind));
                }
            }
            catch
            {
                // Folder became inaccessible; treat as empty.
                return [];
            }

            return [.. items.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }
}
