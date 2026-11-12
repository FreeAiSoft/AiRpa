using System.Collections.Generic;
using System.Windows;
using Agent.Core.Models;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Кисти-алиасы студии (<c>Studio.*</c>) и системные кисти WPF.
    /// <para>
    /// Значения берутся только из <see cref="StudioPalette"/> — никакого акцента MaterialDesign
    /// (<c>Primary</c>/<c>Secondary</c>) здесь не используется. Алиас <c>Studio.Surface</c> —
    /// единственный фон окна, панелей, заголовков и вкладок; состояние передаётся яркостью
    /// (<c>Studio.SurfaceAlt</c> → <c>Studio.Hover</c> → <c>Studio.Selected</c>) и hairline
    /// <c>Studio.Separator</c>.
    /// </para>
    /// <para>
    /// ВАЖНО: алиасы нельзя объявлять в ResourceDictionary как
    /// <c>&lt;DynamicResource x:Key="Studio.MenuForeground" ResourceKey="MaterialDesign.Brush.Foreground" /&gt;</c>.
    /// В этом случае в словарь попадает объект <c>System.Windows.ResourceReferenceExpression</c>
    /// (отложенная ссылка, а не Brush), и при применении значения через Style/Template
    /// (<c>StyleHelper.ApplyStyleOrTemplateValue</c>) WPF выбрасывает
    /// <c>InvalidOperationException: "System.Windows.ResourceReferenceExpression" не является
    /// допустимым значением для свойства "Foreground"</c>, что на этапе разбора BAML выглядит как
    /// <c>XamlParseException</c> при инициализации MenuItem.
    /// </para>
    /// Поэтому алиасы создаются в рантайме — реальными (замороженными) <see cref="SolidColorBrush"/>
    /// из активной палитры. Вызывать после каждой смены темы.
    /// </summary>
    public static class StudioBrushes
    {
        // --- Алиасы-роли (обратная совместимость: на них ссылаются существующие представления). ---
        public const string WindowBackgroundKey = "Studio.WindowBackground";
        public const string MenuBackgroundKey = "Studio.MenuBackground";
        public const string MenuForegroundKey = "Studio.MenuForeground";
        public const string ToolbarBackgroundKey = "Studio.ToolbarBackground";
        public const string ToolbarForegroundKey = "Studio.ToolbarForeground";
        public const string ToolbarBorderKey = "Studio.ToolbarBorder";
        public const string ToolbarHoverBackgroundKey = "Studio.ToolbarHoverBackground";
        public const string ToolbarPressedBackgroundKey = "Studio.ToolbarPressedBackground";
        public const string ToolbarCheckedBackgroundKey = "Studio.ToolbarCheckedBackground";
        public const string TitleBarBackgroundKey = "Studio.TitleBarBackground";
        public const string TitleBarForegroundKey = "Studio.TitleBarForeground";
        public const string TitleBarButtonHoverBackgroundKey = "Studio.TitleBarButtonHoverBackground";

        // --- Алиасы-токены палитры (docs\Vs2026_Theme_Plan.md, §2). ---
        public const string SurfaceKey = "Studio.Surface";
        public const string SurfaceAltKey = "Studio.SurfaceAlt";
        public const string HoverKey = "Studio.Hover";
        public const string SelectedKey = "Studio.Selected";
        public const string SeparatorKey = "Studio.Separator";
        public const string SeparatorStrongKey = "Studio.SeparatorStrong";
        public const string ForegroundKey = "Studio.Foreground";
        public const string ForegroundMutedKey = "Studio.ForegroundMuted";
        public const string CloseHoverBackgroundKey = "Studio.CloseHoverBackground";
        public const string CloseHoverForegroundKey = "Studio.CloseHoverForeground";
        public const string OverlayFillKey = "Studio.OverlayFill";
        public const string OverlayStrokeKey = "Studio.OverlayStroke";

        // --- Семантические цвета (docs\Vs2026_Theme_Plan.md §1 п.2). ---
        // Не акцент: кодируют состояние (запуск/пауза/стоп) и тип файла. Значения одинаковы в обеих
        // темах, поэтому живут вне StudioPalette — её токены обязаны быть нейтральными (chroma <= 6).
        public const string SemanticRunKey = "Studio.Semantic.Run";
        public const string SemanticPauseKey = "Studio.Semantic.Pause";
        public const string SemanticStopKey = "Studio.Semantic.Stop";
        public const string SemanticSaveKey = "Studio.Semantic.Save";
        public const string SemanticFolderIconKey = "Studio.Semantic.FolderIcon";
        public const string SemanticXamlFileIconKey = "Studio.Semantic.XamlFileIcon";
        public const string SemanticJsonFileIconKey = "Studio.Semantic.JsonFileIcon";

        // Бейдж ошибки поля C#-выражения (CSharpExpressionEditor\RoslynExpressionEditor.xaml). Голубой —
        // не красный: красный в студии закреплён за системной семантикой ошибки (иконка валидации на
        // активности — родной рисунок WF, кружок адаптера Validation.ErrorTemplate), а глиф в поле
        // сигналит «проверка этого поля». Цвет непрозрачный и одинаковый в обеих темах.
        public const string SemanticValidationKey = "Studio.Semantic.Validation";

        // Иконки фильтров панели «Log» (Debug…Fatal): цвет — кодировка уровня, то есть семантика,
        // а не акцент. Цвета непрозрачные (alpha 255): глиф обязан читаться на любой поверхности
        // панели. Значения одинаковы в тёмной и светлой теме. Trace сюда не входит: он нейтральный,
        // его иконка и буквы красятся Studio.Foreground (белый в тёмной теме, чёрный в светлой).
        public const string SemanticLogDebugKey = "Studio.Semantic.Log.Debug";
        public const string SemanticLogInfoKey = "Studio.Semantic.Log.Info";
        public const string SemanticLogWarningKey = "Studio.Semantic.Log.Warning";
        public const string SemanticLogErrorKey = "Studio.Semantic.Log.Error";
        public const string SemanticLogFatalKey = "Studio.Semantic.Log.Fatal";

        /// <summary>Все ключи-алиасы, которые обязана заполнять <see cref="Apply(ResourceDictionary, ResourceDictionary)"/>.</summary>
        public static readonly IReadOnlyList<string> AliasKeys = new[]
        {
            WindowBackgroundKey,
            MenuBackgroundKey,
            MenuForegroundKey,
            ToolbarBackgroundKey,
            ToolbarForegroundKey,
            ToolbarBorderKey,
            ToolbarHoverBackgroundKey,
            ToolbarPressedBackgroundKey,
            ToolbarCheckedBackgroundKey,
            TitleBarBackgroundKey,
            TitleBarForegroundKey,
            TitleBarButtonHoverBackgroundKey,
            SurfaceKey,
            SurfaceAltKey,
            HoverKey,
            SelectedKey,
            SeparatorKey,
            SeparatorStrongKey,
            ForegroundKey,
            ForegroundMutedKey,
            CloseHoverBackgroundKey,
            CloseHoverForegroundKey,
            OverlayFillKey,
            OverlayStrokeKey
        };

        /// <summary>
        /// Семантические кисти: кодируют состояние и тип данных, а не оформление. В отличие от
        /// <see cref="AliasKeys"/> не зависят от темы и не входят в <see cref="StudioPalette"/>
        /// (её токены обязаны быть нейтральными).
        /// </summary>
        public static readonly IReadOnlyList<string> SemanticKeys = new[]
        {
            SemanticRunKey,
            SemanticPauseKey,
            SemanticStopKey,
            SemanticSaveKey,
            SemanticFolderIconKey,
            SemanticXamlFileIconKey,
            SemanticJsonFileIconKey,
            SemanticValidationKey,
            SemanticLogDebugKey,
            SemanticLogInfoKey,
            SemanticLogWarningKey,
            SemanticLogErrorKey,
            SemanticLogFatalKey
        };

        /// <summary>Системные кисти, которыми темизируются нативные WF-контролы (TreeView/Toolbox/Outline).</summary>
        public static readonly IReadOnlyList<object> SystemBrushKeys = new object[]
        {
            SystemColors.MenuBrushKey,
            SystemColors.MenuTextBrushKey,
            SystemColors.WindowBrushKey,
            SystemColors.WindowTextBrushKey,
            SystemColors.ControlBrushKey,
            SystemColors.ControlTextBrushKey,
            SystemColors.HighlightBrushKey,
            SystemColors.HighlightTextBrushKey,
            SystemColors.InactiveSelectionHighlightBrushKey,
            SystemColors.ActiveBorderBrushKey,
            SystemColors.InactiveBorderBrushKey,
            SystemColors.GrayTextBrushKey,

            // Градации системного chrome (3D-рамки, полосы прокрутки, фон рабочей области). Шаблоны
            // Aero2 читают их вместо Control/Window — именно из-за них в тёмной теме появлялись светлые
            // плашки (заголовок категории в панели «Активности», углы ScrollViewer).
            SystemColors.ControlLightBrushKey,
            SystemColors.ControlLightLightBrushKey,
            SystemColors.ControlDarkBrushKey,
            SystemColors.ControlDarkDarkBrushKey,
            SystemColors.ScrollBarBrushKey,
            SystemColors.AppWorkspaceBrushKey,
            SystemColors.WindowFrameBrushKey,
            SystemColors.InfoBrushKey,
            SystemColors.InfoTextBrushKey
        };

        /// <summary>Заполняет алиасы в ресурсах приложения (безопасно вызывать многократно).</summary>
        public static void ApplyToApplication()
        {
            Apply(Application.Current?.Resources);
        }

        /// <summary>Заполняет алиасы в указанном словаре, беря палитру из него же.</summary>
        public static void Apply(ResourceDictionary target)
        {
            if (target == null)
            {
                return;
            }

            ApplyPalette(target, StudioPalette.Active);
        }

        /// <summary>Заполняет алиасы и системные кисти значениями конкретной палитры.</summary>
        public static void ApplyPalette(ResourceDictionary target, StudioThemeColors palette)
        {
            if (target == null || palette == null)
            {
                return;
            }

            SetAliases(target, palette);
            SetSemanticBrushes(target);
            ApplySystemBrushes(target, palette);
        }

        private static void SetAliases(ResourceDictionary target, StudioThemeColors palette)
        {
            // Правило одного фона: все «фоны» — это Surface, различие зон передаёт только Separator.
            Set(target, WindowBackgroundKey, palette.Surface);
            Set(target, MenuBackgroundKey, palette.Surface);
            Set(target, ToolbarBackgroundKey, palette.Surface);
            Set(target, TitleBarBackgroundKey, palette.Surface);

            Set(target, MenuForegroundKey, palette.Foreground);
            Set(target, ToolbarForegroundKey, palette.Foreground);
            Set(target, TitleBarForegroundKey, palette.Foreground);

            Set(target, ToolbarBorderKey, palette.Separator);

            Set(target, ToolbarHoverBackgroundKey, palette.Hover);
            Set(target, TitleBarButtonHoverBackgroundKey, palette.Hover);
            Set(target, ToolbarPressedBackgroundKey, palette.Selected);
            Set(target, ToolbarCheckedBackgroundKey, palette.Selected);

            Set(target, SurfaceKey, palette.Surface);
            Set(target, SurfaceAltKey, palette.SurfaceAlt);
            Set(target, HoverKey, palette.Hover);
            Set(target, SelectedKey, palette.Selected);
            Set(target, SeparatorKey, palette.Separator);
            Set(target, SeparatorStrongKey, palette.SeparatorStrong);
            Set(target, ForegroundKey, palette.Foreground);
            Set(target, ForegroundMutedKey, palette.ForegroundMuted);
            Set(target, CloseHoverBackgroundKey, palette.CloseHoverBackground);
            Set(target, CloseHoverForegroundKey, palette.CloseHoverForeground);
            Set(target, OverlayFillKey, palette.OverlayFill);
            Set(target, OverlayStrokeKey, palette.OverlayStroke);
        }

        private static void SetSemanticBrushes(ResourceDictionary target)
        {
            // Значения фиксированы: «запуск»/«пауза»/«стоп» и типы файлов распознаются по цвету
            // в обеих темах. Красный используется только как семантика ошибки/останова.
            Set(target, SemanticRunKey, Color.FromRgb(0x22, 0x8B, 0x22));
            Set(target, SemanticPauseKey, Color.FromRgb(0xFF, 0xA5, 0x00));
            Set(target, SemanticStopKey, Color.FromRgb(0xDC, 0x14, 0x3C));
            Set(target, SemanticSaveKey, Color.FromRgb(0x19, 0x76, 0xD2));
            Set(target, SemanticFolderIconKey, Color.FromRgb(0xFF, 0x45, 0x00));
            Set(target, SemanticXamlFileIconKey, Color.FromRgb(0x4C, 0xAF, 0x50));
            Set(target, SemanticJsonFileIconKey, Color.FromRgb(0x21, 0x96, 0xF3));

            // Бейдж проверки поля выражения: голубой (светлая гамма), а не красный. Значения фиксированы
            // и одинаковы в обеих темах — это семантика, а не оформление.
            Set(target, SemanticValidationKey, Color.FromRgb(0x29, 0xB6, 0xF6));

            // Фильтры панели «Log»: приглушённые тона уровней, согласованные с буквами уровня в строках
            // (LogLevelToColorConverter), — цвета непрозрачные, чтобы глиф читался на панели и в тёмной,
            // и в светлой теме. Trace — не цвет, а основной текст панели (Studio.Foreground): белый в тёмной,
            // чёрный в светлой, поэтому в семантике его нет.
            Set(target, SemanticLogDebugKey, Color.FromRgb(0x5B, 0x9B, 0xD5));
            Set(target, SemanticLogInfoKey, Color.FromRgb(0x4E, 0x9A, 0x45));
            Set(target, SemanticLogWarningKey, Color.FromRgb(0xC0, 0x8A, 0x00));
            Set(target, SemanticLogErrorKey, Color.FromRgb(0xD1, 0x4B, 0x4B));
            Set(target, SemanticLogFatalKey, Color.FromRgb(0xA6, 0x3A, 0x3A));
        }

        private static void ApplySystemBrushes(ResourceDictionary target, StudioThemeColors palette)
        {
            Set(target, SystemColors.MenuBrushKey, palette.Surface);
            Set(target, SystemColors.MenuTextBrushKey, palette.Foreground);
            Set(target, SystemColors.WindowBrushKey, palette.Surface);
            Set(target, SystemColors.WindowTextBrushKey, palette.Foreground);
            Set(target, SystemColors.ControlBrushKey, palette.Surface);
            Set(target, SystemColors.ControlTextBrushKey, palette.Foreground);

            // Выделение нативных контролов (TreeView/Toolbox/Outline/ListView) — нейтральное:
            // раньше здесь стоял MaterialDesign Primary, из-за чего выделение было фиолетовым.
            Set(target, SystemColors.HighlightBrushKey, palette.Selected);
            Set(target, SystemColors.HighlightTextBrushKey, palette.Foreground);
            Set(target, SystemColors.InactiveSelectionHighlightBrushKey, palette.SurfaceAlt);

            Set(target, SystemColors.ActiveBorderBrushKey, palette.SeparatorStrong);
            Set(target, SystemColors.InactiveBorderBrushKey, palette.Separator);
            Set(target, SystemColors.GrayTextBrushKey, palette.ForegroundMuted);

            // Градации chrome: светлые варианты — на поверхность, тёмные — в линии палитры. Кисти,
            // которые шаблон Aero2 рисует плашкой, обязаны быть нейтральными (см. SystemBrushKeys).
            Set(target, SystemColors.ControlLightBrushKey, palette.Surface);
            Set(target, SystemColors.ControlLightLightBrushKey, palette.Surface);
            Set(target, SystemColors.ControlDarkBrushKey, palette.Separator);
            Set(target, SystemColors.ControlDarkDarkBrushKey, palette.SeparatorStrong);
            Set(target, SystemColors.ScrollBarBrushKey, palette.Surface);
            Set(target, SystemColors.AppWorkspaceBrushKey, palette.Surface);
            Set(target, SystemColors.WindowFrameBrushKey, palette.SeparatorStrong);
            Set(target, SystemColors.InfoBrushKey, palette.SurfaceAlt);
            Set(target, SystemColors.InfoTextBrushKey, palette.Foreground);
        }

        private static void Set(ResourceDictionary target, object key, Color color)
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
