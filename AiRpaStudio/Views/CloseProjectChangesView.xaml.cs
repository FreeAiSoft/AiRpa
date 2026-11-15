using System;
using System.Windows;
using RPA.Enums;
using RPA.Helpers;

namespace RPA.Views
{
    /// <summary>
    /// Темизированный вопрос «Закрыть проект с несохранёнными изменениями» — замена системного
    /// <c>MessageBox</c>. Кнопки возвращают результат через <see cref="Result"/>; закрытие крестиком,
    /// Alt+F4 или Esc оставляет <see cref="CloseProjectChangesResult.Cancel"/>.
    /// </summary>
    public partial class CloseProjectChangesView : Window
    {
        public CloseProjectChangesView()
        {
            InitializeComponent();
            ApplyLocalizedFallbacks();
        }

        /// <summary>Выбор пользователя. По умолчанию — «Отмена».</summary>
        public CloseProjectChangesResult Result { get; private set; } = CloseProjectChangesResult.Cancel;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _ = WindowCorner.Enable(this);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseProjectChangesResult.Save;
            Close();
        }

        private void BufferButton_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseProjectChangesResult.SaveToBuffer;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = CloseProjectChangesResult.Cancel;
            Close();
        }

        /// <summary>
        /// Подписи приходят из словаря ресурсов (<c>{DynamicResource lang_*}</c>), который
        /// создаёт <c>App.Language</c>. Если словаря ещё нет — подставляем значения через
        /// <see cref="LocalizeHelper"/> (как в <see cref="UnsavedChangesView"/>).
        /// </summary>
        private void ApplyLocalizedFallbacks()
        {
            if (string.IsNullOrEmpty(Title))
            {
                Title = Localize("lang_close_project_title") ?? Title;
            }

            if (string.IsNullOrEmpty(MessageLabel.Text))
            {
                MessageLabel.Text = Localize("lang_close_project_message");
            }

            if (string.IsNullOrEmpty(SaveButton.Content as string))
            {
                SaveButton.Content = Localize("lang_close_project_save");
            }

            if (string.IsNullOrEmpty(BufferButton.Content as string))
            {
                BufferButton.Content = Localize("lang_close_project_buffer");
            }

            if (string.IsNullOrEmpty(CancelButton.Content as string))
            {
                CancelButton.Content = Localize("lang_close_project_cancel");
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
                return null;
            }
        }
    }
}
