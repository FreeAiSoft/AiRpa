using FW.EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for VsTemplateProjectCreatorView.xaml
    /// </summary>
    public partial class VsTemplateProjectCreatorView : MetroWindow, IView
    {
        public VsTemplateProjectCreatorView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            _ = this.ShowMessageAsync("", message);
        }
    }
}
