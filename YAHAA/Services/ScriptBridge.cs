using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YAHAA.Scripts;

namespace YAHAA.Services
{
    /// <summary>
    /// Bridges the local scripts folder to Home Assistant. Over a WebSocket connection it creates an
    /// <c>input_button</c> helper per enabled script (named "YAHAA: &lt;filename&gt;"), listens for
    /// presses, and runs the matching script. Helpers for scripts that are removed or disabled are
    /// deleted. Requires the configured token to belong to an admin user (helper create/delete).
    ///
    /// Note: Home Assistant's mobile_app integration can only attach sensors/binary_sensors to the
    /// YAHAA device, not buttons, so these helpers live under Settings → Devices &amp; services →
    /// Helpers rather than on the device page. They can still be pressed from the UI, dashboards,
    /// and automations to trigger the script remotely.
    /// </summary>
    public static class ScriptBridge
    {
        private const string HelperPrefix = "YAHAA: ";
        private static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
        private static readonly object Gate = new();

        // Maps a helper's friendly name -> the script's full path.
        private static readonly ConcurrentDictionary<string, string> NameToScript = new();

        private static CancellationTokenSource? _cts;
        private static Task? _loop;

        public static string StatusText { get; private set; } = "Off";
        public static event Action? StatusChanged;

        private static bool ShouldRun => ConfigStore.IsConfigured && AppSettings.ScriptsEnabled;

        public static void Start()
        {
            lock (Gate)
            {
                if (!ShouldRun) return;
                if (_loop is { IsCompleted: false }) return;

                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => RunAsync(_cts.Token));
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                _cts?.Cancel();
                _cts = null;
            }

            NameToScript.Clear();
            SetStatus("Off");
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var client = new HaWebSocketClient();
                    SetStatus("Connecting…");
                    await client.ConnectAndAuthAsync(ConfigStore.ServerUrl, ConfigStore.Token, ct).ConfigureAwait(false);
                    client.OnEvent(OnHaEvent);

                    await SyncHelpersAsync(client, ct).ConfigureAwait(false);
                    await client.SendCommandAsync(
                        new() { ["type"] = "subscribe_events", ["event_type"] = "state_changed" }, ct).ConfigureAwait(false);
                    SetStatus($"Connected • {NameToScript.Count} button(s)");

                    while (!ct.IsCancellationRequested && client.IsOpen)
                    {
                        await Task.Delay(ResyncInterval, ct).ConfigureAwait(false);
                        await SyncHelpersAsync(client, ct).ConfigureAwait(false);
                        SetStatus($"Connected • {NameToScript.Count} button(s)");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus($"Disconnected: {ex.Message}");
                }

                try
                {
                    await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            SetStatus("Off");
        }

        private static async Task SyncHelpersAsync(HaWebSocketClient client, CancellationToken ct)
        {
            // Only scripts the user has left enabled get an HA button.
            var scripts = ScriptCatalog.Enumerate(AppSettings.ScriptsFolder)
                .Where(s => AppSettings.IsScriptEnabled(s.Name))
                .ToList();

            // Existing input_button helpers, keyed by friendly name -> storage id.
            var existing = new Dictionary<string, string>(StringComparer.Ordinal);
            var list = await client.SendCommandAsync(new() { ["type"] = "input_button/list" }, ct).ConfigureAwait(false);
            foreach (var item in list.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                if (name is not null && id is not null) existing[name] = id;
            }

            var desired = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in scripts) desired.Add(HelperPrefix + s.Name);

            // Create buttons for new scripts.
            foreach (var s in scripts)
            {
                var helperName = HelperPrefix + s.Name;
                if (!existing.ContainsKey(helperName))
                {
                    await client.SendCommandAsync(new()
                    {
                        ["type"] = "input_button/create",
                        ["name"] = helperName,
                        ["icon"] = "mdi:script-text-play",
                    }, ct).ConfigureAwait(false);
                }
            }

            // Remove our buttons whose script is gone or has been disabled.
            foreach (var kv in existing)
            {
                if (kv.Key.StartsWith(HelperPrefix, StringComparison.Ordinal) && !desired.Contains(kv.Key))
                {
                    try
                    {
                        await client.SendCommandAsync(new()
                        {
                            ["type"] = "input_button/delete",
                            ["input_button_id"] = kv.Value,
                        }, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }

            NameToScript.Clear();
            foreach (var s in scripts)
                NameToScript[HelperPrefix + s.Name] = s.FullPath;
        }

        private static void OnHaEvent(JsonElement ev)
        {
            try
            {
                if (!ev.TryGetProperty("event_type", out var et) || et.GetString() != "state_changed") return;
                if (!ev.TryGetProperty("data", out var data)) return;

                var entityId = data.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                if (entityId is null || !entityId.StartsWith("input_button.", StringComparison.Ordinal)) return;

                if (!data.TryGetProperty("new_state", out var ns) || ns.ValueKind != JsonValueKind.Object) return;

                // input_button presses set the state to a timestamp; "unknown"/"unavailable" aren't presses.
                var state = ns.TryGetProperty("state", out var st) ? st.GetString() : null;
                if (string.IsNullOrEmpty(state) || state == "unknown" || state == "unavailable") return;

                var friendlyName = ns.TryGetProperty("attributes", out var attrs)
                    && attrs.TryGetProperty("friendly_name", out var fn)
                    ? fn.GetString()
                    : null;

                if (friendlyName is not null && NameToScript.TryGetValue(friendlyName, out var path))
                    ScriptRunner.Run(path);
            }
            catch
            {
                // Ignore malformed events.
            }
        }

        private static void SetStatus(string text)
        {
            if (StatusText == text) return;
            StatusText = text;
            StatusChanged?.Invoke();
        }
    }
}
