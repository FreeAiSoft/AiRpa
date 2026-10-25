using FW.EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for VsProjectOpenView.xaml
    /// </summary>
    public partial class VsProjectOpenView : MetroWindow, IView
    {
        public VsProjectOpenView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            _ = this.ShowMessageAsync("", message);
        }
    }
}
