using FW.EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for ActivityCreatorView.xaml
    /// </summary>
    public partial class ActivityCreatorView : MetroWindow, IView
    {
        public ActivityCreatorView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            _ = this.ShowMessageAsync("", message);
        }
    }
}
