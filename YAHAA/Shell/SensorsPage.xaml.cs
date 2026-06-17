using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>A toggle row for one status sensor; current value refreshes live.</summary>
    public sealed class SensorRow : INotifyPropertyChanged
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required Func<bool> Read { get; init; }

        public bool Enabled { get; set; }
        public bool Pinned { get; set; }

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

    /// <summary>Lists the status sensors with an enable/disable toggle for each.</summary>
    public sealed partial class SensorsPage : Page
    {
        private readonly List<SensorRow> _rows;
        private readonly DispatcherTimer _refreshTimer;

        public SensorsPage()
        {
            InitializeComponent();

            _rows = SensorCatalog.All.Select(s => new SensorRow
            {
                Id = s.Id,
                DisplayName = s.DisplayName,
                Read = s.Read,
                Enabled = AppSettings.IsSensorEnabled(s.Id),
                Pinned = AppSettings.IsSensorPinned(s.Id),
            }).ToList();

            RefreshValues();
            SensorsList.ItemsSource = _rows;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, _) => RefreshValues();
            _refreshTimer.Start();

            Unloaded += (_, _) => _refreshTimer.Stop();
        }

        private void RefreshValues()
        {
            foreach (var row in _rows)
                row.CurrentValue = row.Read() ? "Active" : "Inactive";
        }

        private void Sensor_Toggled(object sender, RoutedEventArgs e)
        {
            // Read IsOn directly: the Toggled event fires before the TwoWay binding writes back to
            // row.Enabled, so row.Enabled is still stale here. SetSensorEnabled is idempotent, so the
            // initial bind (matching saved state) is a no-op.
            if (sender is ToggleSwitch { DataContext: SensorRow row } toggle)
                AppSettings.SetSensorEnabled(row.Id, toggle.IsOn);
        }

        private void SensorPin_Click(object sender, RoutedEventArgs e)
        {
            // Read IsChecked from the control (set before Click fires), not the stale bound value.
            if (sender is ToggleButton { DataContext: SensorRow row } button)
                AppSettings.SetSensorPinned(row.Id, button.IsChecked == true);
        }
    }
}
