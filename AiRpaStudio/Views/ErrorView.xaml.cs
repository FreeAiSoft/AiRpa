using System;
using System.Windows;
using RPA.Helpers;

namespace RPA.Views
{
    /// <summary>
    /// Окно ошибки студии — единственный способ показать пользователю ошибку
    /// (вместо <c>MessageBox.Show</c>, который в тёмной теме даёт белое окно и не даёт
    /// скопировать текст).
    /// <para>
    /// Текст ошибки лежит в readonly <c>TextBox</c>: его можно выделить мышью и скопировать
    /// через <c>Ctrl+C</c> или кнопкой «Копировать» — это важно, потому что пользователь
    /// пересылает текст ошибки в поддержку/разработчику.
    /// </para>
    /// <para>
    /// Окно открывается не через <c>MvvmFactory</c> (как остальные диалоги студии), а
    /// напрямую из <see cref="ErrorDialog"/>: оно должно показываться из обработчика
    /// необработанного исключения, когда ни модели, ни DataContext уже нет.
    /// </para>
    /// </summary>
    public partial class ErrorView : Window
    {
        public ErrorView()
        {
            InitializeComponent();
            ApplyLocalizedFallbacks();
        }

        /// <summary>Текст ошибки. <c>null</c> трактуется как пустая строка.</summary>
        public string Message
        {
            get => MessageTextBox.Text ?? string.Empty;
            set
            {
                MessageTextBox.Text = value ?? string.Empty;
                MessageTextBox.CaretIndex = 0;
                MessageTextBox.ScrollToHome();
                CopiedLabel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Заголовок внутри окна (для падения — «Критическая ошибка»).</summary>
        public string Header
        {
            get => HeaderLabel.Text ?? string.Empty;
            set
            {
                string header = value ?? string.Empty;
                HeaderLabel.Text = header;

                if (!string.IsNullOrEmpty(header))
                {
                    // Заголовок окна повторяет внутренний: у крэша он отличается от «Ошибка».
                    Title = header;
                }
            }
        }

        /// <summary>Видна ли подсказка «Скопировано».</summary>
        public bool IsCopiedHintVisible => CopiedLabel.Visibility == Visibility.Visible;

        /// <summary>
        /// Кладёт текст ошибки в буфер обмена. Возвращает <c>false</c>, если копировать было
        /// нечего или буфер недоступен; в любом случае текст предварительно выделяется,
        /// чтобы его можно было скопировать вручную.
        /// </summary>
        public bool CopyMessage()
        {
            string text = Message;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            // Выделение — страховка: в сессиях без доступа к буферу обмена (служба, RDP)
            // пользователю остаётся Ctrl+C по выделенному тексту.
            _ = MessageTextBox.Focus();
            MessageTextBox.SelectAll();

            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception)
            {
                // Буфер обмена может быть занят другим процессом — не роняем окно ошибки.
                return false;
            }

            if (string.IsNullOrEmpty(CopiedLabel.Text))
            {
                CopiedLabel.Text = Localize("lang_error_copied");
            }

            CopiedLabel.Visibility = Visibility.Visible;
            return true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна (этап 5, M-03): DWM на Windows 11. На Windows 10 диалог
            // с системным заголовком остаётся прямоугольным.
            _ = WindowCorner.Enable(this);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            _ = CopyMessage();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Подписи приходят из словаря ресурсов (<c>{DynamicResource lang_*}</c>), который
        /// создаёт <c>App.Language</c>. Если словаря ещё нет — падение на старте приложения,
        /// окно из оффлайн-потока — подставляем значения через <see cref="LocalizeHelper"/>.
        /// </summary>
        private void ApplyLocalizedFallbacks()
        {
            if (string.IsNullOrEmpty(Title))
            {
                Title = Localize("lang_error_title");
            }

            if (string.IsNullOrEmpty(HeaderLabel.Text))
            {
                HeaderLabel.Text = Localize("lang_error_title");
            }

            if (string.IsNullOrEmpty(CopyButton.Content as string))
            {
                CopyButton.Content = Localize("lang_error_copy");
            }

            if (string.IsNullOrEmpty(CloseButton.Content as string))
            {
                CloseButton.Content = Localize("lang_close");
            }

            if (string.IsNullOrEmpty(CopiedLabel.Text))
            {
                CopiedLabel.Text = Localize("lang_error_copied");
            }
        }

        private static string Localize(string key)
        {
            try
            {
                return LocalizeHelper.Get(key);
            }
            catch (Exception)
            {
                // Отсутствующий ресурс не должен мешать показать саму ошибку.
                return null;
            }
        }
    }
}
