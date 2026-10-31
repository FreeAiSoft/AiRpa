using System;
using System.Windows;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Именованный набор цветов, извлекаемый из MaterialDesign-темы для стилизации
    /// нативных WF-контролов обходом визуального дерева.
    /// </summary>
    public sealed class MaterialThemeColors
    {
        public Color Background { get; set; }
        public Color Surface { get; set; }
        public Color Foreground { get; set; }
        public Color Border { get; set; }
        public Color Hover { get; set; }
        public Color Selected { get; set; }
        public Color Accent { get; set; }
        public Color ToolBarBackground { get; set; }
        public Color ToolBarForeground { get; set; }
    }

    /// <summary>
    /// Адаптер палитры MaterialDesign.Themes.Wpf: разрешает кисти MaterialDesign в
    /// конкретные цвета и предоставляет чистые (тестируемые) операции над цветом.
    /// </summary>
    public static class MaterialThemePalette
    {
        private static readonly Color FallbackBackground = Color.FromRgb(255, 255, 255);
        private static readonly Color FallbackForeground = Color.FromRgb(30, 30, 30);
        private static readonly Color FallbackBorder = Color.FromRgb(221, 221, 221);
        private static readonly Color FallbackAccent = Color.FromRgb(103, 58, 183); // DeepPurple

        public static MaterialThemeColors Resolve(ResourceDictionary resources)
        {
            Color background = GetBrushColor(resources, "MaterialDesign.Brush.Background", FallbackBackground);
            Color foreground = GetBrushColor(resources, "MaterialDesign.Brush.Foreground", FallbackForeground);
            Color surface = GetBrushColor(resources, new[] { "MaterialDesign.Brush.Card.Background", "MaterialDesignPaper" }, background);
            Color border = GetBrushColor(resources, new[] { "MaterialDesign.Brush.Card.Border", "MaterialDesignDivider" }, FallbackBorder);
            Color accent = GetBrushColor(resources, new[] { "MaterialDesign.Brush.Primary", "MaterialDesign.Brush.Primary.Foreground" }, FallbackAccent);
            Color hover = GetBrushColor(resources, new[] { "MaterialDesign.Brush.TextBox.HoverBackground", "MaterialDesign.Brush.ToolBar.Item.Background" }, surface);
            Color selected = GetBrushColor(resources, "MaterialDesign.Brush.ListBoxItem.Selected", accent);
            Color toolbarBackground = GetBrushColor(resources, "MaterialDesign.Brush.ToolBar.Background", background);
            Color toolbarForeground = GetBrushColor(resources, "MaterialDesign.Brush.ToolBar.Item.Foreground", foreground);

            return new MaterialThemeColors
            {
                Background = background,
                Surface = surface,
                Foreground = foreground,
                Border = border,
                Hover = hover,
                Selected = selected,
                Accent = accent,
                ToolBarBackground = toolbarBackground,
                ToolBarForeground = toolbarForeground
            };
        }

        public static Color Lighten(Color color, double amount)
        {
            double t = Clamp01(amount);
            return Color.FromRgb(
                (byte)Math.Round(color.R + ((255 - color.R) * t)),
                (byte)Math.Round(color.G + ((255 - color.G) * t)),
                (byte)Math.Round(color.B + ((255 - color.B) * t)));
        }

        public static Color Darken(Color color, double amount)
        {
            double t = Clamp01(amount);
            return Color.FromRgb(
                (byte)Math.Round(color.R * (1 - t)),
                (byte)Math.Round(color.G * (1 - t)),
                (byte)Math.Round(color.B * (1 - t)));
        }

        public static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        public static bool TryParseHex(string hex, out Color color)
        {
            color = default;

            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            string value = hex.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.Length == 3)
            {
                value = new string(new[] { value[0], value[0], value[1], value[1], value[2], value[2] });
            }
            else if (value.Length == 8)
            {
                value = value.Substring(2); // отбрасываем альфа-канал
            }

            if (value.Length != 6)
            {
                return false;
            }

            if (!TryParseHexByte(value.Substring(0, 2), out byte r) ||
                !TryParseHexByte(value.Substring(2, 2), out byte g) ||
                !TryParseHexByte(value.Substring(4, 2), out byte b))
            {
                return false;
            }

            color = Color.FromRgb(r, g, b);
            return true;
        }

        private static Color GetBrushColor(ResourceDictionary resources, string key, Color fallback)
        {
            return GetBrushColor(resources, new[] { key }, fallback);
        }

        /// <summary>
        /// Пытается получить цвет первой найденной кисти по списку ключей.
        /// </summary>
        public static bool TryGetBrushColor(ResourceDictionary resources, out Color color, params string[] keys)
        {
            color = default;

            if (resources == null || keys == null)
            {
                return false;
            }

            foreach (string key in keys)
            {
                if (key != null && resources[key] is SolidColorBrush brush)
                {
                    color = brush.Color;
                    return true;
                }
            }

            return false;
        }

        private static Color GetBrushColor(ResourceDictionary resources, string[] keys, Color fallback)
        {
            return TryGetBrushColor(resources, out Color color, keys) ? color : fallback;
        }

        private static double Clamp01(double value)
        {
            return value < 0 ? 0 : value > 1 ? 1 : value;
        }

        private static bool TryParseHexByte(string value, out byte result)
        {
            result = 0;
            try
            {
                result = Convert.ToByte(value, 16);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}