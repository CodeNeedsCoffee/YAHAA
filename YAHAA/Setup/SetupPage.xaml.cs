using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using YAHAA.Services;
using YAHAA.Setup.Steps;

namespace YAHAA.Setup
{
    /// <summary>
    /// Hosts the setup wizard: a constant brand header and progress dots around a frame that
    /// slides between steps. Pre-seeds the flow with any existing config so re-running setup
    /// shows current values.
    /// </summary>
    public sealed partial class SetupPage : Page
    {
        private SetupFlow? _flow;

        public SetupPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _flow = new SetupFlow(StepFrame, new[]
            {
                typeof(WelcomeStep),
                typeof(ServerStep),
                typeof(CredentialsStep),
                typeof(ConnectingStep),
            })
            {
                ServerUrl = ConfigStore.ServerUrl,
                Username = ConfigStore.Username,
                Token = ConfigStore.Token,
            };

            _flow.StepChanged += OnStepChanged;
            _flow.Start();
        }

        private void OnStepChanged(int index, int total)
        {
            DotsPanel.Children.Clear();
            for (int i = 0; i < total; i++)
            {
                var active = i == index;
                DotsPanel.Children.Add(new Rectangle
                {
                    Width = active ? 22 : 8,
                    Height = 8,
                    RadiusX = 4,
                    RadiusY = 4,
                    Fill = active
                        ? (Brush)Application.Current.Resources["HaBlueBrush"]
                        : new SolidColorBrush(Colors.Gray) { Opacity = 0.35 },
                });
            }
        }
    }
}
