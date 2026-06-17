using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    /// <summary>
    /// Background service that keeps Home Assistant updated with this PC's status sensors (see
    /// <see cref="SensorCatalog"/>). Each sensor's change is debounced by
    /// <see cref="AppSettings.StatusDebounceSeconds"/> in both directions. Sensors the user has
    /// turned off are left registered (and enabled) in HA but stop being reported and are blanked
    /// to "unknown": HA's mobile_app integration cannot reliably re-enable a webhook sensor once it
    /// has been disabled, so we never disable the entity itself. Runs while the app is alive,
    /// including hidden in the system tray.
    /// </summary>
    public static class DeviceStatusService
    {
        // Bump when the set of registered sensors changes so existing installs re-register them.
        // v3: sensors are always registered enabled (toggling off only blanks/stops reporting).
        private const int SensorsVersion = 3;

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);
        private static readonly object Gate = new();

        private static CancellationTokenSource? _cts;
        private static Task? _loop;
        private static volatile bool _registrationRefreshRequested;
        private static volatile bool _sensorSyncRequested;

        public static bool IsRunning
        {
            get { lock (Gate) return _loop is { IsCompleted: false }; }
        }

        public static string StatusText { get; private set; } = "Not reporting";
        public static bool? LastReportedActive { get; private set; }

        /// <summary>Raised when <see cref="StatusText"/> or <see cref="LastReportedActive"/> changes.</summary>
        public static event Action? StatusChanged;

        private sealed class SensorRuntime
        {
            public SensorInfo Info { get; }
            public SensorDefinition Def { get; }
            public bool? Reported { get; set; }
            public bool? Pending { get; set; }
            public DateTime PendingSince { get; set; }

            public SensorRuntime(SensorInfo info)
            {
                Info = info;
                Def = new SensorDefinition(info.Id, info.DisplayName, info.OnIcon, info.OffIcon);
            }
        }

        public static void Start()
        {
            lock (Gate)
            {
                if (!ConfigStore.IsConfigured || !AppSettings.ReportingEnabled) return;
                if (_loop is { IsCompleted: false }) return;

                _registrationRefreshRequested = true; // refresh device name / app version on start
                _sensorSyncRequested = true;          // push current enabled/disabled state on start
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

            LastReportedActive = null;
            SetStatus("Not reporting");
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        /// <summary>Asks the running loop to push an updated registration (e.g. after a name change).</summary>
        public static void RequestRegistrationRefresh() => _registrationRefreshRequested = true;

        /// <summary>Asks the running loop to push the current sensor enabled/disabled state to HA.</summary>
        public static void RequestSensorSync() => _sensorSyncRequested = true;

        /// <summary>
        /// Stops reporting and pushes all sensors to "unknown" so Home Assistant doesn't keep their
        /// last value (e.g. Active = on) after the app exits. Best-effort, with a short timeout.
        /// </summary>
        public static async Task ReportOfflineAndStopAsync()
        {
            Stop();

            if (!ConfigStore.IsConfigured || string.IsNullOrEmpty(RegistrationStore.WebhookId))
                return;

            var uniqueIds = SensorCatalog.All.Select(s => s.Id).ToList();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await MobileAppClient
                    .SetSensorsUnknownAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, uniqueIds, cts.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best effort during shutdown.
            }
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            var sensors = SensorCatalog.All.Select(i => new SensorRuntime(i)).ToList();
            var lastSendUtc = DateTime.MinValue;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                    if (await EnsureRegisteredAsync(sensors, ct).ConfigureAwait(false))
                    {
                        if (_registrationRefreshRequested)
                            await RefreshRegistrationAsync(sensors, ct).ConfigureAwait(false);

                        if (_sensorSyncRequested)
                            await SyncSensorEnablementAsync(sensors, ct).ConfigureAwait(false);

                        var now = DateTime.UtcNow;
                        var debounce = TimeSpan.FromSeconds(AppSettings.StatusDebounceSeconds);
                        var heartbeatDue = now - lastSendUtc >= Heartbeat;

                        var sends = new List<(SensorRuntime Sensor, bool State)>();
                        foreach (var s in sensors)
                        {
                            if (!AppSettings.IsSensorEnabled(s.Info.Id))
                            {
                                s.Reported = null;
                                s.Pending = null;
                                continue;
                            }

                            var raw = s.Info.Read();
                            if (s.Reported is null)
                            {
                                sends.Add((s, raw));
                            }
                            else if (raw != s.Reported)
                            {
                                if (s.Pending != raw)
                                {
                                    s.Pending = raw;
                                    s.PendingSince = now;
                                }
                                else if (now - s.PendingSince >= debounce)
                                {
                                    sends.Add((s, raw));
                                }
                            }
                            else
                            {
                                s.Pending = null;
                            }
                        }

                        if (heartbeatDue)
                        {
                            foreach (var s in sensors)
                            {
                                if (AppSettings.IsSensorEnabled(s.Info.Id)
                                    && s.Reported is bool reported
                                    && sends.All(x => x.Sensor != s))
                                {
                                    sends.Add((s, reported));
                                }
                            }
                        }

                        if (sends.Count > 0)
                        {
                            var readings = sends
                                .Select(x => new SensorReading(x.Sensor.Def.UniqueId, x.State, x.Sensor.Def.IconFor(x.State)))
                                .ToList();

                            var result = await MobileAppClient
                                .UpdateSensorStatesAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, readings, ct)
                                .ConfigureAwait(false);

                            switch (result)
                            {
                                case WebhookResult.Ok:
                                    foreach (var (sensor, state) in sends)
                                    {
                                        sensor.Reported = state;
                                        sensor.Pending = null;
                                    }
                                    lastSendUtc = now;
                                    UpdateStatus(sensors);
                                    break;
                                case WebhookResult.WebhookInvalid:
                                    RegistrationStore.ClearWebhook();
                                    ResetReported(sensors);
                                    SetStatus("Re-registering…");
                                    break;
                                default:
                                    SetStatus("Couldn't reach Home Assistant");
                                    break;
                            }
                        }
                    }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // shutdown — bubble to the outer handler
                    }
                    catch (Exception ex)
                    {
                        // A transient fault must not permanently kill reporting; log and keep looping.
                        SetStatus($"Reporting error: {ex.Message}");
                    }

                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        private static async Task RefreshRegistrationAsync(List<SensorRuntime> sensors, CancellationToken ct)
        {
            var refresh = await MobileAppClient
                .UpdateRegistrationAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, DeviceInfo.Current, ct)
                .ConfigureAwait(false);

            if (refresh == WebhookResult.WebhookInvalid)
            {
                RegistrationStore.ClearWebhook();
                ResetReported(sensors);
            }
            else if (refresh == WebhookResult.Ok)
            {
                _registrationRefreshRequested = false;
            }
        }

        // Reacts to a sensor being toggled on/off. We never disable the HA entity (the mobile_app
        // integration can't reliably re-enable it afterwards); instead, sensors turned off are
        // blanked to "unknown" and simply stop being reported, while the rest are reset so their
        // current value is re-sent.
        private static async Task SyncSensorEnablementAsync(List<SensorRuntime> sensors, CancellationToken ct)
        {
            var disabledIds = new List<string>();
            foreach (var s in sensors)
            {
                // Clearing Reported forces enabled sensors to re-send their value on the next poll.
                s.Reported = null;
                s.Pending = null;
                if (!AppSettings.IsSensorEnabled(s.Info.Id))
                    disabledIds.Add(s.Info.Id);
            }

            if (disabledIds.Count > 0)
            {
                var result = await MobileAppClient
                    .SetSensorsUnknownAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, disabledIds, ct)
                    .ConfigureAwait(false);

                if (result == WebhookResult.WebhookInvalid)
                {
                    RegistrationStore.ClearWebhook();
                    ResetReported(sensors);
                    return; // leave _sensorSyncRequested set so we retry after re-registering
                }
            }

            _sensorSyncRequested = false;
        }

        private static async Task<bool> EnsureRegisteredAsync(IReadOnlyList<SensorRuntime> sensors, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(RegistrationStore.WebhookId))
            {
                SetStatus("Registering device…");
                var registration = await MobileAppClient
                    .RegisterDeviceAsync(ConfigStore.ServerUrl, ConfigStore.Token, DeviceInfo.Current, ct)
                    .ConfigureAwait(false);

                if (!registration.Success || string.IsNullOrEmpty(registration.WebhookId))
                {
                    SetStatus(registration.ErrorMessage ?? "Registration failed");
                    return false;
                }

                RegistrationStore.SetWebhook(registration.WebhookId);
            }

            if (RegistrationStore.RegisteredSensorsVersion != SensorsVersion)
            {
                foreach (var s in sensors)
                {
                    // Always register enabled; sensors the user turned off are blanked to "unknown"
                    // by SyncSensorEnablementAsync (requested on start) rather than disabled in HA.
                    var result = await MobileAppClient
                        .RegisterSensorAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, s.Def, s.Info.Read(), disabled: false, ct: ct)
                        .ConfigureAwait(false);

                    if (result == WebhookResult.WebhookInvalid)
                    {
                        RegistrationStore.ClearWebhook();
                        return false;
                    }

                    if (result != WebhookResult.Ok)
                    {
                        SetStatus("Couldn't register sensors");
                        return false;
                    }
                }

                RegistrationStore.SetSensorsVersion(SensorsVersion);
            }

            return true;
        }

        private static void ResetReported(IEnumerable<SensorRuntime> sensors)
        {
            foreach (var s in sensors)
            {
                s.Reported = null;
                s.Pending = null;
            }
        }

        private static void UpdateStatus(IEnumerable<SensorRuntime> sensors)
        {
            var active = sensors.FirstOrDefault(s => s.Info.Id == "active")?.Reported;
            LastReportedActive = active;
            SetStatus(active == true ? "Reporting • Active" : "Reporting • Inactive");
        }

        private static void SetStatus(string text)
        {
            if (StatusText == text) return;
            StatusText = text;
            StatusChanged?.Invoke();
        }
    }
}
