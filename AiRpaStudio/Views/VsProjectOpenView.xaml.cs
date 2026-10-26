using System.Windows;
using FW.EasyMvvm.Core;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for VsProjectOpenView.xaml
    /// </summary>
    public partial class VsProjectOpenView : Window, IView
    {
        public VsProjectOpenView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            MessageBox.Show(message);
        }
    }
}
