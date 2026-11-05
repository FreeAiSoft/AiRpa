using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Agent.Core.Services.Documents
{
    /// <summary>
    /// Reader <c>pdf</c> (<c>TZ-02</c>): только текстовый слой, без сторонних пакетов и лицензий.
    /// <para>
    /// Разбираются потоки содержимого (<c>stream … endstream</c>, включая <c>FlateDecode</c>) и текстовые
    /// операторы <c>Tj</c>, <c>'</c>, <c>"</c>, <c>TJ</c>. Если текста нет — это <see cref="DocumentTextStatus.NoTextLayer"/>
    /// («скан, приложите страницы изображениями»), а не пустая строка: «не удалось» никогда не выглядит как «пусто».
    /// </para>
    /// <para>
    /// Ограничение названо честно: PDF со шрифтами Identity-H (кириллица «картинкой из глифов») текстового слоя
    /// в читаемом виде не даёт — такие страницы распознаются как изображения (vision).
    /// </para>
    /// </summary>
    public sealed class PdfTextReader : IDocumentTextReader
    {
        private static readonly string[] SupportedExtensions = { ".pdf" };

        public IReadOnlyList<string> Extensions => SupportedExtensions;

        public DocumentTextResult Read(string path, DocumentTextReadOptions options)
        {
            var settings = options ?? DocumentTextReadOptions.Default;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string raw = ToLatin1(bytes);

                int pageCount = CountPages(raw);
                var text = new StringBuilder();
                foreach (var block in EnumerateStreams(bytes, raw))
                {
                    string content = block.Flate ? TryInflate(block.Data) : block.Data;
                    if (string.IsNullOrEmpty(content))
                    {
                        continue;
                    }

                    string extracted = ExtractText(content);
                    if (extracted.Length == 0)
                    {
                        continue;
                    }

                    if (text.Length > 0)
                    {
                        text.AppendLine();
                    }

                    text.Append(extracted);
                }

                string result = text.ToString().Trim();
                if (result.Length == 0)
                {
                    return DocumentTextResult.NoTextLayer("pdf",
                        "текстового слоя нет (скан или шрифт Identity-H) — приложите страницы изображениями");
                }

                return DocumentTextResult.Success("pdf")
                    .WithCounts(Math.Max(pageCount, 1), 0, 0)
                    .WithText(result, settings);
            }
            catch (Exception ex)
            {
                return DocumentTextResult.Failure("pdf", "документ не прочитан: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Потоки
        // ------------------------------------------------------------------

        private struct StreamBlock
        {
            public string Data;
            public bool Flate;
        }

        private static IEnumerable<StreamBlock> EnumerateStreams(byte[] bytes, string raw)
        {
            int position = 0;
            while (position < raw.Length)
            {
                int start = raw.IndexOf("stream", position, StringComparison.Ordinal);
                if (start < 0)
                {
                    yield break;
                }

                // «endstream» тоже содержит «stream» — пропускаем его.
                if (start >= 3 && string.CompareOrdinal(raw, start - 3, "end", 0, 3) == 0)
                {
                    position = start + 6;
                    continue;
                }

                int dataStart = start + 6;
                if (dataStart < raw.Length && raw[dataStart] == '\r') dataStart++;
                if (dataStart < raw.Length && raw[dataStart] == '\n') dataStart++;

                int end = raw.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                if (end < 0)
                {
                    yield break;
                }

                int dataEnd = end;
                while (dataEnd > dataStart && (raw[dataEnd - 1] == '\r' || raw[dataEnd - 1] == '\n'))
                {
                    dataEnd--;
                }

                if (dataEnd <= dataStart || dataStart >= bytes.Length)
                {
                    position = end + 9;
                    continue;
                }

                // Словарь потока — в пределах 400 символов перед «stream»: ищем /FlateDecode.
                int dictionaryStart = Math.Max(0, start - 400);
                string dictionary = raw.Substring(dictionaryStart, start - dictionaryStart);

                yield return new StreamBlock
                {
                    Data = ToLatin1(bytes, dataStart, dataEnd - dataStart),
                    Flate = dictionary.IndexOf("FlateDecode", StringComparison.Ordinal) >= 0
                };

                position = end + 9;
            }
        }

        private static string TryInflate(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return string.Empty;
            }

            byte[] compressed = FromLatin1(data);
            // zlib: 2 байта заголовка (RFC 1950) перед deflate-потоком; пробуем оба варианта.
            foreach (int skip in new[] { 2, 0 })
            {
                if (compressed.Length <= skip + 1)
                {
                    continue;
                }

                try
                {
                    using (var input = new MemoryStream(compressed, skip, compressed.Length - skip))
                    using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        int read;
                        while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                        }

                        return ToLatin1(output.ToArray());
                    }
                }
                catch (Exception)
                {
                    // Пробуем следующий вариант смещения; частичный результат недоступен — поток битый.
                }
            }

            return string.Empty;
        }

        private static int CountPages(string raw)
        {
            int count = 0;
            int position = 0;
            while (true)
            {
                int index = raw.IndexOf("/Type", position, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                int pageIndex = raw.IndexOf("/Page", index, StringComparison.Ordinal);
                if (pageIndex < 0)
                {
                    break;
                }

                bool isPages = pageIndex + 5 < raw.Length && raw[pageIndex + 5] == 's';
                if (!isPages && pageIndex - index < 12)
                {
                    count++;
                }

                position = pageIndex + 5;
            }

            return count;
        }

        // ------------------------------------------------------------------
        // Текстовые операторы
        // ------------------------------------------------------------------

        /// <summary>Извлекает текст из потока содержимого страницы.</summary>
        internal static string ExtractText(string content)
        {
            var text = new StringBuilder();
            var pending = new StringBuilder();
            int index = 0;

            while (index < content.Length)
            {
                char symbol = content[index];

                if (symbol == '(')
                {
                    index = ReadLiteralString(content, index, pending);
                    continue;
                }

                if (symbol == '<' && index + 1 < content.Length && content[index + 1] == '<')
                {
                    // Словарь внутри потока содержимого (MarkInfo, BDC и т. п.) — не текст.
                    index += 2;
                    continue;
                }

                if (symbol == '<')
                {
                    index = ReadHexString(content, index, pending);
                    continue;
                }

                if (char.IsLetter(symbol) || symbol == '"' || symbol == '\'')
                {
                    int start = index;
                    while (index < content.Length &&
                           (char.IsLetter(content[index]) || content[index] == '*' || content[index] == '"' || content[index] == '\''))
                    {
                        index++;
                    }

                    string token = content.Substring(start, index - start);
                    switch (token)
                    {
                        case "Tj":
                            Flush(pending, text);
                            break;
                        case "TJ":
                            // Массив строк: элементы идут подряд, между ними кернинг — без пробела.
                            if (pending.Length > 0)
                            {
                                text.Append(pending);
                                pending.Clear();
                            }
                            break;
                        case "'":
                        case "\"":
                            Flush(pending, text);
                            text.AppendLine();
                            break;
                        case "Td":
                        case "TD":
                        case "T*":
                        case "ET":
                            if (pending.Length > 0)
                            {
                                Flush(pending, text);
                            }
                            text.AppendLine();
                            break;
                    }

                    continue;
                }

                index++;
            }

            Flush(pending, text);

            // Схлопываем пустые строки: в отчёте агент читает абзацы, а не «лестницу» из переводов строк.
            var lines = text.ToString().Replace("\r", string.Empty).Split('\n');
            var result = new StringBuilder();
            bool previousBlank = true;
            foreach (string line in lines)
            {
                string trimmed = line.TrimEnd();
                if (trimmed.Length == 0)
                {
                    if (previousBlank)
                    {
                        continue;
                    }

                    previousBlank = true;
                    result.AppendLine();
                    continue;
                }

                previousBlank = false;
                result.AppendLine(trimmed);
            }

            return result.ToString().Trim();
        }

        private static void Flush(StringBuilder pending, StringBuilder text)
        {
            if (pending.Length == 0)
            {
                return;
            }

            text.Append(pending);
            pending.Clear();
        }

        private static int ReadLiteralString(string content, int index, StringBuilder target)
        {
            var bytes = new List<byte>();
            int depth = 0;
            index++; // '('

            while (index < content.Length)
            {
                char symbol = content[index];

                if (symbol == '\\' && index + 1 < content.Length)
                {
                    char next = content[index + 1];
                    switch (next)
                    {
                        case 'n': bytes.Add((byte)'\n'); index += 2; continue;
                        case 'r': bytes.Add((byte)'\r'); index += 2; continue;
                        case 't': bytes.Add((byte)'\t'); index += 2; continue;
                        case 'b': bytes.Add((byte)'\b'); index += 2; continue;
                        case 'f': bytes.Add((byte)'\f'); index += 2; continue;
                        case '(': bytes.Add((byte)'('); index += 2; continue;
                        case ')': bytes.Add((byte)')'); index += 2; continue;
                        case '\\': bytes.Add((byte)'\\'); index += 2; continue;
                        default:
                            if (next >= '0' && next <= '7')
                            {
                                int value = 0;
                                int count = 0;
                                while (count < 3 && index + 1 < content.Length &&
                                       content[index + 1] >= '0' && content[index + 1] <= '7')
                                {
                                    value = value * 8 + (content[index + 1] - '0');
                                    index++;
                                    count++;
                                }

                                bytes.Add((byte)(value & 0xFF));
                                index++;
                                continue;
                            }

                            bytes.Add((byte)next);
                            index += 2;
                            continue;
                    }
                }

                if (symbol == '(')
                {
                    depth++;
                    bytes.Add((byte)symbol);
                    index++;
                    continue;
                }

                if (symbol == ')')
                {
                    if (depth == 0)
                    {
                        index++;
                        break;
                    }

                    depth--;
                    bytes.Add((byte)symbol);
                    index++;
                    continue;
                }

                bytes.Add((byte)(symbol & 0xFF));
                index++;
            }

            target.Append(DecodeStringBytes(bytes));
            return index;
        }

        private static int ReadHexString(string content, int index, StringBuilder target)
        {
            index++; // '<'
            var digits = new StringBuilder();
            while (index < content.Length && content[index] != '>')
            {
                char symbol = content[index];
                if (Uri.IsHexDigit(symbol))
                {
                    digits.Append(symbol);
                }

                index++;
            }

            if (index < content.Length)
            {
                index++; // '>'
            }

            if (digits.Length % 2 == 1)
            {
                digits.Append('0');
            }

            var bytes = new List<byte>(digits.Length / 2);
            for (int i = 0; i + 1 < digits.Length; i += 2)
            {
                bytes.Add(Convert.ToByte(digits.ToString(i, 2), 16));
            }

            target.Append(DecodeStringBytes(bytes));
            return index;
        }

        private static string DecodeStringBytes(IReadOnlyList<byte> bytes)
        {
            if (bytes == null || bytes.Count == 0)
            {
                return string.Empty;
            }

            // UTF-16BE с BOM (FE FF) — так пишут PDF-генераторы для нелатинского текста.
            if (bytes.Count >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                var buffer = new byte[bytes.Count - 2];
                for (int i = 2; i < bytes.Count; i++)
                {
                    buffer[i - 2] = bytes[i];
                }

                return Encoding.BigEndianUnicode.GetString(buffer);
            }

            var raw = new byte[bytes.Count];
            for (int i = 0; i < bytes.Count; i++)
            {
                raw[i] = bytes[i];
            }

            try
            {
                return Encoding.GetEncoding(1251).GetString(raw);
            }
            catch (Exception)
            {
                return ToLatin1(raw);
            }
        }

        // ------------------------------------------------------------------
        // Latin-1 ↔ байты (один байт — один символ, без потерь)
        // ------------------------------------------------------------------

        private static string ToLatin1(byte[] bytes)
        {
            return ToLatin1(bytes, 0, bytes?.Length ?? 0);
        }

        private static string ToLatin1(byte[] bytes, int offset, int count)
        {
            if (bytes == null || count <= 0)
            {
                return string.Empty;
            }

            var chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                chars[i] = (char)bytes[offset + i];
            }

            return new string(chars);
        }

        private static byte[] FromLatin1(string text)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                bytes[i] = (byte)(text[i] & 0xFF);
            }

            return bytes;
        }
    }
}
