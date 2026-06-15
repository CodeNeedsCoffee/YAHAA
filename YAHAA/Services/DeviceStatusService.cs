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

        /// <summary>Applies a changed idle threshold or re-start after settings change.</summary>
        public static void Restart()
        {
            Stop();
            Start();
        }

        /// <summary>Asks the running loop to push an updated registration (e.g. after a name change).</summary>
        public static void RequestRegistrationRefresh() => _registrationRefreshRequested = true;

        private static async Task RunAsync(CancellationToken ct)
        {
            bool? reported = null;             // the state Home Assistant currently believes
            bool? pending = null;             // a flipped state waiting out the debounce window
            var pendingSince = DateTime.MinValue;
            var lastSendUtc = DateTime.MinValue;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (await EnsureRegisteredAsync(ct).ConfigureAwait(false))
                    {
                        if (_registrationRefreshRequested)
                        {
                            var refresh = await MobileAppClient
                                .UpdateRegistrationAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, DeviceInfo.Current, ct)
                                .ConfigureAwait(false);

                            if (refresh == WebhookResult.WebhookInvalid)
                            {
                                RegistrationStore.ClearWebhook();
                                reported = null;
                                pending = null;
                            }
                            else if (refresh == WebhookResult.Ok)
                            {
                                _registrationRefreshRequested = false;
                            }
                        }

                        var raw = Activity.IsActive(AppSettings.IdleThresholdSeconds);
                        var now = DateTime.UtcNow;
                        var debounce = TimeSpan.FromSeconds(AppSettings.StatusDebounceSeconds);

                        if (reported is null)
                        {
                            // Establish the baseline immediately on (re)start.
                            if (await SendAsync(raw, ct).ConfigureAwait(false))
                            {
                                reported = raw;
                                lastSendUtc = now;
                                pending = null;
                            }
                        }
                        else if (raw != reported)
                        {
                            // State differs from what HA knows — only report once it holds steady
                            // for the debounce window (covers both active→inactive and back).
                            if (pending != raw)
                            {
                                pending = raw;
                                pendingSince = now;
                                SetStatus(raw ? "Confirming active…" : "Confirming inactive…");
                            }
                            else if (now - pendingSince >= debounce)
                            {
                                if (await SendAsync(raw, ct).ConfigureAwait(false))
                                {
                                    reported = raw;
                                    lastSendUtc = now;
                                    pending = null;
                                }
                            }
                        }
                        else
                        {
                            // Raw matches what we reported — cancel any half-finished flip…
                            if (pending is not null)
                            {
                                pending = null;
                                SetStatus(reported.Value ? "Reporting • Active" : "Reporting • Inactive");
                            }

                            // …and keep the entity fresh with a periodic heartbeat.
                            if (now - lastSendUtc >= Heartbeat)
                            {
                                if (await SendAsync(reported.Value, ct).ConfigureAwait(false))
                                    lastSendUtc = now;
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

        private static async Task<bool> SendAsync(bool active, CancellationToken ct)
        {
            var result = await MobileAppClient
                .UpdateActiveSensorAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, active, ct)
                .ConfigureAwait(false);

            switch (result)
            {
                case WebhookResult.Ok:
                    LastReportedActive = active;
                    SetStatus(active ? "Reporting • Active" : "Reporting • Inactive");
                    return true;
                case WebhookResult.WebhookInvalid:
                    RegistrationStore.ClearWebhook();
                    SetStatus("Re-registering…");
                    return false;
                default:
                    SetStatus("Couldn't reach Home Assistant");
                    return false;
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
