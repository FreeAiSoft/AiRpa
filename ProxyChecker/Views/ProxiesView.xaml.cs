using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyMvvm.Core;

namespace ProxyChecker.Views
{
    /// <summary>
    /// Interaction logic for ProxiesView.xaml
    /// </summary>
    public partial class ProxiesView : Window, IView
    {
        public ProxiesView()
        {
            InitializeComponent();
        }

        public void OnSendMessageAsync(string message)
        {
            MessageBox.Show(message);
        }

        private void ShowDetailsClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button button = sender as Button;
            FindParent<DataGridRow>(button).DetailsVisibility = Visibility.Visible;
        }

        private void HideDetailsClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            Button button = sender as Button;
            FindParent<DataGridRow>(button).DetailsVisibility = Visibility.Collapsed;
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

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }
    }
}
