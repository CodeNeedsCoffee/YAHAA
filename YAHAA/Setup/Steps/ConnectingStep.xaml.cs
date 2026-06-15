using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YAHAA.Services;

namespace YAHAA.Setup.Steps
{
    /// <summary>
    /// Final wizard step: validates the connection against /api/config. On success it persists
    /// the config and enters the app; on failure it offers a way back to edit the details.
    /// </summary>
    public sealed partial class ConnectingStep : Page
    {
        private SetupFlow? _flow;

        public ConnectingStep()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _flow = (SetupFlow)e.Parameter;
            _ = ValidateAsync();
        }

        private async Task ValidateAsync()
        {
            if (_flow is null) return;

            ShowBusy($"Connecting to {_flow.ServerUrl}…");

            var result = await HomeAssistantClient.ValidateAsync(_flow.ServerUrl, _flow.Token);

            if (result.Success)
            {
                _flow.LocationName = result.LocationName;
                _flow.Version = result.Version;

                ConfigStore.Save(_flow.ServerUrl, _flow.Username, _flow.Token);

                ShowSuccess(result);
                await Task.Delay(1400);
                App.Current.GoToShell();
            }
            else
            {
                ShowError(result.ErrorMessage ?? "Couldn't connect to Home Assistant.");
            }
        }

        private void ShowBusy(string message)
        {
            BusyText.Text = message;
            BusyPanel.Visibility = Visibility.Visible;
            SuccessPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowSuccess(ConnectionResult result)
        {
            var name = string.IsNullOrWhiteSpace(result.LocationName) ? "your home" : result.LocationName;
            SuccessSubtitle.Text = string.IsNullOrWhiteSpace(result.Version)
                ? $"Connected to {name}."
                : $"Connected to {name} • Home Assistant {result.Version}";

            BusyPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Visible;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            BusyPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        private void EditDetails_Click(object sender, RoutedEventArgs e) => _flow?.Back();
    }
}
