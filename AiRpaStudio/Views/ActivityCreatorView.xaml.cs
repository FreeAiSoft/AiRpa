using System;
using System.Windows;
using Agent.Mvvm;
using RPA.Helpers;

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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна (W-02): DWM на Windows 11. На Windows 10 диалог с системным заголовком
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
