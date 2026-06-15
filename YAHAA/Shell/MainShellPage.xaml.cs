using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace YAHAA.Shell
{
    /// <summary>
    /// The main application shell shown after setup completes. Hosts a NavigationView with a
    /// Home page and the built-in Settings entry.
    /// </summary>
    public sealed partial class MainShellPage : Page
    {
        public MainShellPage()
        {
            InitializeComponent();
        }

        private void Nav_Loaded(object sender, RoutedEventArgs e)
        {
            // Selecting the item raises SelectionChanged, which performs the initial navigation.
            Nav.SelectedItem = HomeItem;
        }

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
                }
            }
        }
    }
}
