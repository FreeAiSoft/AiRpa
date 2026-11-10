using System;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Безопасное усечение текста, которое не разрывает суррогатные пары UTF-16.
    /// <para>
    /// Эмодзи и прочие символы вне Basic Multilingual Plane хранятся в строках .NET как пара
    /// <c>char</c> (high + low surrogate). Обычное <c>Substring(0, n)</c> может разрезать такую
    /// пару пополам и оставить в строке одиночный суррогат, который нельзя записать в UTF-8 —
    /// это роняет <c>File.WriteAllText</c> с <see cref="System.Text.EncoderFallbackException"/>.
    /// </para>
    /// </summary>
    public static class SafeText
    {
        /// <summary>Возвращает первые <paramref name="maxChars"/> символов, не разрезая суррогатную пару.</summary>
        public static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (maxChars <= 0) return string.Empty;
            if (text.Length <= maxChars) return text;

            var cut = maxChars;
            // High surrogate не может быть последним символом валидной строки — отодвигаем границу влево.
            if (char.IsHighSurrogate(text[cut - 1])) cut--;
            return text.Substring(0, cut);
        }

        /// <summary>Возвращает последние <paramref name="maxChars"/> символов, не начиная с обломка суррогатной пары.</summary>
        public static string Tail(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            if (maxChars <= 0) return string.Empty;
            if (text.Length <= maxChars) return text;

            var start = text.Length - maxChars;
            // Low surrogate не может быть первым символом — сдвигаем начало хвоста вперёд.
            if (char.IsLowSurrogate(text[start])) start++;
            return text.Substring(start);
        }
    }
}
