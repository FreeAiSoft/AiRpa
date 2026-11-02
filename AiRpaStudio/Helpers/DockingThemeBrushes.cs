using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Кисти AvalonDock, которые студия пишет в рантайме (единственная точка записи —
    /// <c>AppSettings.ReapplyDockManagerResources</c>, см. M-05 плана).
    /// <para>
    /// Тема <c>AvalonDock.Themes.Metro\Theme.xaml</c> подтягивает эти ключи по <c>DynamicResource</c>
    /// (вкладки панелей и документов, заголовки, ресайзеры, оверлей перетаскивания, Ctrl+Tab).
    /// Объявлять их в XAML словаря темы нельзя: словарь лежит в отдельной сборке, а значения нужны из
    /// палитры студии (<see cref="StudioPalette"/>). Поэтому кисти кладутся кодом в
    /// <c>DockingManager.Resources</c>, ресурсы приложения и ресурсы плавающих окон — собственные записи
    /// словаря имеют приоритет над merged-словарём темы, а DynamicResource-ссылки получают уведомление.
    /// </para>
    /// <para>
    /// Ключи <c>AvalonDock_ThemeMetro_BaseColor1..7</c> сохранены как legacy-алиасы на те же нейтральные
    /// значения (их проверяют тесты, на них могут ссылаться сторонние шаблоны), но сама тема должна
    /// использовать только нейтральные ключи <c>AvalonDock_*</c>.
    /// </para>
    /// </summary>
    public static class DockingThemeBrushes
    {
        // --- Вкладки: три ключа, которые читает тема Metro. ---

        /// <summary>Фон неактивной вкладки.</summary>
        public const string TabBackgroundKey = "AvalonDock_Base";

        /// <summary>Фон вкладки под курсором.</summary>
        public const string TabHoverKey = "AvalonDock_Hover";

        /// <summary>Фон выбранной (активной) вкладки — светлее неактивной (<see cref="TabBackgroundKey"/>).</summary>
        public const string TabActiveKey = "AvalonDock_Active";

        // --- Нейтральные ключи палитры. ---

        /// <summary>Единственный фон панелей, заголовков, вкладок и контента документов.</summary>
        public const string SurfaceKey = "AvalonDock_Surface";

        /// <summary>Фон неактивной вкладки / неактивного выделения.</summary>
        public const string SurfaceAltKey = "AvalonDock_SurfaceAlt";

        /// <summary>Основной текст.</summary>
        public const string ForegroundKey = "AvalonDock_Foreground";

        /// <summary>Неактивные вкладки и подписи.</summary>
        public const string ForegroundMutedKey = "AvalonDock_ForegroundMuted";

        /// <summary>Hairline-разделитель 1px между зонами.</summary>
        public const string SeparatorKey = "AvalonDock_Separator";

        /// <summary>Усиленный hairline: граница плавающего окна, hover ресайзера.</summary>
        public const string SeparatorStrongKey = "AvalonDock_SeparatorStrong";

        /// <summary>Выбранное состояние.</summary>
        public const string SelectionKey = "AvalonDock_Selection";

        /// <summary>Hover-состояние.</summary>
        public const string SelectionHoverKey = "AvalonDock_SelectionHover";

        /// <summary>Заливка рамки при перетаскивании вкладки (Surface @15%).</summary>
        public const string OverlayFillKey = "AvalonDock_OverlayFill";

        /// <summary>Обводка рамки при перетаскивании вкладки.</summary>
        public const string OverlayStrokeKey = "AvalonDock_OverlayStroke";

        /// <summary>Hover в переключателе документов (Ctrl+Tab).</summary>
        public const string NavigatorHoverKey = "AvalonDock_NavigatorHover";

        /// <summary>Выделение в переключателе документов.</summary>
        public const string NavigatorSelectedKey = "AvalonDock_NavigatorSelected";

        /// <summary>Выделение в неактивном переключателе документов.</summary>
        public const string NavigatorSelectedInactiveKey = "AvalonDock_NavigatorSelectedInactive";

        /// <summary>Hover по выделенной строке переключателя документов.</summary>
        public const string NavigatorSelectedHoverKey = "AvalonDock_NavigatorSelectedHover";

        /// <summary>Фон кнопки закрытия под курсором (единственный не-нейтральный цвет).</summary>
        public const string CloseHoverBackgroundKey = "AvalonDock_CloseHoverBackground";

        /// <summary>Текст кнопки закрытия под курсором.</summary>
        public const string CloseHoverForegroundKey = "AvalonDock_CloseHoverForeground";

        // --- Legacy-алиасы темы Metro. ---

        public const string LegacyBaseColor1Key = "AvalonDock_ThemeMetro_BaseColor1";
        public const string LegacyBaseColor2Key = "AvalonDock_ThemeMetro_BaseColor2";
        public const string LegacyBaseColor3Key = "AvalonDock_ThemeMetro_BaseColor3";
        public const string LegacyBaseColor4Key = "AvalonDock_ThemeMetro_BaseColor4";
        public const string LegacyBaseColor5Key = "AvalonDock_ThemeMetro_BaseColor5";
        public const string LegacyBaseColor6Key = "AvalonDock_ThemeMetro_BaseColor6";
        public const string LegacyBaseColor7Key = "AvalonDock_ThemeMetro_BaseColor7";

        /// <summary>Ключи вкладок (обратная совместимость с первым этапом темизации).</summary>
        public static readonly IReadOnlyList<string> Keys = new[]
        {
            TabBackgroundKey,
            TabHoverKey,
            TabActiveKey
        };

        /// <summary>Все нейтральные ключи палитры, которые пишет студия.</summary>
        public static readonly IReadOnlyList<string> PaletteKeys = new[]
        {
            SurfaceKey,
            SurfaceAltKey,
            ForegroundKey,
            ForegroundMutedKey,
            SeparatorKey,
            SeparatorStrongKey,
            SelectionKey,
            SelectionHoverKey,
            OverlayFillKey,
            OverlayStrokeKey,
            NavigatorHoverKey,
            NavigatorSelectedKey,
            NavigatorSelectedInactiveKey,
            NavigatorSelectedHoverKey,
            CloseHoverBackgroundKey,
            CloseHoverForegroundKey
        };

        /// <summary>Legacy-алиасы <c>BaseColor1..7</c>.</summary>
        public static readonly IReadOnlyList<string> LegacyKeys = new[]
        {
            LegacyBaseColor1Key,
            LegacyBaseColor2Key,
            LegacyBaseColor3Key,
            LegacyBaseColor4Key,
            LegacyBaseColor5Key,
            LegacyBaseColor6Key,
            LegacyBaseColor7Key
        };

        /// <summary>Всё, что обязана заполнять <see cref="Apply(ResourceDictionary, bool, ResourceDictionary)"/>.</summary>
        public static readonly IReadOnlyList<string> AllKeys = new[]
        {
            TabBackgroundKey,
            TabHoverKey,
            TabActiveKey,
            SurfaceKey,
            SurfaceAltKey,
            ForegroundKey,
            ForegroundMutedKey,
            SeparatorKey,
            SeparatorStrongKey,
            SelectionKey,
            SelectionHoverKey,
            OverlayFillKey,
            OverlayStrokeKey,
            NavigatorHoverKey,
            NavigatorSelectedKey,
            NavigatorSelectedInactiveKey,
            NavigatorSelectedHoverKey,
            CloseHoverBackgroundKey,
            CloseHoverForegroundKey,
            LegacyBaseColor1Key,
            LegacyBaseColor2Key,
            LegacyBaseColor3Key,
            LegacyBaseColor4Key,
            LegacyBaseColor5Key,
            LegacyBaseColor6Key,
            LegacyBaseColor7Key
        };

        /// <summary>
        /// Заполняет кисти AvalonDock в <paramref name="target"/> значениями палитры студии.
        /// </summary>
        /// <param name="target">Словарь, куда пишутся кисти (обычно <c>DockingManager.Resources</c>).</param>
        /// <param name="dark">Текущая тема студии.</param>
        /// <param name="paletteSource">
        /// Не используется: значения берутся из <see cref="StudioPalette"/>. Параметр оставлен для
        /// совместимости с существующими вызовами (раньше палитра MaterialDesign читалась из него).
        /// </param>
        public static void Apply(ResourceDictionary target, bool dark, ResourceDictionary paletteSource = null)
        {
            if (target == null)
            {
                return;
            }

            ApplyPalette(target, StudioPalette.For(dark));
        }

        /// <summary>Заполняет кисти AvalonDock значениями конкретной палитры.</summary>
        public static void ApplyPalette(ResourceDictionary target, StudioThemeColors palette)
        {
            if (target == null || palette == null)
            {
                return;
            }

            // Вкладки: неактивная — SurfaceAlt, hover — Hover, выбранная — TabActive.
            // Выбранная вкладка обязана быть светлее неактивной: раньше она брала Surface и сливалась с
            // фоном полосы вкладок, из-за чего переключатель «Проект / Активности / Структура» выглядел вдавленным.
            Set(target, TabBackgroundKey, palette.SurfaceAlt);
            Set(target, TabHoverKey, palette.Hover);
            Set(target, TabActiveKey, palette.TabActive);

            Set(target, SurfaceKey, palette.Surface);
            Set(target, SurfaceAltKey, palette.SurfaceAlt);
            Set(target, ForegroundKey, palette.Foreground);
            Set(target, ForegroundMutedKey, palette.ForegroundMuted);
            Set(target, SeparatorKey, palette.Separator);
            Set(target, SeparatorStrongKey, palette.SeparatorStrong);
            Set(target, SelectionKey, palette.Selected);
            Set(target, SelectionHoverKey, palette.Hover);
            Set(target, OverlayFillKey, palette.OverlayFill);
            Set(target, OverlayStrokeKey, palette.OverlayStroke);

            Set(target, NavigatorHoverKey, palette.Hover);
            Set(target, NavigatorSelectedKey, palette.Selected);
            Set(target, NavigatorSelectedInactiveKey, palette.SurfaceAlt);
            Set(target, NavigatorSelectedHoverKey, palette.Hover);

            Set(target, CloseHoverBackgroundKey, palette.CloseHoverBackground);
            Set(target, CloseHoverForegroundKey, palette.CloseHoverForeground);

            // Legacy-алиасы: те же нейтральные значения, без акцента.
            Set(target, LegacyBaseColor1Key, palette.Surface);
            Set(target, LegacyBaseColor2Key, palette.Foreground);
            Set(target, LegacyBaseColor3Key, palette.Selected);
            Set(target, LegacyBaseColor4Key, palette.Hover);
            Set(target, LegacyBaseColor5Key, palette.SurfaceAlt);
            Set(target, LegacyBaseColor6Key, palette.ForegroundMuted);
            Set(target, LegacyBaseColor7Key, palette.Separator);
        }

        private static void Set(ResourceDictionary target, string key, Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();

            // Remove + Add вместо простого присваивания: гарантирует уведомление
            // DynamicResource-ссылок при смене темы.
            target.Remove(key);
            target[key] = brush;
        }
    }
}
