using System.Diagnostics;

namespace RPA.AiAgent.Control
{
    /// <summary>
    /// Interaction logic for OpenRouterSettingsControl.xaml
    /// </summary>
    public partial class OpenRouterSettingsControl : System.Windows.Controls.UserControl
    {
        public OpenRouterSettingsControl()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadApiKey();
        }

        private void LoadApiKey()
        {
            string savedApiKey = Properties.Settings.Default.OpenRouterApiKey;
            if (!string.IsNullOrEmpty(savedApiKey))
            {
                OpenRouterApiKeyBox.Password = savedApiKey;
            }
        }

        private void OpenRouterApiKeyBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            Properties.Settings.Default.OpenRouterApiKey = OpenRouterApiKeyBox.Password;
            Properties.Settings.Default.Save();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            _ = Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}