using System;
using System.Windows;
using RPA.Enums;
using RPA.Helpers;

namespace RPA.Views
{
    /// <summary>
    /// Темизированный вопрос «есть несохранённые изменения» — замена системного
    /// <c>MessageBox</c> (Yes/No/Cancel). Кнопки возвращают результат через
    /// <see cref="Result"/>; закрытие крестиком, Alt+F4 или Esc оставляет
    /// <see cref="UnsavedChangesResult.Cancel"/> (значение по умолчанию).
    /// </summary>
    public partial class UnsavedChangesView : Window
    {
        public UnsavedChangesView()
        {
            InitializeComponent();
            ApplyLocalizedFallbacks();
        }

        /// <summary>Выбор пользователя. По умолчанию — «Отмена».</summary>
        public UnsavedChangesResult Result { get; private set; } = UnsavedChangesResult.Cancel;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна (этап 5, M-03): DWM на Windows 11. На Windows 10 диалог
            // с системным заголовком остаётся прямоугольным.
            _ = WindowCorner.Enable(this);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UnsavedChangesResult.Save;
            Close();
        }

        private void DiscardButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UnsavedChangesResult.Discard;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UnsavedChangesResult.Cancel;
            Close();
        }

        /// <summary>
        /// Подписи приходят из словаря ресурсов (<c>{DynamicResource lang_*}</c>), который
        /// создаёт <c>App.Language</c>. Если словаря ещё нет — подставляем значения через
        /// <see cref="LocalizeHelper"/> (как в <see cref="ErrorView"/>).
        /// </summary>
        private void ApplyLocalizedFallbacks()
        {
            if (string.IsNullOrEmpty(Title))
            {
                string title = Localize("lang_unsaved_title");
                if (!string.IsNullOrEmpty(title))
                {
                    Title = title;
                }
            }

            if (string.IsNullOrEmpty(MessageLabel.Text))
            {
                MessageLabel.Text = Localize("lang_unsaved_message");
            }

            if (string.IsNullOrEmpty(SaveButton.Content as string))
            {
                SaveButton.Content = Localize("lang_unsaved_save");
            }

            if (string.IsNullOrEmpty(DiscardButton.Content as string))
            {
                DiscardButton.Content = Localize("lang_unsaved_dont_save");
            }

            if (string.IsNullOrEmpty(CancelButton.Content as string))
            {
                CancelButton.Content = Localize("lang_unsaved_cancel");
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
                // Отсутствующий ресурс не должен мешать показать сам вопрос.
                return null;
            }
        }
    }
}
