using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using YAHAA.Services;

namespace YAHAA.Setup.Steps
{
    /// <summary>Second wizard step: collect and reachability-check the server URL.</summary>
    public sealed partial class ServerStep : Page
    {
        private SetupFlow? _flow;

        public ServerStep()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _flow = (SetupFlow)e.Parameter;
            UrlBox.Text = _flow.ServerUrl;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e) => await ConnectAsync();

        private async void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await ConnectAsync();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _flow?.Back();

        private async Task ConnectAsync()
        {
            if (_flow is null) return;

            var url = UrlBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                ShowError("Please enter your Home Assistant URL.");
                return;
            }

            SetBusy(true);
            var result = await HomeAssistantClient.CheckReachableAsync(url);
            SetBusy(false);

            if (!result.Success)
            {
                ShowError(result.ErrorMessage ?? "Couldn't reach that address.");
                return;
            }

            ErrorBar.IsOpen = false;
            _flow.ServerUrl = HomeAssistantClient.NormalizeUrl(url);
            _flow.Next();
        }

        private void SetBusy(bool busy)
        {
            ConnectButton.IsEnabled = !busy;
            UrlBox.IsEnabled = !busy;
            BusyRing.IsActive = busy;
            BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ConnectLabel.Text = busy ? "Connecting…" : "Connect";
        }

        private void ShowError(string message)
        {
            ErrorBar.Message = message;
            ErrorBar.IsOpen = true;
        }
    }
}
