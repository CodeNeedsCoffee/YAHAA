using System;
using System.Threading;
using System.Threading.Tasks;

namespace YAHAA.Services
{
    /// <summary>
    /// Background service that keeps Home Assistant updated with this PC's "Active" state. It
    /// ensures the device + sensor are registered, then reports the active state on change (with a
    /// periodic heartbeat). Runs while the app is alive — including hidden in the system tray.
    /// </summary>
    public static class DeviceStatusService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5);
        private static readonly object Gate = new();

        private static CancellationTokenSource? _cts;
        private static Task? _loop;

        public static bool IsRunning
        {
            get { lock (Gate) return _loop is { IsCompleted: false }; }
        }

        public static string StatusText { get; private set; } = "Not reporting";
        public static bool? LastReportedActive { get; private set; }

        /// <summary>Raised when <see cref="StatusText"/> or <see cref="LastReportedActive"/> changes.</summary>
        public static event Action? StatusChanged;

        /// <summary>Starts reporting if the app is configured and reporting is enabled. No-op otherwise.</summary>
        public static void Start()
        {
            lock (Gate)
            {
                if (!ConfigStore.IsConfigured || !AppSettings.ReportingEnabled) return;
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

            LastReportedActive = null;
            SetStatus("Not reporting");
        }

        /// <summary>Applies a changed idle threshold or re-start after settings change.</summary>
        public static void Restart()
        {
            Stop();
            Start();
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            bool? lastSent = null;
            var lastSendUtc = DateTime.MinValue;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (await EnsureRegisteredAsync(ct).ConfigureAwait(false))
                    {
                        var active = Activity.IsActive(AppSettings.IdleThresholdSeconds);
                        var now = DateTime.UtcNow;

                        if (lastSent != active || now - lastSendUtc >= Heartbeat)
                        {
                            var result = await MobileAppClient
                                .UpdateActiveSensorAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, active, ct)
                                .ConfigureAwait(false);

                            switch (result)
                            {
                                case WebhookResult.Ok:
                                    lastSent = active;
                                    lastSendUtc = now;
                                    LastReportedActive = active;
                                    SetStatus(active ? "Reporting • Active" : "Reporting • Inactive");
                                    break;
                                case WebhookResult.WebhookInvalid:
                                    RegistrationStore.ClearWebhook();
                                    lastSent = null;
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

        private static async Task<bool> EnsureRegisteredAsync(CancellationToken ct)
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

            if (!RegistrationStore.SensorsRegistered)
            {
                var active = Activity.IsActive(AppSettings.IdleThresholdSeconds);
                var result = await MobileAppClient
                    .RegisterActiveSensorAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, active, ct)
                    .ConfigureAwait(false);

                if (result == WebhookResult.WebhookInvalid)
                {
                    RegistrationStore.ClearWebhook();
                    return false;
                }

                if (result != WebhookResult.Ok)
                {
                    SetStatus("Couldn't register the Active sensor");
                    return false;
                }

                RegistrationStore.SetSensorsRegistered(true);
            }

            return true;
        }

        private static void SetStatus(string text)
        {
            if (StatusText == text) return;
            StatusText = text;
            StatusChanged?.Invoke();
        }
    }
}
