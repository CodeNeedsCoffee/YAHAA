using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using YAHAA.Common;
using YAHAA.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace YAHAA
{
    /// <summary>
    /// The application window. Hosts a custom title bar (blended with the Mica backdrop) and a
    /// root frame whose content is chosen at startup (setup wizard vs. main shell). Closing the
    /// window hides it to the system tray so the app keeps running in the background.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly TrayIcon _trayIcon = new();
        private bool _forceClose;

        public MainWindow()
        {
            InitializeComponent();

            SizeAndCenter(1100, 760);
            ConfigureTitleBar();
            ApplyWindowIcon();
            ConfigureTrayIcon();

            UpdateTitleBarLogo();
            AppSettings.LogoChanged += OnLogoChanged;
            Closed += (_, _) => AppSettings.LogoChanged -= OnLogoChanged;

            AppWindow.Closing += OnAppWindowClosing;
        }

        /// <summary>Applies the currently selected logo to the title bar, taskbar, and tray.</summary>
        private void OnLogoChanged()
        {
            UpdateTitleBarLogo();
            ApplyWindowIcon();
            _trayIcon.UpdateIcon(AppSettings.CurrentLogoIconPath);
        }

        private void UpdateTitleBarLogo() =>
            TitleBarLogo.Source = new BitmapImage(new Uri(AppSettings.CurrentLogoImageUri));

        /// <summary>The root navigation frame exposed to <see cref="App"/>.</summary>
        public Frame RootFrame => rootFrame;

        private void SizeAndCenter(int width, int height)
        {
            AppWindow.Resize(new SizeInt32(width, height));

            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (display is null) return;

            var x = display.WorkArea.X + (display.WorkArea.Width - width) / 2;
            var y = display.WorkArea.Y + (display.WorkArea.Height - height) / 2;
            AppWindow.Move(new PointInt32(x, y));
        }

        private void ConfigureTitleBar()
        {
            // Draw our own content all the way up into the title bar so there is no Win32 chrome.
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Let the system caption buttons (min/max/close) blend with the Mica backdrop.
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        private void ApplyWindowIcon()
        {
            var iconPath = AppSettings.CurrentLogoIconPath;
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }

        private void ConfigureTrayIcon()
        {
            _trayIcon.Create(AppSettings.CurrentLogoIconPath, "YAHAA", onOpen: ShowFromTray, onExit: ExitApp);
        }

        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // The "X" button hides to tray instead of exiting, so background reporting keeps running.
            if (_forceClose) return;
            args.Cancel = true;
            AppWindow.Hide();
        }

        private void ShowFromTray()
        {
            AppWindow.Show();
            Activate();
            AppWindow.MoveInZOrderAtTop();
        }

        private async void ExitApp()
        {
            _forceClose = true;
            _trayIcon.Dispose();

            // Mark sensors unknown in HA before quitting so stale values don't linger.
            ScriptBridge.Stop();
            await DeviceStatusService.ReportOfflineAndStopAsync();

            Close();
        }
    }
}
