using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using YAHAA.Scripts;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>A pinned sensor row whose value refreshes live.</summary>
    public sealed partial class DashSensorRow : INotifyPropertyChanged
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required Func<bool> Read { get; init; }

        private string _currentValue = string.Empty;
        public string CurrentValue
        {
            get => _currentValue;
            set
            {
                if (_currentValue == value) return;
                _currentValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentValue)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>A pinned script row.</summary>
    public sealed class DashScriptRow
    {
        public required string Name { get; init; }
        public required string FullPath { get; init; }
    }

    /// <summary>
    /// The landing page and YAHAA dashboard: greets the user, shows the connection, and lists
    /// pinned sensors (live), pinned scripts (Run), and user-defined webhook actions.
    /// </summary>
    public sealed partial class HomePage : Page
    {
        private readonly DispatcherTimer _refreshTimer;
        private List<DashSensorRow> _sensorRows = [];

        public HomePage()
        {
            InitializeComponent();

            Greeting.Text = string.IsNullOrWhiteSpace(ConfigStore.Username)
                ? "Welcome home"
                : $"Welcome home, {ConfigStore.Username}";
            UpdateConnection();

            BuildLists();
            AppSettings.DashboardChanged += OnDashboardChanged;
            AppSettings.ScriptsChanged += OnScriptsChanged;
            ConfigStore.ActiveEndpointChanged += OnEndpointChanged;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, _) => RefreshSensorValues();
            _refreshTimer.Start();

            Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _refreshTimer.Stop();
            AppSettings.DashboardChanged -= OnDashboardChanged;
            AppSettings.ScriptsChanged -= OnScriptsChanged;
            ConfigStore.ActiveEndpointChanged -= OnEndpointChanged;
        }

        private void OnDashboardChanged() => DispatcherQueue.TryEnqueue(BuildLists);

        private void OnScriptsChanged() => DispatcherQueue.TryEnqueue(BuildLists);

        private void OnEndpointChanged() => DispatcherQueue.TryEnqueue(UpdateConnection);

        // Shows which endpoint is in use (local vs remote) so it's clear when the internal URL is active.
        private void UpdateConnection()
        {
            if (string.IsNullOrWhiteSpace(ConfigStore.InternalUrl))
            {
                ConnectionLabel.Text = "Connected";
                ServerText.Text = ConfigStore.ExternalUrl;
            }
            else
            {
                ConnectionLabel.Text = ConfigStore.ActiveEndpointIsInternal ? "Connected (local network)" : "Connected (remote)";
                ServerText.Text = ConfigStore.ServerUrl;
            }
        }

        private void BuildLists()
        {
            // Sensors: only those still in the catalog, in catalog order.
            _sensorRows = [.. SensorCatalog.All
                .Where(s => AppSettings.IsSensorPinned(s.Id))
                .Select(s => new DashSensorRow { Id = s.Id, DisplayName = s.DisplayName, Read = s.Read })];
            SensorsList.ItemsSource = _sensorRows;
            SensorsEmpty.Visibility = _sensorRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshSensorValues();

            // Scripts: the whole card is hidden when the Scripts feature is off.
            ScriptsCard.Visibility = AppSettings.ScriptsEnabled ? Visibility.Visible : Visibility.Collapsed;
            var scripts = ScriptCatalog.Enumerate(AppSettings.ScriptsFolder)
                .Where(s => AppSettings.IsScriptPinned(s.Name))
                .Select(s => new DashScriptRow { Name = s.Name, FullPath = s.FullPath })
                .ToList();
            ScriptsList.ItemsSource = scripts;
            ScriptsEmpty.Visibility = scripts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Actions.
            var actions = AppSettings.DashboardActions.ToList();
            ActionsList.ItemsSource = actions;
            ActionsEmpty.Visibility = actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshSensorValues()
        {
            foreach (var row in _sensorRows)
                row.CurrentValue = row.Read() ? "Active" : "Inactive";
        }

        private void RunScript_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: DashScriptRow row })
                ScriptRunner.Run(row.FullPath);
        }

        private async void AddAction_Click(object sender, RoutedEventArgs e)
        {
            var nameBox = new TextBox { Header = "Name", PlaceholderText = "e.g. Movie time" };
            var webhookBox = new TextBox
            {
                Header = "Home Assistant webhook",
                PlaceholderText = "webhook id or full https://…/api/webhook/… URL",
                Margin = new Thickness(0, 12, 0, 0),
            };

            var dialog = new ContentDialog
            {
                Title = "Add action",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                Content = new StackPanel { Children = { nameBox, webhookBox } },
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var name = nameBox.Text?.Trim() ?? string.Empty;
            var webhook = webhookBox.Text?.Trim() ?? string.Empty;
            if (name.Length == 0 || webhook.Length == 0)
            {
                ShowActionInfo(InfoBarSeverity.Error, "Enter both a name and a webhook.");
                return;
            }

            AppSettings.AddDashboardAction(new DashboardAction { Name = name, Webhook = webhook });
        }

        private async void TriggerAction_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not DashboardAction action) return;

            var button = sender as Button;
            if (button is not null) button.IsEnabled = false;

            var ok = await WebhookClient.TriggerAsync(action.Webhook);

            if (button is not null) button.IsEnabled = true;
            ShowActionInfo(ok ? InfoBarSeverity.Success : InfoBarSeverity.Error,
                ok ? $"Triggered “{action.Name}”." : $"Couldn't reach the webhook for “{action.Name}”.");
        }

        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: DashboardAction action })
                AppSettings.RemoveDashboardAction(action);
        }

        private void ShowActionInfo(InfoBarSeverity severity, string message)
        {
            ActionInfoBar.Severity = severity;
            ActionInfoBar.Message = message;
            ActionInfoBar.IsOpen = true;
        }
    }
}
