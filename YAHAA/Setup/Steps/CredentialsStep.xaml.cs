using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace YAHAA.Setup.Steps
{
    /// <summary>Third wizard step: collect username and the long-lived access token.</summary>
    public sealed partial class CredentialsStep : Page
    {
        private SetupFlow? _flow;

        public CredentialsStep()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _flow = (SetupFlow)e.Parameter;
            UsernameBox.Text = _flow.Username;
            TokenBox.Password = _flow.Token;
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            if (_flow is null) return;

            var token = TokenBox.Password?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                ErrorBar.Message = "A long-lived access token is required to sign in.";
                ErrorBar.IsOpen = true;
                return;
            }

            ErrorBar.IsOpen = false;
            _flow.Username = UsernameBox.Text?.Trim() ?? string.Empty;
            _flow.Token = token;
            _flow.Next();
        }

        private void Back_Click(object sender, RoutedEventArgs e) => _flow?.Back();
    }
}
