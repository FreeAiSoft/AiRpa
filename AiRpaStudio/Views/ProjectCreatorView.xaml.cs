using System;
using System.Windows;
using RPA.Helpers;
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна (W-03): DWM на Windows 11. На Windows 10 диалог с системным заголовком
            // остаётся прямоугольным — WindowChrome такому окну навязывать нельзя (слетит caption).
            WindowCorner.Enable(this);
        }

        void IView.OnSendMessage(string message)
        {
            _ = MessageBox.Show(message);
        }
    }
}
