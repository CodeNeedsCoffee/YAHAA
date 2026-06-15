using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace YAHAA.Setup.Steps
{
    /// <summary>First wizard step: branding and a single call to action.</summary>
    public sealed partial class WelcomeStep : Page
    {
        private SetupFlow? _flow;

        public WelcomeStep()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _flow = (SetupFlow)e.Parameter;
        }

        private void GetStarted_Click(object sender, RoutedEventArgs e) => _flow?.Next();
    }
}
