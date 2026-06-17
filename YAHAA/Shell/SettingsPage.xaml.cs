using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>
    /// Lets the user view and edit the saved connection, test or save changes, re-run the
    /// setup wizard, or sign out.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private readonly DispatcherTimer _refreshTimer;
        private bool _initializing;

        public SettingsPage()
        {
            InitializeComponent();

            UrlBox.Text = ConfigStore.ExternalUrl;
            InternalUrlBox.Text = ConfigStore.InternalUrl;
            UsernameBox.Text = ConfigStore.Username;
            TokenBox.Password = ConfigStore.Token;
            UpdateEndpointStatus();

            LogoChoice.SelectedIndex = (int)AppSettings.Logo;

            _initializing = true;
            DeviceNameBox.Text = AppSettings.EffectiveDeviceName;
            AppVersionText.Text = DeviceInfo.AppVersion;
            ReportToggle.IsOn = AppSettings.ReportingEnabled;
            IdleBox.Value = AppSettings.IdleThresholdSeconds / 60.0;
            DebounceSlider.Value = AppSettings.StatusDebounceSeconds;
            UpdateDebounceLabel();
            ScriptsToggle.IsOn = AppSettings.ScriptsEnabled;
            LocationToggle.IsOn = AppSettings.LocationTrackingEnabled;
            _initializing = false;

            UpdateScriptsUi();
            UpdateDeviceStatus();
            UpdateLocationStatus();
            DeviceStatusService.StatusChanged += OnReportingStatusChanged;
            LocationService.StatusChanged += OnLocationStatusChanged;
            LocationService.TrackingDisabled += OnLocationTrackingDisabled;
            ConfigStore.ActiveEndpointChanged += OnEndpointChanged;

            // Initialize the startup task state from the OS (packaged app) if available.
            InitializeStartupStateAsync();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _refreshTimer.Tick += (_, _) => UpdateDeviceStatus();
            _refreshTimer.Start();

            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();
            DeviceStatusService.StatusChanged -= OnReportingStatusChanged;
            LocationService.StatusChanged -= OnLocationStatusChanged;
            LocationService.TrackingDisabled -= OnLocationTrackingDisabled;
            ConfigStore.ActiveEndpointChanged -= OnEndpointChanged;
        }

        private void OnEndpointChanged() => DispatcherQueue.TryEnqueue(UpdateEndpointStatus);

        private void UpdateEndpointStatus()
        {
            if (string.IsNullOrWhiteSpace(ConfigStore.InternalUrl))
            {
                EndpointStatusText.Text = "Using the external URL.";
                return;
            }

            EndpointStatusText.Text = ConfigStore.ActiveEndpointIsInternal
                ? "Currently connected via the internal URL (on your local network)."
                : "Currently connected via the external URL (internal not reachable).";
        }

        private void OnReportingStatusChanged() => DispatcherQueue.TryEnqueue(UpdateDeviceStatus);

        private void OnLocationStatusChanged() => DispatcherQueue.TryEnqueue(UpdateLocationStatus);

        // The service turned tracking off (permission revoked) — reflect that in the toggle.
        private void OnLocationTrackingDisabled() => DispatcherQueue.TryEnqueue(() =>
        {
            _initializing = true;
            LocationToggle.IsOn = false;
            _initializing = false;
            ShowLocationInfo(InfoBarSeverity.Warning,
                "Location permission was revoked, so tracking was turned off. Re-enable it in Windows Settings → Privacy & security → Location.");
            UpdateLocationStatus();
        });

        private void UpdateLocationStatus() =>
            LocationStatusText.Text = AppSettings.LocationTrackingEnabled ? LocationService.StatusText : "Off";

        private void UpdateDeviceStatus()
        {
            ReportStatusText.Text = AppSettings.ReportingEnabled ? DeviceStatusService.StatusText : "Off";

            var active = Activity.IsActive(AppSettings.IdleThresholdSeconds);
            ActiveNowText.Text = $"Currently: {(active ? "Active" : "Inactive")} — {Activity.IdleSeconds}s since last input";
        }

        private void ReportToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            AppSettings.SetReportingEnabled(ReportToggle.IsOn);
            if (ReportToggle.IsOn) DeviceStatusService.Start();
            else DeviceStatusService.Stop();

            UpdateDeviceStatus();
        }

        private void IdleBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_initializing || double.IsNaN(args.NewValue)) return;

            AppSettings.SetIdleThresholdSeconds((int)Math.Round(args.NewValue * 60));
            UpdateDeviceStatus();
        }

        private void DeviceNameBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            AppSettings.SetDeviceName(DeviceNameBox.Text);
            DeviceNameBox.Text = AppSettings.EffectiveDeviceName;
            DeviceStatusService.RequestRegistrationRefresh();
        }

        private void DebounceSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_initializing) return;

            AppSettings.SetStatusDebounceSeconds((int)Math.Round(e.NewValue));
            UpdateDebounceLabel();
        }

        private void UpdateDebounceLabel() =>
            DebounceLabel.Text = $"Reporting delay: {AppSettings.StatusDebounceSeconds} s";

        private void LogoChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogoChoice.SelectedIndex < 0) return;
            AppSettings.SetLogo((AppLogo)LogoChoice.SelectedIndex);
        }

        private async void Test_Click(object sender, RoutedEventArgs e)
        {
            var result = await RunValidationAsync();
            if (result is null) return;

            if (result.Success)
            {
                var name = string.IsNullOrWhiteSpace(result.LocationName) ? "your home" : result.LocationName;
                ShowResult(InfoBarSeverity.Success,
                    string.IsNullOrWhiteSpace(result.Version)
                        ? $"Connected to {name}."
                        : $"Connected to {name} • Home Assistant {result.Version}");
            }
            else
            {
                ShowResult(InfoBarSeverity.Error, result.ErrorMessage ?? "Couldn't connect.");
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            var result = await RunValidationAsync();
            if (result is null) return;

            if (!result.Success)
            {
                ShowResult(InfoBarSeverity.Error,
                    (result.ErrorMessage ?? "Couldn't connect.") + " Changes were not saved.");
                return;
            }

            ConfigStore.Save(
                HomeAssistantClient.NormalizeUrl(UrlBox.Text),
                UsernameBox.Text?.Trim() ?? string.Empty,
                TokenBox.Password?.Trim() ?? string.Empty,
                InternalUrlBox.Text?.Trim() ?? string.Empty);

            // Re-probe which endpoint to use, and reconnect the scripts bridge to the saved server.
            EndpointMonitor.Refresh();
            ScriptBridge.Restart();

            UpdateEndpointStatus();
            ShowResult(InfoBarSeverity.Success, "Connection saved.");
        }

        private async void ScriptsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            AppSettings.SetScriptsEnabled(ScriptsToggle.IsOn);
            if (ScriptsToggle.IsOn && string.IsNullOrWhiteSpace(AppSettings.ScriptsFolder))
            {
                var folder = await PickFolderAsync();
                if (folder is not null) AppSettings.SetScriptsFolder(folder);
            }

            UpdateScriptsUi();
        }

        private async void LocationToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            if (!LocationToggle.IsOn)
            {
                AppSettings.SetLocationTrackingEnabled(false);
                LocationService.Stop();
                LocationInfoBar.IsOpen = false;
                UpdateLocationStatus();
                return;
            }

            // Turning on: ask Windows for location permission first.
            GeolocationAccessStatus status;
            try
            {
                status = await LocationService.RequestAccessAsync();
            }
            catch
            {
                status = GeolocationAccessStatus.Unspecified;
            }

            if (status == GeolocationAccessStatus.Allowed)
            {
                AppSettings.SetLocationTrackingEnabled(true);
                LocationService.Start();
                LocationInfoBar.IsOpen = false;
            }
            else
            {
                // Permission denied — revert the toggle and tell the user how to grant it.
                _initializing = true;
                LocationToggle.IsOn = false;
                _initializing = false;
                AppSettings.SetLocationTrackingEnabled(false);
                ShowLocationInfo(InfoBarSeverity.Error,
                    "Location permission wasn't granted, so tracking stays off. Allow location for YAHAA in Windows Settings → Privacy & security → Location, then try again.");
            }

            UpdateLocationStatus();
        }

        private void ShowLocationInfo(InfoBarSeverity severity, string message)
        {
            LocationInfoBar.Severity = severity;
            LocationInfoBar.Message = message;
            LocationInfoBar.IsOpen = true;
        }

        private async void InitializeStartupStateAsync()
        {
            try
            {
                var task = await Windows.ApplicationModel.StartupTask.GetAsync("YAHAAStartup");
                if (task is not null)
                {
                    // Reflect the OS state, not just the saved preference.
                    var enabled = task.State == Windows.ApplicationModel.StartupTaskState.Enabled;
                    _initializing = true;
                    LaunchToggle.IsOn = enabled;
                    LaunchStatusText.Text = task.State.ToString();
                    _initializing = false;

                    // Keep stored preference in sync with OS when possible.
                    AppSettings.SetLaunchOnStartup(enabled);
                    return;
                }
            }
            catch
            {
                // API unavailable (unpackaged or older runtime) — fall back to stored preference below.
            }

            // Fallback: reflect saved preference.
            _initializing = true;
            LaunchToggle.IsOn = AppSettings.LaunchOnStartup;
            LaunchStatusText.Text = AppSettings.LaunchOnStartup ? "Enabled (preference)" : "Disabled (preference)";
            _initializing = false;
        }

        private async void LaunchToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            var enabled = LaunchToggle.IsOn;

            // Try to register/unregister the StartupTask if running as a packaged app.
            try
            {
                // Windows.ApplicationModel.StartupTask exists in MSIX packaged apps.
                var startupTask = await Windows.ApplicationModel.StartupTask.GetAsync("YAHAAStartup");
                if (startupTask != null)
                {
                    if (enabled)
                    {
                        var state = await startupTask.RequestEnableAsync();
                        // RequestEnableAsync returns Enabled, DisabledByPolicy, DisabledByUser, Disabled
                        // Treat enabled only when Enabled.
                        enabled = state == Windows.ApplicationModel.StartupTaskState.Enabled;
                    }
                    else
                    {
                        // There's no explicit disable API; disable by disabling the task via State property is not allowed.
                        // The recommended approach is to call DisableAsync on later SDKs, but fallback to leaving user to disable.
                        // Some runtimes support startupTask.Disable(); try-catch.
                        try { startupTask.Disable(); }
                        catch { }
                    }
                }
            }
            catch
            {
                // Not packaged / API unavailable — attempt registry run key for unpackaged app.
                try
                {
                    var appName = "YAHAA";
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        using RegistryKey? key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                        if (enabled)
                            key?.SetValue(appName, $"\"{exe}\"");
                        else
                            key?.DeleteValue(appName, false);
                    }
                }
                catch
                {
                    // best-effort; ignore failures
                }
            }

            AppSettings.SetLaunchOnStartup(enabled);
            LaunchStatusText.Text = enabled ? "Enabled" : "Disabled";
        }

        private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = await PickFolderAsync();
            if (folder is not null)
            {
                AppSettings.SetScriptsFolder(folder);
                UpdateScriptsUi();
            }
        }

        private static async Task<string?> PickFolderAsync()
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }

        private void UpdateScriptsUi()
        {
            ScriptsFolderPanel.Visibility = AppSettings.ScriptsEnabled ? Visibility.Visible : Visibility.Collapsed;
            ScriptsFolderText.Text = string.IsNullOrWhiteSpace(AppSettings.ScriptsFolder)
                ? "(no folder selected)"
                : AppSettings.ScriptsFolder;
        }

        private void Rerun_Click(object sender, RoutedEventArgs e) => App.Current.GoToSetup();

        private void SignOut_Click(object sender, RoutedEventArgs e)
        {
            DeviceStatusService.Stop();
            ScriptBridge.Stop();
            LocationService.Stop();
            RegistrationStore.ClearWebhook();
            ConfigStore.Clear();
            App.Current.GoToSetup();
        }

        /// <summary>
        /// Validates the values currently in the fields. Returns null if a required field is
        /// missing (an error InfoBar is shown in that case).
        /// </summary>
        private async Task<ConnectionResult?> RunValidationAsync()
        {
            var url = UrlBox.Text?.Trim() ?? string.Empty;
            var token = TokenBox.Password?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(url))
            {
                ShowResult(InfoBarSeverity.Error, "Please enter your Home Assistant URL.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                ShowResult(InfoBarSeverity.Error, "Please enter a long-lived access token.");
                return null;
            }

            SetBusy(true);
            var result = await HomeAssistantClient.ValidateAsync(url, token);
            SetBusy(false);
            return result;
        }

        private void SetBusy(bool busy)
        {
            TestButton.IsEnabled = !busy;
            SaveButton.IsEnabled = !busy;
            BusyRing.IsActive = busy;
            BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowResult(InfoBarSeverity severity, string message)
        {
            ResultBar.Severity = severity;
            ResultBar.Message = message;
            ResultBar.IsOpen = true;
        }
    }
}
