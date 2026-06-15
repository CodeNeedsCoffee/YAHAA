using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

            UrlBox.Text = ConfigStore.ServerUrl;
            UsernameBox.Text = ConfigStore.Username;
            TokenBox.Password = ConfigStore.Token;

            LogoChoice.SelectedIndex = (int)AppSettings.Logo;

            _initializing = true;
            DeviceNameBox.Text = AppSettings.EffectiveDeviceName;
            AppVersionText.Text = DeviceInfo.AppVersion;
            ReportToggle.IsOn = AppSettings.ReportingEnabled;
            IdleBox.Value = AppSettings.IdleThresholdSeconds / 60.0;
            DebounceSlider.Value = AppSettings.StatusDebounceSeconds;
            UpdateDebounceLabel();
            ScriptsToggle.IsOn = AppSettings.ScriptsEnabled;
            _initializing = false;

            UpdateScriptsUi();
            UpdateDeviceStatus();
            DeviceStatusService.StatusChanged += OnReportingStatusChanged;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _refreshTimer.Tick += (_, _) => UpdateDeviceStatus();
            _refreshTimer.Start();

            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();
            DeviceStatusService.StatusChanged -= OnReportingStatusChanged;
        }

        private void OnReportingStatusChanged() => DispatcherQueue.TryEnqueue(UpdateDeviceStatus);

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
                TokenBox.Password?.Trim() ?? string.Empty);

            // Reconnect the scripts bridge to the (possibly new) server.
            ScriptBridge.Restart();

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
