using System.Windows;
using System.Windows.Controls;
using EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace ProxyChecker.Views
{
    /// <summary>
    /// Interaction logic for ProxiesView.xaml
    /// </summary>
    public partial class ProxiesView : MetroWindow, IView
    {
        public ProxiesView()
        {
            InitializeComponent();
        }

        public async void OnSendMessageAsync(string message)
        {
            _ = await this.ShowMessageAsync("", message);
        }

        private void ShowDetailsClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button button = sender as Button;
            button.TryFindParent<DataGridRow>().DetailsVisibility = Visibility.Visible;
        }

        private void HideDetailsClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button button = sender as Button;
            button.TryFindParent<DataGridRow>().DetailsVisibility = Visibility.Collapsed;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            //Console.WriteLine(((ComboBox)sender).SelectedIndex);
            //if (((ComboBox)sender).SelectedIndex == 0)
            //{
            //    socksProxyComboBox.Visibility = Visibility.Collapsed;
            //    httpProxyComboBox.Visibility = Visibility.Visible;
            //}
            //else
            //{
            //    socksProxyComboBox.Visibility = Visibility.Visible;
            //    httpProxyComboBox.Visibility = Visibility.Collapsed;
            //}
        }

#pragma warning disable IDE1006 // Naming Styles
        private void proxyListTextBox_TextChanged(object sender, TextChangedEventArgs e)
#pragma warning restore IDE1006 // Naming Styles
        {

        }
    }
}
