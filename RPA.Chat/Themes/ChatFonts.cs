using System.Windows;
using System.Windows.Media;

namespace RPA.Chat.Themes
{
    /// <summary>
    /// Шрифты чата (этап 12: Roboto для всего проекта).
    /// <para>
    /// Сборка чата не ссылается на <c>RPA.Helpers</c>, поэтому значения приходят строковыми ключами:
    /// их пишет студия (<c>Helpers\StudioFonts.cs</c>) в <c>Application.Resources</c>. Без ресурсов
    /// студии используется установленный Roboto, иначе WPF подставит системный шрифт.
    /// </para>
    /// </summary>
    public static class ChatFonts
    {
        /// <summary>Ключ шрифта интерфейса (пишет студия).</summary>
        public const string UiFontKey = "Studio.FontFamily";

        /// <summary>Ключ шрифта кода (пишет студия).</summary>
        public const string CodeFontKey = "Studio.CodeFontFamily";

        private static readonly FontFamily Fallback = new FontFamily("Roboto");

        /// <summary>Шрифт интерфейса чата.</summary>
        public static FontFamily Ui => Resolve(UiFontKey);

        /// <summary>Шрифт блоков кода в сообщениях.</summary>
        public static FontFamily Code => Resolve(CodeFontKey);

        private static FontFamily Resolve(string key)
        {
            try
            {
                return Application.Current?.TryFindResource(key) as FontFamily ?? Fallback;
            }
            catch (System.InvalidOperationException)
            {
                return Fallback;
            }
        }
    }
}
