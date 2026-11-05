using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Agent.Core.Services.Documents
{
    /// <summary>
    /// Текстовые форматы (<c>TZ-02</c>): <c>md</c>, <c>txt</c>, <c>csv</c>, <c>json</c>, <c>xaml</c>, код.
    /// Кодировка определяется явно: BOM → строгий UTF-8 → <c>cp1251</c> → Latin-1. Раньше файл читался
    /// «как получится», и русское ТЗ в <c>cp1251</c> приходило в чат мусором.
    /// </summary>
    public sealed class PlainTextReader : IDocumentTextReader
    {
        private static readonly string[] SupportedExtensions =
        {
            ".md", ".markdown", ".txt", ".text", ".csv", ".tsv", ".json", ".xml", ".xaml", ".yml", ".yaml",
            ".ini", ".log", ".cs", ".vb", ".ps1", ".sql", ".html", ".htm"
        };

        public IReadOnlyList<string> Extensions => SupportedExtensions;

        public DocumentTextResult Read(string path, DocumentTextReadOptions options)
        {
            string format = DocumentTextReaders.FormatOf(path);
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string encodingName;
                string text = Decode(bytes, out encodingName);
                var result = DocumentTextResult.Success(format).WithText(text, options);
                if (!string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase))
                {
                    result.WithReason("кодировка: " + encodingName);
                }

                return result;
            }
            catch (Exception ex)
            {
                return DocumentTextResult.Failure(format, "текст не прочитан: " + ex.Message);
            }
        }

        /// <summary>
        /// Декодирует байты: BOM (UTF-8/UTF-16), затем строгий UTF-8, затем <c>cp1251</c>, затем Latin-1.
        /// Возвращает имя выбранной кодировки — оно попадает в ответ инструмента.
        /// </summary>
        public static string Decode(byte[] bytes, out string encodingName)
        {
            bytes = bytes ?? new byte[0];

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encodingName = "utf-8 (bom)";
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encodingName = "utf-16le (bom)";
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encodingName = "utf-16be (bom)";
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            var strictUtf8 = new UTF8Encoding(false, true);
            try
            {
                encodingName = "utf-8";
                return strictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Не UTF-8 — значит вероятнее всего русская однобайтовая кодировка.
            }

            foreach (int codePage in new[] { 1251, 1252 })
            {
                try
                {
                    var encoding = Encoding.GetEncoding(codePage);
                    encodingName = "cp" + codePage;
                    return encoding.GetString(bytes);
                }
                catch (Exception)
                {
                    // Провайдер кодовых страниц не зарегистрирован — пробуем следующую.
                }
            }

            encodingName = "latin-1";
            try
            {
                return Encoding.GetEncoding(28591).GetString(bytes);
            }
            catch (Exception)
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }
}
