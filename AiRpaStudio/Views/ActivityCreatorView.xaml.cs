using System.Windows;
using FW.EasyMvvm.Core;

namespace RPA.Views
{
    /// <summary>
    /// Interaction logic for ActivityCreatorView.xaml
    /// </summary>
    public partial class ActivityCreatorView : Window, IView
    {
        public ActivityCreatorView()
        {
            InitializeComponent();
        }

        void IView.OnSendMessage(string message)
        {
            MessageBox.Show(message);
        }
    }
}
