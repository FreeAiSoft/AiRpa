using System;
using System.Windows;
using Agent.Mvvm;
using RPA.Helpers;

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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна (W-04): DWM на Windows 11. На Windows 10 диалог с системным заголовком
            // остаётся прямоугольным — WindowChrome такому окну навязывать нельзя (слетит caption).
            _ = WindowCorner.Enable(this);
        }

        bool? IView.ShowDialogMessage(string message)
        {
            ErrorDialog.Show(message, this);
            return null;
        }

        void IView.NotifyMessage(Notify notify) { }
    }
}
