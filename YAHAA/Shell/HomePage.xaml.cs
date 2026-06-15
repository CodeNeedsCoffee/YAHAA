using Microsoft.UI.Xaml.Controls;
using YAHAA.Services;

namespace YAHAA.Shell
{
    /// <summary>Landing page after setup; greets the user and shows the active connection.</summary>
    public sealed partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();

            Greeting.Text = string.IsNullOrWhiteSpace(ConfigStore.Username)
                ? "Welcome home"
                : $"Welcome home, {ConfigStore.Username}";

            ServerText.Text = ConfigStore.ServerUrl;
        }
    }
}
