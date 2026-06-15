using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YAHAA.Scripts;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>Lists the .ps1/.bat scripts in the configured folder, each runnable on the spot.</summary>
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

            var items = ScriptCatalog.Enumerate(folder);
            ScriptsList.ItemsSource = items;

            var empty = items.Count == 0;
            EmptyText.Text = string.IsNullOrWhiteSpace(folder)
                ? "Choose a scripts folder in Settings to see your scripts here."
                : "No .ps1 or .bat files were found in this folder.";
            EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ScriptsList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => LoadScripts();

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ScriptItem item)
                ScriptRunner.Run(item);
        }
    }
}
