using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>
    /// The main application shell shown after setup completes. Hosts a NavigationView with a
    /// Home page, an optional Scripts page, and the built-in Settings entry.
    /// </summary>
    public sealed partial class MainShellPage : Page
    {
        public MainShellPage()
        {
            InitializeComponent();
            AppSettings.ScriptsChanged += OnScriptsChanged;
            Unloaded += (_, _) => AppSettings.ScriptsChanged -= OnScriptsChanged;
        }

        private void Nav_Loaded(object sender, RoutedEventArgs e)
        {
            ScriptsItem.Visibility = AppSettings.ScriptsEnabled ? Visibility.Visible : Visibility.Collapsed;

            // Selecting the item raises SelectionChanged, which performs the initial navigation.
            Nav.SelectedItem = HomeItem;
        }

        private void OnScriptsChanged() => DispatcherQueue.TryEnqueue(() =>
        {
            ScriptsItem.Visibility = AppSettings.ScriptsEnabled ? Visibility.Visible : Visibility.Collapsed;

            if (!AppSettings.ScriptsEnabled && ContentFrame.CurrentSourcePageType == typeof(ScriptsPage))
                Nav.SelectedItem = HomeItem;
        });

        private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ContentFrame.Navigate(typeof(SettingsPage), null, args.RecommendedNavigationTransitionInfo);
                return;
            }

            if (args.SelectedItem is NavigationViewItem { Tag: string tag })
            {
                switch (tag)
                {
                    case "home":
                        ContentFrame.Navigate(typeof(HomePage), null, args.RecommendedNavigationTransitionInfo);
                        break;
                    case "scripts":
                        ContentFrame.Navigate(typeof(ScriptsPage), null, args.RecommendedNavigationTransitionInfo);
                        break;
                }
            }
        }
    }
}
