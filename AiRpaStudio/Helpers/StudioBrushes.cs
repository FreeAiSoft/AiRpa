using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Кисти-алиасы студии (<c>Studio.*</c>) и системные кисти WPF.
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
    /// из активной MaterialDesign-палитры. Вызывать после каждой смены темы.
    /// </summary>
    public static class StudioBrushes
    {
        // Алиасы студии.
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

        private static readonly Color FallbackBackground = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color FallbackForeground = Color.FromRgb(0x1E, 0x1E, 0x1E);
        private static readonly Color FallbackToolbarBackground = Color.FromRgb(0xF0, 0xF0, 0xF0);
        private static readonly Color FallbackBorder = Color.FromRgb(0xDD, 0xDD, 0xDD);
        private static readonly Color FallbackHover = Color.FromRgb(0xE0, 0xE0, 0xE0);
        private static readonly Color FallbackSelected = Color.FromRgb(0xD6, 0xD6, 0xD6);
        private static readonly Color FallbackHighlightText = Color.FromRgb(0xFF, 0xFF, 0xFF);

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
            TitleBarButtonHoverBackgroundKey
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
            SystemColors.GrayTextBrushKey
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

            Apply(target, target);
        }

        /// <summary>
        /// Заполняет алиасы <paramref name="target"/> реальными кистями из палитры <paramref name="source"/>.
        /// Существующие значения заменяются, чтобы DynamicResource-ссылки получили уведомление
        /// и перерисовались при смене темы.
        /// </summary>
        public static void Apply(ResourceDictionary target, ResourceDictionary source)
        {
            if (target == null)
            {
                return;
            }

            Set(target, WindowBackgroundKey,
                ResolveColor(source, FallbackBackground, "MaterialDesign.Brush.Background"));
            Set(target, MenuBackgroundKey,
                ResolveColor(source, FallbackBackground, "MaterialDesign.Brush.Background"));
            Set(target, MenuForegroundKey,
                ResolveColor(source, FallbackForeground, "MaterialDesign.Brush.Foreground"));
            Set(target, ToolbarBackgroundKey,
                ResolveColor(source, FallbackToolbarBackground,
                    "MaterialDesign.Brush.ToolBar.Background", "MaterialDesign.Brush.Card.Background"));
            Set(target, ToolbarForegroundKey,
                ResolveColor(source, FallbackForeground,
                    "MaterialDesign.Brush.ToolBar.Item.Foreground", "MaterialDesign.Brush.Foreground"));
            Set(target, ToolbarBorderKey,
                ResolveColor(source, FallbackBorder,
                    "MaterialDesign.Brush.Card.Border", "MaterialDesignDivider"));
            Set(target, ToolbarHoverBackgroundKey,
                ResolveColor(source, FallbackHover,
                    "MaterialDesign.Brush.ToolBar.Item.Background", "MaterialDesign.Brush.TextBox.HoverBackground"));
            Set(target, ToolbarPressedBackgroundKey,
                ResolveColor(source, FallbackHover,
                    "MaterialDesign.Brush.ToolBar.Item.Background", "MaterialDesign.Brush.TextBox.HoverBackground"));
            Set(target, ToolbarCheckedBackgroundKey,
                ResolveColor(source, FallbackSelected,
                    "MaterialDesign.Brush.ToggleButton.Background", "MaterialDesign.Brush.ListBoxItem.Selected"));
            Set(target, TitleBarBackgroundKey,
                ResolveColor(source, FallbackBackground, "MaterialDesign.Brush.Background"));
            Set(target, TitleBarForegroundKey,
                ResolveColor(source, FallbackForeground, "MaterialDesign.Brush.Foreground"));
            Set(target, TitleBarButtonHoverBackgroundKey,
                ResolveColor(source, FallbackHover,
                    "MaterialDesign.Brush.ToolBar.Item.Background", "MaterialDesign.Brush.TextBox.HoverBackground"));

            ApplySystemBrushes(target, source);
        }

        private static void ApplySystemBrushes(ResourceDictionary target, ResourceDictionary source)
        {
            Color background = ResolveColor(source, FallbackBackground, "MaterialDesign.Brush.Background");
            Color foreground = ResolveColor(source, FallbackForeground, "MaterialDesign.Brush.Foreground");
            Color accent = ResolveColor(source, FallbackBackground, "MaterialDesign.Brush.Primary");
            Color accentForeground = ResolveColor(source, FallbackHighlightText,
                "MaterialDesign.Brush.Primary.Foreground");
            Color border = ResolveColor(source, FallbackBorder, "MaterialDesign.Brush.Card.Border", "MaterialDesignDivider");

            Set(target, SystemColors.MenuBrushKey, background);
            Set(target, SystemColors.MenuTextBrushKey, foreground);
            Set(target, SystemColors.WindowBrushKey, background);
            Set(target, SystemColors.WindowTextBrushKey, foreground);
            Set(target, SystemColors.ControlBrushKey, background);
            Set(target, SystemColors.ControlTextBrushKey, foreground);
            Set(target, SystemColors.HighlightBrushKey, accent);
            Set(target, SystemColors.HighlightTextBrushKey, accentForeground);
            Set(target, SystemColors.InactiveSelectionHighlightBrushKey, accent);
            Set(target, SystemColors.ActiveBorderBrushKey, border);
            Set(target, SystemColors.InactiveBorderBrushKey, border);
            Set(target, SystemColors.GrayTextBrushKey, foreground);
        }

        private static Color ResolveColor(ResourceDictionary source, Color fallback, params string[] keys)
        {
            return MaterialThemePalette.TryGetBrushColor(source, out Color color, keys) ? color : fallback;
        }

        private static void Set(ResourceDictionary target, object key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            // Remove + Add вместо простого присваивания: гарантирует уведомление
            // DynamicResource-ссылок при смене темы.
            target.Remove(key);
            target[key] = brush;
        }
    }
}
