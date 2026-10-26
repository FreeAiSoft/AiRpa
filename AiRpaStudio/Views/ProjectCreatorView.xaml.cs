using System.Windows;
using FW.EasyMvvm.Core;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for ProjectCreatorView.xaml
    /// </summary>
    public partial class ProjectCreatorView : Window, IView
    {
        public ProjectCreatorView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            MessageBox.Show(message);
        }
    }
}
