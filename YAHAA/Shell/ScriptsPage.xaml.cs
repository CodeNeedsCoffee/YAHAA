using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YAHAA.Scripts;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>A script row: its identity plus whether it's exposed to Home Assistant.</summary>
    public sealed class ScriptRow
    {
        public required string Name { get; init; }
        public required string Kind { get; init; }
        public required string FullPath { get; init; }
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Lists the .ps1/.bat scripts in the configured folder. Each can be run on the spot, and
    /// toggled on/off to control whether it's exposed as a button in Home Assistant.
    /// </summary>
    public sealed partial class ScriptsPage : Page
    {
        public ScriptsPage()
        {
            InitializeComponent();
            Loaded += (_, _) => LoadScripts();
        }

        private void LoadScripts()
        {
            var folder = AppSettings.ScriptsFolder;
            FolderText.Text = string.IsNullOrWhiteSpace(folder)
                ? "No folder selected — choose one in Settings."
                : folder;

            var rows = ScriptCatalog.Enumerate(folder)
                .Select(s => new ScriptRow
                {
                    Name = s.Name,
                    Kind = s.Kind,
                    FullPath = s.FullPath,
                    Enabled = AppSettings.IsScriptEnabled(s.Name),
                })
                .ToList();

            ScriptsList.ItemsSource = rows;

            var empty = rows.Count == 0;
            EmptyText.Text = string.IsNullOrWhiteSpace(folder)
                ? "Choose a scripts folder in Settings to see your scripts here."
                : "No .ps1 or .bat files were found in this folder.";
            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ScriptsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadScripts();

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ScriptRow row)
                ScriptRunner.Run(row.FullPath);
        }

        private void Script_Toggled(object sender, RoutedEventArgs e)
        {
            // Read IsOn directly: the Toggled event fires before the TwoWay binding writes back to
            // row.Enabled, so row.Enabled is still stale here. SetScriptEnabled is idempotent, so the
            // initial bind (matching saved state) is a no-op.
            if (sender is ToggleSwitch { DataContext: ScriptRow row } toggle)
                AppSettings.SetScriptEnabled(row.Name, toggle.IsOn);
        }
    }
}
