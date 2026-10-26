using System.Windows;
using FW.EasyMvvm.Core;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for VsTemplateProjectCreatorView.xaml
    /// </summary>
    public partial class VsTemplateProjectCreatorView : Window, IView
    {
        public VsTemplateProjectCreatorView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            MessageBox.Show(message);
        }
    }
}
