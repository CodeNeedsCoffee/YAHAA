using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using YAHAA.Scripts;

namespace YAHAA.Services
{
    /// <summary>
    /// Bridges the local scripts folder to Home Assistant: registers each enabled script as a
    /// <c>button</c> entity on the YAHAA device (via the mobile_app webhook) so they appear in
    /// the device's Controls section, then listens for presses over WebSocket and runs the script.
    /// </summary>
    public static class ScriptBridge
    {
        private const string UniqueIdPrefix = "yahaa_script_";
        private const string ButtonNamePrefix = "YAHAA: ";
        private static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RegistrationPollDelay = TimeSpan.FromSeconds(5);
        private static readonly object Gate = new();

        // Maps HA entity_id → local script full path.
        private static readonly ConcurrentDictionary<string, string> EntityToScript = new();

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

            EntityToScript.Clear();
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
                // Device must be registered (webhook may not exist yet on first run).
                if (!RegistrationStore.IsRegistered)
                {
                    SetStatus("Waiting for device registration…");
                    try { await Task.Delay(RegistrationPollDelay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                try
                {
                    using var client = new HaWebSocketClient();
                    SetStatus("Connecting…");
                    await client.ConnectAndAuthAsync(ConfigStore.ServerUrl, ConfigStore.Token, ct).ConfigureAwait(false);
                    client.OnEvent(OnHaEvent);

                    await SyncAsync(client, ct).ConfigureAwait(false);
                    await client.SendCommandAsync(
                        new() { ["type"] = "subscribe_events", ["event_type"] = "state_changed" }, ct).ConfigureAwait(false);

                    SetStatus($"Connected • {EntityToScript.Count} button(s)");

                    while (!ct.IsCancellationRequested && client.IsOpen)
                    {
                        await Task.Delay(ResyncInterval, ct).ConfigureAwait(false);
                        await SyncAsync(client, ct).ConfigureAwait(false);
                        SetStatus($"Connected • {EntityToScript.Count} button(s)");
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { SetStatus($"Disconnected: {ex.Message}"); }

                try { await Task.Delay(ReconnectDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            SetStatus("Off");
        }

        private static async Task SyncAsync(HaWebSocketClient client, CancellationToken ct)
        {
            var baseUrl = ConfigStore.ServerUrl;
            var webhookId = RegistrationStore.WebhookId!;

            // Build slug → script map for all files currently in the folder.
            var scripts = ScriptCatalog.Enumerate(AppSettings.ScriptsFolder);
            var currentBySlug = new Dictionary<string, ScriptItem>(StringComparer.Ordinal);
            foreach (var s in scripts)
            {
                var slug = Slugify(s.Name);
                if (!string.IsNullOrEmpty(slug) && !currentBySlug.ContainsKey(slug))
                    currentBySlug[slug] = s;
            }

            // Register / update each script as a button on the YAHAA device via the mobile_app
            // webhook. Disabled scripts stay registered but hidden in HA.
            foreach (var kvp in currentBySlug)
            {
                var enabled = AppSettings.IsScriptEnabled(kvp.Value.Name);
                var result = await MobileAppClient.RegisterScriptButtonAsync(
                    baseUrl, webhookId,
                    UniqueIdPrefix + kvp.Key,
                    ButtonNamePrefix + kvp.Value.Name,
                    disabled: !enabled,
                    ct: ct).ConfigureAwait(false);

                if (result == WebhookResult.WebhookInvalid)
                {
                    RegistrationStore.ClearWebhook();
                    return;
                }
            }

            // Query HA's entity registry to get the entity_id for each registered button and
            // to disable any orphaned buttons (scripts deleted since last run).
            EntityToScript.Clear();
            try
            {
                var haPrefix = $"mobile_app_{webhookId}_{UniqueIdPrefix}";
                var entities = await client.SendCommandAsync(
                    new() { ["type"] = "entity_registry/list" }, ct).ConfigureAwait(false);

                foreach (var entry in entities.EnumerateArray())
                {
                    var entityId = entry.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                    var uniqueId = entry.TryGetProperty("unique_id", out var uid) ? uid.GetString() : null;
                    if (entityId is null || uniqueId is null) continue;
                    if (!entityId.StartsWith("button.", StringComparison.Ordinal)) continue;
                    if (!uniqueId.StartsWith(haPrefix, StringComparison.Ordinal)) continue;

                    var slug = uniqueId[haPrefix.Length..];

                    if (currentBySlug.TryGetValue(slug, out var script))
                    {
                        if (AppSettings.IsScriptEnabled(script.Name))
                            EntityToScript[entityId] = script.FullPath;
                    }
                    else
                    {
                        // Script file is gone — disable the orphaned button in HA.
                        var name = (entry.TryGetProperty("original_name", out var on) ? on.GetString() : null)
                                   ?? ButtonNamePrefix + slug;
                        try
                        {
                            await MobileAppClient.RegisterScriptButtonAsync(
                                baseUrl, webhookId, UniqueIdPrefix + slug, name,
                                disabled: true, ct: ct).ConfigureAwait(false);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                // entity_registry/list is best-effort; EntityToScript stays empty until next sync.
            }
        }

        private static void OnHaEvent(JsonElement ev)
        {
            try
            {
                if (!ev.TryGetProperty("event_type", out var et) || et.GetString() != "state_changed") return;
                if (!ev.TryGetProperty("data", out var data)) return;

                var entityId = data.TryGetProperty("entity_id", out var eid) ? eid.GetString() : null;
                if (entityId is null || !entityId.StartsWith("button.", StringComparison.Ordinal)) return;
                if (!EntityToScript.ContainsKey(entityId)) return;

                if (!data.TryGetProperty("new_state", out var ns) || ns.ValueKind != JsonValueKind.Object) return;

                var state = ns.TryGetProperty("state", out var st) ? st.GetString() : null;
                if (string.IsNullOrEmpty(state) || state == "unknown" || state == "unavailable") return;

                if (EntityToScript.TryGetValue(entityId, out var path))
                    ScriptRunner.Run(path);
            }
            catch { }
        }

        // Mirrors HA's slugify: lowercase, non-alphanumeric chars → single underscore, trim ends.
        private static string Slugify(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                    sb.Append('_');
            }
            while (sb.Length > 0 && sb[sb.Length - 1] == '_')
                sb.Length--;
            return sb.ToString();
        }

        private static void SetStatus(string text)
        {
            if (StatusText == text) return;
            StatusText = text;
            StatusChanged?.Invoke();
        }
    }
}
