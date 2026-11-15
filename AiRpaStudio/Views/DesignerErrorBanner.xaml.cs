using System;
using System.Windows;
using System.Windows.Controls;
using RPA.Core;
using RPA.Core.Services;

namespace RPA.Views
{
    /// <summary>
    /// Баннер ошибки загрузки документа, показываемый в верхней части вкладки конструктора
    /// (дизайнер при этом сдвигается вниз). Иконка — родная ошибка WF (красный круг с «!»),
    /// рамка — семантика ошибки палитры студии.
    /// </summary>
    public partial class DesignerErrorBanner : UserControl
    {
        private DesignerErrorSnapshot _snapshot;

        public DesignerErrorBanner()
        {
            InitializeComponent();
            ErrorIcon.Fill = ValidationIconState.ErrorIconBrush();
        }

        /// <summary>Показывает снимок ошибки в баннере.</summary>
        public void Show(DesignerErrorSnapshot snapshot)
        {
            _snapshot = snapshot;
            MessageText.Text = string.IsNullOrWhiteSpace(snapshot?.Message)
                ? "Ошибка загрузки документа в конструкторе"
                : snapshot.Message;
            Visibility = Visibility.Visible;
        }

        private void OnCopyClick(object sender, RoutedEventArgs e)
        {
            string text = _snapshot?.BuildPromptText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);
            }
            catch
            {
                // буфер обмена занят — не критично
            }
        }

        private void OnSendClick(object sender, RoutedEventArgs e)
        {
            if (_snapshot == null)
            {
                return;
            }

            DesignerErrorReporter.RequestSend(_snapshot);
            Visibility = Visibility.Collapsed;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
        }
    }
}
