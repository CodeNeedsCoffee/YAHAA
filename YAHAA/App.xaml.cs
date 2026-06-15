using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using YAHAA.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace YAHAA
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>Strongly-typed accessor for the current application instance.</summary>
        public static new App Current => (App)Application.Current;

        /// <summary>The window-level frame that hosts either the setup wizard or the main shell.</summary>
        public Frame? RootFrame { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            ConfigStore.Load();

            var mainWindow = new MainWindow();
            _window = mainWindow;
            RootFrame = mainWindow.RootFrame;

            RootFrame.Navigate(ConfigStore.IsConfigured
                ? typeof(Shell.MainShellPage)
                : typeof(Setup.SetupPage));

            _window.Activate();
        }

        /// <summary>Navigates the root frame to the main app shell (after a successful setup).</summary>
        public void GoToShell() =>
            RootFrame?.Navigate(typeof(Shell.MainShellPage), null, new DrillInNavigationTransitionInfo());

        /// <summary>Navigates the root frame back into the setup wizard (re-run setup).</summary>
        public void GoToSetup() =>
            RootFrame?.Navigate(typeof(Setup.SetupPage), null, new DrillInNavigationTransitionInfo());
    }
}
