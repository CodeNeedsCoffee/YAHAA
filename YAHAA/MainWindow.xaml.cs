using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace YAHAA
{
    /// <summary>
    /// The application window. Hosts a single root frame; its content is chosen at startup
    /// (setup wizard vs. main shell) by <see cref="App.OnLaunched"/>.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            SizeAndCenter(1100, 760);
        }

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
    }
}
