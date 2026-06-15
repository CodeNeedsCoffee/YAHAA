using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    /// <summary>
    /// Background service that keeps Home Assistant updated with this PC's status sensors: Active,
    /// Camera, Microphone, and a combined "Camera or Microphone". Each sensor's change is debounced
    /// by <see cref="AppSettings.StatusDebounceSeconds"/> in both directions. Runs while the app is
    /// alive, including hidden in the system tray.
    /// </summary>
    public static class DeviceStatusService
    {
        // Bump when the set of registered sensors changes so existing installs re-register them.
        private const int SensorsVersion = 2;

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);
        private static readonly object Gate = new();

        private static CancellationTokenSource? _cts;
        private static Task? _loop;
        private static volatile bool _registrationRefreshRequested;

        public static bool IsRunning
        {
            get { lock (Gate) return _loop is { IsCompleted: false }; }
        }

        public static string StatusText { get; private set; } = "Not reporting";
        public static bool? LastReportedActive { get; private set; }

        /// <summary>Raised when <see cref="StatusText"/> or <see cref="LastReportedActive"/> changes.</summary>
        public static event Action? StatusChanged;

        private readonly record struct Snapshot(bool Active, bool Camera, bool Microphone);

        private sealed class SensorRuntime
        {
            public required SensorDefinition Def { get; init; }
            public required Func<Snapshot, bool> Select { get; init; }
            public bool? Reported { get; set; }
            public bool? Pending { get; set; }
            public DateTime PendingSince { get; set; }
        }

        private static List<SensorRuntime> BuildSensors() => new()
        {
            new SensorRuntime { Def = new("active", "Active", "mdi:monitor", "mdi:monitor-off"), Select = s => s.Active },
            new SensorRuntime { Def = new("camera", "Camera", "mdi:webcam", "mdi:webcam-off"), Select = s => s.Camera },
            new SensorRuntime { Def = new("microphone", "Microphone", "mdi:microphone", "mdi:microphone-off"), Select = s => s.Microphone },
            new SensorRuntime { Def = new("camera_or_microphone", "Camera or Microphone", "mdi:video", "mdi:video-off"), Select = s => s.Camera || s.Microphone },
        };

        private static Snapshot Capture() => new(
            Activity.IsActive(AppSettings.IdleThresholdSeconds),
            CapabilityUsage.IsCameraInUse(),
            CapabilityUsage.IsMicrophoneInUse());

        /// <summary>Starts reporting if the app is configured and reporting is enabled. No-op otherwise.</summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (!ConfigStore.IsConfigured || !AppSettings.ReportingEnabled) return;
                if (_loop is { IsCompleted: false }) return;

                _registrationRefreshRequested = true; // refresh device name / app version on start
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

        private static async Task RunAsync(CancellationToken ct)
        {
            var sensors = BuildSensors();
            var lastSendUtc = DateTime.MinValue;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (await EnsureRegisteredAsync(sensors, ct).ConfigureAwait(false))
                    {
                        if (_registrationRefreshRequested)
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

                        var now = DateTime.UtcNow;
                        var debounce = TimeSpan.FromSeconds(AppSettings.StatusDebounceSeconds);
                        var snapshot = Capture();
                        var heartbeatDue = now - lastSendUtc >= Heartbeat;

                        // Decide which sensors to send: a new baseline, a debounced change, or a heartbeat.
                        var sends = new List<(SensorRuntime Sensor, bool State)>();
                        foreach (var s in sensors)
                        {
                            var raw = s.Select(snapshot);
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
                                s.Pending = null; // raw matches reported; cancel any pending flip
                            }
                        }

                        if (heartbeatDue)
                        {
                            foreach (var s in sensors)
                            {
                                if (s.Reported is bool reported && sends.All(x => x.Sensor != s))
                                    sends.Add((s, reported));
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

                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
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
                var snapshot = Capture();
                foreach (var s in sensors)
                {
                    var result = await MobileAppClient
                        .RegisterSensorAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, s.Def, s.Select(snapshot), ct)
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
            var active = sensors.First(s => s.Def.UniqueId == "active").Reported;
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
