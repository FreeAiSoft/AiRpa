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
    /// <c>*курсив*</c>, <c>`код`</c>, ссылки <c>[текст](url)</c> и картинки <c>![alt](url)</c>
    /// (показывается alt-текст).
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
        private static readonly Regex ImagePattern = new Regex(@"!\[([^\]]*)\]\(([^)]*)\)", RegexOptions.Compiled);
        private static readonly Regex LinkPattern = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex BoldPattern = new Regex(@"(\*\*|__)(.+?)\1", RegexOptions.Compiled);
        private static readonly Regex ItalicPattern = new Regex(@"(?<![\*_])([\*_])(?!\1)(.+?)\1(?![\*_])", RegexOptions.Compiled);
        private static readonly Regex CodePattern = new Regex(@"`([^`]+)`", RegexOptions.Compiled);

        /// <summary>Инлайн-шаблоны в порядке приоритета: код → картинка → ссылка → жирный → курсив.</summary>
        private static readonly Regex[] InlinePatterns = { CodePattern, ImagePattern, LinkPattern, BoldPattern, ItalicPattern };

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
                    document.Blocks.Add(CreateHeading(heading.Groups[1].Value.Length, heading.Groups[2].Value.Trim()));
                    continue;
                }

                if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    FlushParagraph(document, paragraph);
                    document.Blocks.Add(CreateQuote(line.TrimStart().TrimStart('>')));
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
                    document.Blocks.Add(CreateListItem(bullet.Groups[1].Value, ordered: false));
                    continue;
                }

                Match ordered = OrderedListPattern.Match(line);
                if (ordered.Success)
                {
                    FlushParagraph(document, paragraph);
                    document.Blocks.Add(CreateListItem(ordered.Groups[1].Value, ordered: true));
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

            for (int i = 0; i < paragraph.Count; i++)
            {
                if (i > 0)
                    block.Inlines.Add(new LineBreak());

                AddInline(block, paragraph[i]);
            }

            document.Blocks.Add(block);
            paragraph.Clear();
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

            AddInline(heading, text);
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

            AddInline(quote, text);
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
            AddInline(item, text);
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

        /// <summary>Разбирает строку на инлайны: код, картинки, ссылки, жирный, курсив.</summary>
        private static void AddInline(Paragraph target, string text)
        {
            int position = 0;
            while (position < text.Length)
            {
                Match best = null;
                foreach (Regex pattern in InlinePatterns)
                {
                    // Regex.Match(input, startat) не сдвигает индексы — смещение учитывается само.
                    Match candidate = pattern.Match(text, position);
                    if (candidate.Success && (best == null || candidate.Index < best.Index))
                        best = candidate;
                }

                if (best == null)
                {
                    target.Inlines.Add(new Run(Unescape(text.Substring(position))));
                    return;
                }

                if (best.Index > position)
                    target.Inlines.Add(new Run(Unescape(text.Substring(position, best.Index - position))));

                AppendMatched(target, best);
                position = best.Index + best.Length;
            }
        }

        private static void AppendMatched(Paragraph target, Match match)
        {
            string value = match.Value;

            if (value.Length > 1 && value.StartsWith("`", StringComparison.Ordinal) && value.EndsWith("`", StringComparison.Ordinal))
            {
                target.Inlines.Add(new Run(value.Trim('`'))
                {
                    FontFamily = NuGetThemeResources.CodeFont(),
                    Background = NuGetThemeResources.Brush(NuGetThemeResources.SurfaceAltKey)
                });
                return;
            }

            Match image = ImagePattern.Match(value);
            if (image.Success && image.Index == 0 && image.Length == value.Length)
            {
                // Картинки не загружаем (README ссылается на относительные пути) — показываем alt.
                target.Inlines.Add(new Run("[image: " + image.Groups[1].Value + "]")
                {
                    Foreground = NuGetThemeResources.Brush(NuGetThemeResources.ForegroundMutedKey),
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            Match link = LinkPattern.Match(value);
            if (link.Success && link.Index == 0 && link.Length == value.Length)
            {
                target.Inlines.Add(new Hyperlink(new Run(link.Groups[1].Value))
                {
                    NavigateUri = TryCreateUri(link.Groups[2].Value),
                    Foreground = NuGetThemeResources.Brush(NuGetThemeResources.ForegroundKey)
                });
                return;
            }

            Match bold = BoldPattern.Match(value);
            if (bold.Success && bold.Index == 0 && bold.Length == value.Length)
            {
                target.Inlines.Add(new Bold(new Run(bold.Groups[2].Value)));
                return;
            }

            Match italic = ItalicPattern.Match(value);
            if (italic.Success && italic.Index == 0 && italic.Length == value.Length)
            {
                target.Inlines.Add(new Italic(new Run(italic.Groups[2].Value)));
                return;
            }

            target.Inlines.Add(new Run(Unescape(value)));
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
