using FW.EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for AppSettingsView.xaml
    /// </summary>
    public partial class AppSettingsView : MetroWindow, IView
    {
        public AppSettingsView()
        {
            InitializeComponent();
        }

        public BaseViewModel ViewModel { get; set; }

        void IView.OnSendMessage(string message)
        {
            _ = this.ShowMessageAsync("", message);
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //if (((ViewModels.AppSettingsViewModel)DataContext).IsBusy)
            //{
            //    ((ViewModels.AppSettingsViewModel)DataContext).TryClose();
            //    e.Cancel = true;
            //}
        }
    }
}
