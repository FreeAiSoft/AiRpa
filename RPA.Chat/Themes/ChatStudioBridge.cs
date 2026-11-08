using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RPA.Chat.Themes
{
    /// <summary>
    /// Одно соответствие «ключ словаря чата → ключ палитры студии»
    /// (docs\Vs2026_Theme_Plan.md, §2 и §8 п.8, F-08).
    /// </summary>
    public sealed class ChatThemeMapping
    {
        public ChatThemeMapping(string chatKey, string studioKey, byte alpha = 0)
        {
            ChatKey = chatKey;
            StudioKey = studioKey;
            Alpha = alpha;
        }

        /// <summary>Ключ в <c>ChatTheme.Dark.xaml</c> / <c>ChatTheme.Light.xaml</c>.</summary>
        public string ChatKey { get; }

        /// <summary>Ключ-алиас студии (<c>Studio.*</c> из <c>StudioBrushes</c>).</summary>
        public string StudioKey { get; }

        /// <summary>0 — цвет берётся как есть; иначе кисть-накладка с этой альфой.</summary>
        public byte Alpha { get; }
    }

    /// <summary>
    /// Мост кистей чата: <c>RPA.Chat</c> — отдельная сборка и не может ссылаться на
    /// <c>RPA.Helpers</c>, поэтому при применении темы контрол сам перекрывает ключи своего словаря
    /// значениями <c>Studio.*</c> из ресурсов приложения (тот же приём, что
    /// <c>AvalonDock.Themes.ThemeBrushBridge</c> для окон AvalonDock).
    /// <para>
    /// Без ресурсов студии (автономный запуск контрола) мост ничего не делает, а значения в словаре
    /// совпадают с палитрой — чат выглядит одинаково в обоих случаях.
    /// </para>
    /// </summary>
    public static class ChatStudioBridge
    {
        // Имена ключей студии продублированы строками: ссылаться на RPA.Helpers из этой сборки нельзя.
        private const string StudioSurface = "Studio.Surface";
        private const string StudioSurfaceAlt = "Studio.SurfaceAlt";
        private const string StudioHover = "Studio.Hover";
        private const string StudioSelected = "Studio.Selected";
        private const string StudioSeparator = "Studio.Separator";
        private const string StudioSeparatorStrong = "Studio.SeparatorStrong";
        private const string StudioForeground = "Studio.Foreground";
        private const string StudioForegroundMuted = "Studio.ForegroundMuted";
        private const string StudioCloseHoverBackground = "Studio.CloseHoverBackground";

        private const byte OverlayBackgroundAlpha = 0x26;

        /// <summary>Таблица соответствий: единственное место, где чат знает про палитру.</summary>
        public static IReadOnlyList<ChatThemeMapping> Mappings { get; } = new[]
        {
            new ChatThemeMapping("Dark.Window", StudioSurface),
            new ChatThemeMapping("Dark.Panel", StudioSurface),
            new ChatThemeMapping("Dark.Control", StudioSurfaceAlt),
            new ChatThemeMapping("Dark.ControlHover", StudioHover),
            new ChatThemeMapping("Dark.ControlPressed", StudioSelected),
            new ChatThemeMapping("Dark.Border", StudioSeparatorStrong),
            new ChatThemeMapping("Dark.Text", StudioForeground),
            new ChatThemeMapping("Dark.SubtleText", StudioForeground),
            new ChatThemeMapping("Dark.MutedText", StudioForegroundMuted),
            new ChatThemeMapping("Dark.Accent", StudioSelected),
            new ChatThemeMapping("Dark.Primary", StudioForeground),
            // Красный — единственный разрешённый не-нейтральный токен; в чате он остаётся только семантикой ошибки.
            new ChatThemeMapping("Dark.ErrorText", StudioCloseHoverBackground),

            new ChatThemeMapping("Chat.UserBorder", StudioSeparatorStrong),
            new ChatThemeMapping("Chat.UserBackground", StudioSurfaceAlt),

            new ChatThemeMapping("Question.OptionBorder", StudioSeparatorStrong),
            new ChatThemeMapping("Question.AnswerBorder", StudioForeground),

            new ChatThemeMapping("Chat.ToolBlockedBorder", StudioCloseHoverBackground),
            new ChatThemeMapping("Chat.ToolBlockedBackground", StudioCloseHoverBackground, OverlayBackgroundAlpha),
            new ChatThemeMapping("Chat.ErrorBorder", StudioCloseHoverBackground),
            new ChatThemeMapping("Chat.ErrorBackground", StudioCloseHoverBackground, OverlayBackgroundAlpha),
            new ChatThemeMapping("Chat.CompactionBorder", StudioSeparatorStrong),
            new ChatThemeMapping("Chat.CompactionBackground", StudioSurfaceAlt),
            new ChatThemeMapping("Chat.ModeSwitchBorder", StudioSeparatorStrong),
            new ChatThemeMapping("Chat.ModeSwitchBackground", StudioSurfaceAlt),

            new ChatThemeMapping("Chat.ReasoningText", StudioForegroundMuted),
            new ChatThemeMapping("Chat.ScrollThumb", StudioSeparatorStrong),
            new ChatThemeMapping("Chat.ScrollThumbHover", StudioForegroundMuted),
            new ChatThemeMapping("Chat.ScrollThumbDragging", StudioForeground),
            new ChatThemeMapping("Code.Background", StudioSurface)
        };

        /// <summary>
        /// Перекрывает кисти чата значениями из ресурсов студии. Безопасно вызывать многократно:
        /// вызывается при каждом применении темы, в том числе на лету.
        /// </summary>
        public static void Apply(ResourceDictionary target, ResourceDictionary studioSource)
        {
            if (target == null || studioSource == null)
            {
                return;
            }

            foreach (ChatThemeMapping mapping in Mappings)
            {
                if (!TryGetColor(studioSource, mapping.StudioKey, out Color color))
                {
                    continue;
                }

                if (mapping.Alpha != 0)
                {
                    color = Color.FromArgb(mapping.Alpha, color.R, color.G, color.B);
                }

                SolidColorBrush brush = new(color);
                brush.Freeze();

                target.Remove(mapping.ChatKey);
                target[mapping.ChatKey] = brush;
            }
        }

        /// <summary>Кисть из ресурсов студии либо <paramref name="fallback"/>.</summary>
        public static Brush Resolve(ResourceDictionary studioSource, string studioKey, Brush fallback)
        {
            if (studioSource != null && studioKey != null && studioSource.Contains(studioKey) &&
                studioSource[studioKey] is Brush brush)
            {
                return brush;
            }

            return fallback;
        }

        private static bool TryGetColor(ResourceDictionary source, string key, out Color color)
        {
            color = default;

            if (!source.Contains(key) || source[key] is not SolidColorBrush brush)
            {
                return false;
            }

            color = brush.Color;
            return true;
        }
    }
}
