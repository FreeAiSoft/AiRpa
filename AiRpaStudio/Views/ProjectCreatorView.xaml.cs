using FW.EasyMvvm.Core;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for ProjectCreatorView.xaml
    /// </summary>
    public partial class ProjectCreatorView : MetroWindow, IView
    {
        public ProjectCreatorView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            _ = this.ShowMessageAsync("", message);
        }
    }
}
