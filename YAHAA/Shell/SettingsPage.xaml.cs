using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>
    /// Lets the user view and edit the saved connection, test or save changes, re-run the
    /// setup wizard, or sign out.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();

            UrlBox.Text = ConfigStore.ServerUrl;
            UsernameBox.Text = ConfigStore.Username;
            TokenBox.Password = ConfigStore.Token;
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

            ShowResult(InfoBarSeverity.Success, "Connection saved.");
        }

        private void Rerun_Click(object sender, RoutedEventArgs e) => App.Current.GoToSetup();

        private void SignOut_Click(object sender, RoutedEventArgs e)
        {
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
