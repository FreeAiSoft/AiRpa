using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace RPA.NuGet.Converters
{
    /// <summary>
    /// Превращает README пакета (Markdown) в <see cref="FlowDocument"/>: панель деталей показывает
    /// оформленный текст, а не «сырой» md.
    /// <para>
    /// Новых зависимостей нет — поддерживается ровно то, что встречается в README пакетов:
    /// заголовки <c>#</c>, абзацы, списки <c>-</c>/<c>*</c>/<c>1.</c>, цитаты <c>&gt;</c>,
    /// горизонтальные линии, ограждённые блоки <c>```</c>, инлайн <c>**жирный**</c>,
    /// <c>*курсив*</c>, <c>`код`</c> и ссылки <c>[текст](url)</c>. Картинки <c>![alt](url)</c>
    /// скрываются полностью (включая бейджи — ссылки, внутри которых только картинка).
    /// </para>
    /// <para>
    /// Отложено (на потом): загрузка настоящих картинок по абсолютным URL (бейджи shields.io,
    /// nuget.org) с кешем по образцу <c>NuGetIconService</c>; для относительных путей — извлечение
    /// файлов картинок из <c>.nupkg</c> в <c>NuGetPackageDetailsService</c> и показ через
    /// <c>BlockUIContainer</c>/<c>InlineUIContainer</c>.
    /// </para>
    /// <para>
    /// Цвета и шрифты берутся из палитры студии через <see cref="NuGetThemeResources"/>: в тёмной и
    /// светлой теме документ перекрашивается сам, потому что кисти — динамические ресурсы.
    /// </para>
    /// </summary>
    public static class MarkdownRenderer
    {
        private const double BodyFontSize = 12d;

        private static readonly Regex OrderedListPattern = new Regex(@"^\s*\d+[.)]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex BulletListPattern = new Regex(@"^\s*[-*+]\s+(.*)$", RegexOptions.Compiled);
        private static readonly Regex HeadingPattern = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);

        /// <summary>Собирает документ из Markdown; <c>null</c>/пусто — пустой документ.</summary>
        public static FlowDocument Render(string markdown)
        {
            var document = new FlowDocument
            {
                FontFamily = NuGetThemeResources.Font(),
                FontSize = BodyFontSize,
                Foreground = NuGetThemeResources.Brush(NuGetThemeResources.ForegroundKey),
                Background = Brushes.Transparent,
                PagePadding = new Thickness(0),
                ColumnWidth = double.PositiveInfinity
            };

            if (string.IsNullOrWhiteSpace(markdown))
                return document;

            string[] lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var paragraph = new List<string>();
            bool inCodeBlock;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];

                // Ограждённый блок кода: всё внутри — как есть, без разбора Markdown.
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    FlushParagraph(document, paragraph);
                    int end = index + 1;
                    var code = new List<string>();
                    while (end < lines.Length && !lines[end].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        code.Add(lines[end]);
                        end++;
                    }

                    document.Blocks.Add(CreateCodeBlock(string.Join("\n", code)));
                    index = end;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph(document, paragraph);
                    continue;
                }

                Match heading = HeadingPattern.Match(line);
                if (heading.Success)
                {
                    FlushParagraph(document, paragraph);
                    AddBlockIfNotEmpty(document, CreateHeading(heading.Groups[1].Value.Length, heading.Groups[2].Value.Trim()));
                    continue;
                }

                if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    FlushParagraph(document, paragraph);
                    AddBlockIfNotEmpty(document, CreateQuote(line.TrimStart().TrimStart('>')));
                    continue;
                }

                if (IsHorizontalRule(line))
                {
                    FlushParagraph(document, paragraph);
                    document.Blocks.Add(CreateSeparator());
                    continue;
                }

                Match bullet = BulletListPattern.Match(line);
                if (bullet.Success)
                {
                    FlushParagraph(document, paragraph);
                    AddBlockIfNotEmpty(document, CreateListItem(bullet.Groups[1].Value, ordered: false));
                    continue;
                }

                Match ordered = OrderedListPattern.Match(line);
                if (ordered.Success)
                {
                    FlushParagraph(document, paragraph);
                    AddBlockIfNotEmpty(document, CreateListItem(ordered.Groups[1].Value, ordered: true));
                    continue;
                }

                paragraph.Add(line.Trim());
            }

            FlushParagraph(document, paragraph);
            return document;
        }

        private static bool IsHorizontalRule(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 3)
                return false;

            foreach (char symbol in new[] { '-', '*', '_' })
            {
                bool onlySymbol = true;
                foreach (char character in trimmed)
                {
                    if (character != symbol)
                    {
                        onlySymbol = false;
                        break;
                    }
                }

                if (onlySymbol)
                    return true;
            }

            return false;
        }

        private static void FlushParagraph(FlowDocument document, List<string> paragraph)
        {
            if (paragraph.Count == 0)
                return;

            var block = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 8),
                LineHeight = 18
            };

            bool hasContent = false;
            for (int i = 0; i < paragraph.Count; i++)
            {
                if (hasContent)
                    block.Inlines.Add(new LineBreak());

                if (!AddInlines(block.Inlines, paragraph[i]))
                {
                    if (hasContent)
                        block.Inlines.Remove(block.Inlines.LastInline);

                    continue;
                }

                hasContent = true;
            }

            paragraph.Clear();
            TrimBoundaryWhitespace(block.Inlines);

            if (hasContent)
                document.Blocks.Add(block);
        }

        private static void AddBlockIfNotEmpty(FlowDocument document, Block block)
        {
            if (block == null)
                return;

            if (block is Paragraph paragraph && paragraph.Inlines.Count == 0)
                return;

            document.Blocks.Add(block);
        }

        private static Paragraph CreateHeading(int level, string text)
        {
            // Размеры как в Visual Studio: h1 20, h2 17, h3 15, дальше 13.
            double[] sizes = { 20, 17, 15, 13, 13, 13 };
            var heading = new Paragraph
            {
                FontSize = sizes[Math.Min(level, sizes.Length) - 1],
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, level <= 2 ? 10 : 6, 0, 6),
                Foreground = NuGetThemeResources.Brush(NuGetThemeResources.ForegroundKey)
            };

            if (!AddInlines(heading.Inlines, text))
                return null;

            TrimBoundaryWhitespace(heading.Inlines);
            return heading;
        }

        private static Paragraph CreateQuote(string text)
        {
            var quote = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 4, 0, 4),
                BorderBrush = NuGetThemeResources.Brush(NuGetThemeResources.SeparatorStrongKey),
                BorderThickness = new Thickness(2, 0, 0, 0),
                Foreground = NuGetThemeResources.Brush(NuGetThemeResources.ForegroundMutedKey)
            };

            if (!AddInlines(quote.Inlines, text))
                return null;

            TrimBoundaryWhitespace(quote.Inlines);
            return quote;
        }

        private static Paragraph CreateListItem(string text, bool ordered)
        {
            var item = new Paragraph
            {
                Margin = new Thickness(ordered ? 6 : 12, 0, 0, 3)
            };

            item.Inlines.Add(new Run(ordered ? "•" : "•") { Foreground = NuGetThemeResources.Brush(NuGetThemeResources.ForegroundMutedKey) });
            item.Inlines.Add(new Run(" ") { });

            if (!AddInlines(item.Inlines, text))
                return null;

            TrimBoundaryWhitespace(item.Inlines);
            return item;
        }

        private static Section CreateCodeBlock(string code)
        {
            var section = new Section
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 6, 8, 6),
                Background = NuGetThemeResources.Brush(NuGetThemeResources.SurfaceAltKey),
                BorderBrush = NuGetThemeResources.Brush(NuGetThemeResources.SeparatorKey),
                BorderThickness = new Thickness(1),
                FontFamily = NuGetThemeResources.CodeFont()
            };

            section.Blocks.Add(new Paragraph(new Run(code))
            {
                Margin = new Thickness(0),
                FontFamily = NuGetThemeResources.CodeFont(),
                FontSize = 11.5
            });

            return section;
        }

        private static Block CreateSeparator()
        {
            return new Paragraph
            {
                Margin = new Thickness(0, 4, 0, 8),
                BorderBrush = NuGetThemeResources.Brush(NuGetThemeResources.SeparatorKey),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
        }

        /// <summary>Разбирает строку в инлайны: код, ссылки (рекурсивно), жирный, курсив; картинки скрываются.</summary>
        private static bool AddInlines(InlineCollection target, string text)
        {
            int before = target.Count;
            int position = 0;
            while (position < text.Length)
            {
                char character = text[position];

                if (character == '`' && TryParseCode(text, position, out int codeEnd, out string code))
                {
                    target.Add(new Run(code)
                    {
                        FontFamily = NuGetThemeResources.CodeFont(),
                        Background = NuGetThemeResources.Brush(NuGetThemeResources.SurfaceAltKey)
                    });
                    position = codeEnd;
                    continue;
                }

                if (character == '!' && position + 1 < text.Length && text[position + 1] == '['
                    && TryParseImage(text, position, out int imageEnd))
                {
                    // Изображения полностью скрываются: ни alt-текста, ни сырого markdown.
                    position = imageEnd;
                    continue;
                }

                if (character == '[' && TryParseLink(text, position, out int linkEnd, out string linkText, out string url))
                {
                    var hyperlink = new Hyperlink
                    {
                        NavigateUri = TryCreateUri(url),
                        Foreground = NuGetThemeResources.Brush(NuGetThemeResources.ForegroundKey)
                    };

                    AddInlines(hyperlink.Inlines, linkText);
                    TrimBoundaryWhitespace(hyperlink.Inlines);

                    // Бейдж — ссылка, внутри которой только картинка, — целиком скрывается.
                    if (hyperlink.Inlines.Count > 0)
                        target.Add(hyperlink);

                    position = linkEnd;
                    continue;
                }

                if ((character == '*' || character == '_')
                    && TryParseEmphasis(text, position, out int emphasisEnd, out string emphasis, out bool bold))
                {
                    Inline inline = bold
                        ? (Inline)new Bold(new Run(Unescape(emphasis)))
                        : new Italic(new Run(Unescape(emphasis)));
                    target.Add(inline);
                    position = emphasisEnd;
                    continue;
                }

                int nextSpecial = FindNextSpecial(text, position);
                if (nextSpecial < 0)
                {
                    target.Add(new Run(Unescape(text.Substring(position))));
                    return target.Count > before;
                }

                if (nextSpecial > position)
                {
                    target.Add(new Run(Unescape(text.Substring(position, nextSpecial - position))));
                    position = nextSpecial;
                    continue;
                }

                // Спецсимвол не удалось разобрать (например, незакрытая скобка) — выводим как есть.
                target.Add(new Run(Unescape(text.Substring(position, 1))));
                position++;
            }

            return target.Count > before;
        }

        private static bool TryParseCode(string text, int position, out int end, out string code)
        {
            end = position;
            code = null;

            int close = text.IndexOf('`', position + 1);
            if (close <= position + 1)
                return false;

            code = text.Substring(position + 1, close - position - 1);
            end = close + 1;
            return true;
        }

        private static bool TryParseImage(string text, int position, out int end)
        {
            // position указывает на '!'; дальше обязана идти конструкция '[alt](url)'.
            end = position;

            int closeBracket = text.IndexOf(']', position + 2);
            if (closeBracket < 0)
                return false;

            int openParen = closeBracket + 1;
            if (openParen >= text.Length || text[openParen] != '(')
                return false;

            int closeParen = text.IndexOf(')', openParen + 1);
            if (closeParen < 0)
                return false;

            end = closeParen + 1;
            return true;
        }

        private static bool TryParseLink(string text, int position, out int end, out string linkText, out string url)
        {
            end = position;
            linkText = null;
            url = null;

            // Находим парную ']', учитывая вложенные '[' (например, картинка внутри ссылки-бейджа).
            int depth = 1;
            int index = position + 1;
            while (index < text.Length)
            {
                if (text[index] == '[')
                {
                    depth++;
                }
                else if (text[index] == ']')
                {
                    depth--;
                    if (depth == 0)
                        break;
                }

                index++;
            }

            if (index >= text.Length)
                return false;

            linkText = text.Substring(position + 1, index - position - 1);

            int openParen = index + 1;
            if (openParen >= text.Length || text[openParen] != '(')
                return false;

            int closeParen = text.IndexOf(')', openParen + 1);
            if (closeParen < 0)
                return false;

            url = text.Substring(openParen + 1, closeParen - openParen - 1);
            end = closeParen + 1;
            return true;
        }

        private static bool TryParseEmphasis(string text, int position, out int end, out string content, out bool bold)
        {
            end = position;
            content = null;
            bold = false;

            char marker = text[position];
            bool isBold = position + 1 < text.Length && text[position + 1] == marker;
            int markerLength = isBold ? 2 : 1;

            int close = text.IndexOf(new string(marker, markerLength), position + markerLength, StringComparison.Ordinal);
            if (close <= position + markerLength)
                return false;

            content = text.Substring(position + markerLength, close - position - markerLength);
            end = close + markerLength;
            bold = isBold;
            return true;
        }

        private static int FindNextSpecial(string text, int from)
        {
            for (int index = from; index < text.Length; index++)
            {
                char character = text[index];
                if (character == '`' || character == '[' || character == '*' || character == '_')
                    return index;

                if (character == '!' && index + 1 < text.Length && text[index + 1] == '[')
                    return index;
            }

            return -1;
        }

        private static void TrimBoundaryWhitespace(InlineCollection inlines)
        {
            while (inlines.FirstInline is Run first && string.IsNullOrWhiteSpace(first.Text))
                inlines.Remove(first);

            if (inlines.FirstInline is Run leading)
            {
                string trimmed = leading.Text.TrimStart();
                if (trimmed.Length != leading.Text.Length)
                    leading.Text = trimmed;
            }

            while (inlines.LastInline is Run last && string.IsNullOrWhiteSpace(last.Text))
                inlines.Remove(last);

            if (inlines.LastInline is Run trailing)
            {
                string trimmed = trailing.Text.TrimEnd();
                if (trimmed.Length != trailing.Text.Length)
                    trailing.Text = trimmed;
            }
        }

        private static Uri TryCreateUri(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ? uri : null;
        }

        private static string Unescape(string text)
        {
            return text.Replace("\\*", "*").Replace("\\_", "_").Replace("\\`", "`");
        }
    }
}
