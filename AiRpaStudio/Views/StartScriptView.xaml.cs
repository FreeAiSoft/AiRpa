using System;
using System.Windows;
using RPA.Helpers;

namespace RPA.Views
{
    /// <summary>
    /// Диалог «Стартовый скрипт» (Ф1, шаг 4): ввод имени файла, который кнопка «Старт»
    /// запускает, когда активная вкладка — не открытый WF-дизайнер. Значение пишется в
    /// <c>Properties.Settings.Default.DefaultRunScript</c> и сохраняется методом <c>Save()</c>.
    /// </summary>
    public partial class StartScriptView : Window
    {
        public StartScriptView()
        {
            InitializeComponent();
            ScriptFileTextBox.Text = Properties.Settings.Default.DefaultRunScript ?? string.Empty;
            ApplyLocalizedFallbacks();
        }

        /// <summary>Введённое имя файла без ведущих/концевых пробелов.</summary>
        public string ScriptFile => ScriptFileTextBox.Text?.Trim() ?? string.Empty;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна: DWM на Windows 11; на Windows 10 — region-fallback через WindowChrome.
            _ = WindowCorner.Enable(this);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.DefaultRunScript = ScriptFile;
            Properties.Settings.Default.Save();
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>
        /// Подписи приходят из словаря ресурсов (<c>{DynamicResource lang_*}</c>), который
        /// создаёт <c>App.Language</c>. Если словаря нет (тесты/оффлайн) — подставляем
        /// значения через <see cref="LocalizeHelper"/>.
        /// </summary>
        private void ApplyLocalizedFallbacks()
        {
            if (string.IsNullOrEmpty(Title))
            {
                Title = Localize("lang_start_script_title");
            }

            if (string.IsNullOrEmpty(SaveButton.Content as string))
            {
                SaveButton.Content = Localize("lang_start_script_save");
            }

            if (string.IsNullOrEmpty(CancelButton.Content as string))
            {
                CancelButton.Content = Localize("lang_cancel");
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
                // Отсутствующий ресурс не должен мешать показать диалог.
                return null;
            }
        }
    }
}
