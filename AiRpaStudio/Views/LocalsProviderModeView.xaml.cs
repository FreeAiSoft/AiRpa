using System;
using System.Windows;
using ActivitiesTracker.Core;
using RPA.Helpers;

namespace RPA.Views
{
    /// <summary>
    /// Диалог «Локальные переменные»: выбор источника данных панели Locals
    /// (<see cref="LocalsProviderMode"/>). Значение пишется в
    /// <c>Properties.Settings.Default.LocalsProviderMode</c> и сохраняется методом <c>Save()</c>.
    /// </summary>
    public partial class LocalsProviderModeView : Window
    {
        public LocalsProviderModeView()
        {
            InitializeComponent();
            SelectMode(ReadMode());
            ApplyLocalizedFallbacks();
        }

        /// <summary>Выбранный пользователем режим (по умолчанию — Mixed).</summary>
        public LocalsProviderMode Mode
        {
            get
            {
                if (TrackingRadio.IsChecked == true)
                {
                    return LocalsProviderMode.Tracking;
                }

                if (ActivityInstanceRadio.IsChecked == true)
                {
                    return LocalsProviderMode.ActivityInstance;
                }

                return LocalsProviderMode.Mixed;
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Скругление окна: DWM на Windows 11; на Windows 10 — region-fallback через WindowChrome.
            _ = WindowCorner.Enable(this);
        }

        private void SelectMode(LocalsProviderMode mode)
        {
            switch (mode)
            {
                case LocalsProviderMode.Tracking:
                    TrackingRadio.IsChecked = true;
                    break;

                case LocalsProviderMode.ActivityInstance:
                    ActivityInstanceRadio.IsChecked = true;
                    break;

                default:
                    MixedRadio.IsChecked = true;
                    break;
            }
        }

        private static LocalsProviderMode ReadMode()
        {
            try
            {
                string raw = Properties.Settings.Default.LocalsProviderMode;
                if (Enum.TryParse(raw, true, out LocalsProviderMode mode))
                {
                    return mode;
                }
            }
            catch (Exception)
            {
                // Нечитаемая настройка не должна мешать открытию диалога.
            }

            return LocalsProviderMode.Mixed;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.LocalsProviderMode = Mode.ToString();
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
                Title = Localize("lang_locals_provider_mode_title");
            }

            if (string.IsNullOrEmpty(SaveButton.Content as string))
            {
                SaveButton.Content = Localize("lang_save");
            }

            if (string.IsNullOrEmpty(CancelButton.Content as string))
            {
                CancelButton.Content = Localize("lang_cancel");
            }

            if (string.IsNullOrEmpty(TrackingRadio.Content as string))
            {
                TrackingRadio.Content = Localize("lang_locals_provider_mode_tracking");
            }

            if (string.IsNullOrEmpty(ActivityInstanceRadio.Content as string))
            {
                ActivityInstanceRadio.Content = Localize("lang_locals_provider_mode_instance");
            }

            if (string.IsNullOrEmpty(MixedRadio.Content as string))
            {
                MixedRadio.Content = Localize("lang_locals_provider_mode_mixed");
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
