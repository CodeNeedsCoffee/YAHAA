using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace YAHAA.Services
{
    /// <summary>
    /// Background service that reports this PC's location (latitude/longitude/accuracy) to Home
    /// Assistant as diagnostic sensors on the device. Runs only when the user has enabled location
    /// tracking and Windows has granted location permission. If permission is revoked it turns the
    /// feature off. Location changes slowly for a PC, so it polls on a long interval.
    /// </summary>
    public static class LocationService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan RegistrationPollDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
        private static readonly object Gate = new();

        private static CancellationTokenSource? _cts;
        private static Task? _loop;
        private static bool _sensorsRegistered;

        public static string StatusText { get; private set; } = "Off";
        public static event Action? StatusChanged;

        /// <summary>Raised when the service turns itself off because location permission was lost.</summary>
        public static event Action? TrackingDisabled;

        private static bool ShouldRun => ConfigStore.IsConfigured && AppSettings.LocationTrackingEnabled;

        /// <summary>
        /// Prompts (once) for Windows location permission. Must be called on the UI thread.
        /// Returns the resulting access status.
        /// </summary>
        public static async Task<GeolocationAccessStatus> RequestAccessAsync() =>
            await Geolocator.RequestAccessAsync();

        public static void Start()
        {
            lock (Gate)
            {
                if (!ShouldRun) return;
                if (_loop is { IsCompleted: false }) return;

                _sensorsRegistered = false;
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

            SetStatus("Off");
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && ShouldRun)
                {
                    if (!await EnsureRegisteredAsync(ct).ConfigureAwait(false))
                    {
                        await Task.Delay(RegistrationPollDelay, ct).ConfigureAwait(false);
                        continue;
                    }

                    while (!ct.IsCancellationRequested && ShouldRun && RegistrationStore.IsRegistered)
                    {
                        if (!await ReportOnceAsync(ct).ConfigureAwait(false))
                            return; // permission lost → tracking turned off

                        await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }

            SetStatus("Off");
        }

        // True once the device has a webhook. If reporting is on, DeviceStatusService owns
        // registration and we just wait; if reporting is off we register the device ourselves.
        private static async Task<bool> EnsureRegisteredAsync(CancellationToken ct)
        {
            if (RegistrationStore.IsRegistered) return true;

            if (AppSettings.ReportingEnabled)
            {
                SetStatus("Waiting for device registration…");
                return false;
            }

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
            return true;
        }

        // Reads the current position and pushes it. Returns false only when permission was lost
        // (which turns the feature off); transient failures return true so the loop keeps trying.
        private static async Task<bool> ReportOnceAsync(CancellationToken ct)
        {
            double lat, lon;
            double? accuracy;
            try
            {
                var locator = new Geolocator { DesiredAccuracyInMeters = 100 };
                var position = await locator.GetGeopositionAsync();
                ct.ThrowIfCancellationRequested();

                var coord = position.Coordinate;
                lat = coord.Point.Position.Latitude;
                lon = coord.Point.Position.Longitude;
                accuracy = coord.Accuracy;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                // Windows location permission isn't granted (or was revoked) — disable the feature.
                // No sensors are created in this case, so HA isn't left with bogus 0,0 entities.
                AppSettings.SetLocationTrackingEnabled(false);
                SetStatus("Permission denied — tracking turned off");
                TrackingDisabled?.Invoke();
                return false;
            }
            catch (Exception ex)
            {
                SetStatus($"Couldn't get location: {ex.Message}");
                try { await Task.Delay(RetryDelay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                return true;
            }

            // We have a real position — register the diagnostic sensors once, then push the value.
            if (!_sensorsRegistered)
            {
                var reg = await MobileAppClient
                    .RegisterLocationSensorsAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, ct)
                    .ConfigureAwait(false);

                if (reg == WebhookResult.WebhookInvalid)
                {
                    RegistrationStore.ClearWebhook();
                    return true; // re-register on the next outer-loop pass
                }
                if (reg != WebhookResult.Ok)
                {
                    SetStatus("Couldn't register location sensors");
                    return true;
                }
                _sensorsRegistered = true;
            }

            var result = await MobileAppClient
                .UpdateLocationAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, lat, lon, accuracy, ct)
                .ConfigureAwait(false);

            if (result == WebhookResult.WebhookInvalid)
            {
                RegistrationStore.ClearWebhook();
                _sensorsRegistered = false;
                return true; // re-register on the next outer-loop pass
            }

            // Also drive the device_tracker so Home Assistant shows Home/Away (and the map) from zones.
            await MobileAppClient
                .UpdateDeviceTrackerAsync(ConfigStore.ServerUrl, RegistrationStore.WebhookId!, lat, lon, accuracy, ct)
                .ConfigureAwait(false);

            SetStatus($"Reporting • {lat:F4}, {lon:F4}");
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
