using System;
using System.Windows;
using FW.EasyMvvm.Core;
using RPA.Helpers;

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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна (W-05): DWM на Windows 11. На Windows 10 диалог с системным заголовком
            // остаётся прямоугольным — WindowChrome такому окну навязывать нельзя (слетит caption).
            _ = WindowCorner.Enable(this);
        }

        void IView.OnSendMessage(string message)
        {
            ErrorDialog.Show(message, this);
        }
    }
}
