using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace RPA.Helpers
{
    /// <summary>
    /// Палитра редакторов кода и XAML в схеме Visual Studio 2022 (этап 12 плана
    /// docs\Vs2026_Theme_Plan.md, п.5).
    /// <para>
    /// Отдельная от <see cref="StudioPalette"/> намеренно: токены <c>StudioPalette</c> обязаны быть
    /// нейтральными (chroma ≤ 6), а подсветке синтаксиса нужна семантика VS (ключевые слова синие,
    /// строки оранжевые, комментарии зелёные). Поэтому цвета редактора живут здесь одной таблицей:
    /// значения таблицы — единственное место, где правится подсветка.
    /// </para>
    /// <para>
    /// Токены подсветки задаются по <b>именам цветов</b> из XSHD-определений AvalonEdit
    /// (<c>Comment</c>, <c>Keyword</c>, <c>XmlTag</c>, …), а не по значениям цветов: определения
    /// перекрашиваются при загрузке (см. <see cref="StudioEditorTheme"/>), поэтому один и тот же
    /// XSHD работает и в тёмной, и в светлой теме.
    /// </para>
    /// </summary>
    public static class StudioEditorPalette
    {
        // --- Оформление самого редактора. ---
        public const string BackgroundToken = "Background";
        public const string ForegroundToken = "Foreground";
        public const string LineNumberToken = "LineNumber";
        public const string CurrentLineToken = "CurrentLine";
        public const string SelectionToken = "Selection";
        public const string SelectionForegroundToken = "SelectionForeground";

        /// <summary>
        /// Цвет гиперссылок в редакторе (<c>TextView.LinkTextForegroundBrush</c>). AvalonEdit по умолчанию
        /// берёт <c>Brushes.Blue</c> (#0000FF): на тёмном фоне это нечитаемо (контраст ≈1.9), поэтому
        /// цвет берётся из палитры (#3794FF в обеих темах).
        /// </summary>
        public const string LinkToken = "Link";

        // --- Токены подсветки синтаксиса (схема VS). ---
        public const string CommentToken = "Comment";
        public const string StringToken = "String";
        public const string KeywordToken = "Keyword";
        public const string ControlKeywordToken = "ControlKeyword";
        public const string TypeToken = "Type";
        public const string NumberToken = "Number";
        public const string MethodToken = "Method";
        public const string FieldToken = "Field";
        public const string PreprocessorToken = "Preprocessor";
        public const string XmlTagToken = "XmlTag";
        public const string AttributeNameToken = "AttributeName";
        public const string AttributeValueToken = "AttributeValue";
        public const string MarkupExtensionToken = "MarkupExtension";
        public const string EntityToken = "Entity";
        public const string CDataToken = "CData";
        public const string XmlDeclarationToken = "XmlDeclaration";

        /// <summary>Все токены палитры редактора.</summary>
        public static readonly IReadOnlyList<string> TokenNames = new[]
        {
            BackgroundToken, ForegroundToken, LineNumberToken, CurrentLineToken,
            SelectionToken, SelectionForegroundToken, LinkToken,
            CommentToken, StringToken, KeywordToken, ControlKeywordToken, TypeToken, NumberToken,
            MethodToken, FieldToken, PreprocessorToken, XmlTagToken, AttributeNameToken,
            AttributeValueToken, MarkupExtensionToken, EntityToken, CDataToken, XmlDeclarationToken
        };

        /// <summary>Тёмная палитра редактора (тема по умолчанию).</summary>
        public static EditorThemeColors Dark { get; } = new EditorThemeColors(
            isDark: true,
            background: Rgb(0x1E, 0x1E, 0x1E),
            foreground: Rgb(0xD4, 0xD4, 0xD4),
            lineNumber: Rgb(0x85, 0x85, 0x85),
            currentLine: Rgb(0x28, 0x28, 0x28),
            selection: Rgb(0x26, 0x4F, 0x78),
            selectionForeground: Rgb(0xFF, 0xFF, 0xFF),
            // Ссылки: #3794FF из темы VS Code Dark+ вместо синего Brushes.Blue (#0000FF) — на фоне
            // #1E1E1E синий практически не читается.
            link: Rgb(0x37, 0x94, 0xFF),
            comment: Rgb(0x6A, 0x99, 0x55),
            @string: Rgb(0xCE, 0x91, 0x78),
            keyword: Rgb(0x56, 0x9C, 0xD6),
            controlKeyword: Rgb(0xC5, 0x86, 0xC0),
            type: Rgb(0x4E, 0xC9, 0xB0),
            number: Rgb(0xB5, 0xCE, 0xA8),
            method: Rgb(0xDC, 0xDC, 0xAA),
            field: Rgb(0x9C, 0xDC, 0xFE),
            preprocessor: Rgb(0x9B, 0x9B, 0x9B),
            xmlTag: Rgb(0x56, 0x9C, 0xD6),
            attributeName: Rgb(0x9C, 0xDC, 0xFE),
            attributeValue: Rgb(0xCE, 0x91, 0x78),
            markupExtension: Rgb(0xDC, 0xDC, 0xAA),
            entity: Rgb(0xD7, 0xBA, 0x7D),
            cdata: Rgb(0xE9, 0xD1, 0x8A),
            xmlDeclaration: Rgb(0x56, 0x9C, 0xD6));

        /// <summary>Светлая палитра редактора.</summary>
        public static EditorThemeColors Light { get; } = new EditorThemeColors(
            isDark: false,
            background: Rgb(0xFF, 0xFF, 0xFF),
            foreground: Rgb(0x00, 0x00, 0x00),
            lineNumber: Rgb(0x2B, 0x91, 0xAF),
            currentLine: Rgb(0xF0, 0xF0, 0xF0),
            selection: Rgb(0xAD, 0xD6, 0xFF),
            selectionForeground: Rgb(0x00, 0x00, 0x00),
            // Ссылки: тот же #3794FF и в светлой теме (по требованию — один цвет на обе темы).
            link: Rgb(0x37, 0x94, 0xFF),
            comment: Rgb(0x00, 0x80, 0x00),
            @string: Rgb(0xA3, 0x15, 0x15),
            keyword: Rgb(0x00, 0x00, 0xFF),
            controlKeyword: Rgb(0x8F, 0x08, 0xC4),
            type: Rgb(0x2B, 0x91, 0xAF),
            number: Rgb(0x09, 0x86, 0x58),
            method: Rgb(0x74, 0x53, 0x1F),
            field: Rgb(0x1F, 0x37, 0x7F),
            preprocessor: Rgb(0x80, 0x80, 0x80),
            xmlTag: Rgb(0x80, 0x00, 0x00),
            attributeName: Rgb(0xFF, 0x00, 0x00),
            attributeValue: Rgb(0x00, 0x00, 0xFF),
            markupExtension: Rgb(0x74, 0x53, 0x1F),
            entity: Rgb(0xA3, 0x15, 0x15),
            cdata: Rgb(0xA3, 0x15, 0x15),
            xmlDeclaration: Rgb(0x00, 0x00, 0xFF));

        /// <summary>Палитра по признаку темы.</summary>
        public static EditorThemeColors For(bool dark)
        {
            return dark ? Dark : Light;
        }

        /// <summary>Палитра по словарю ресурсов приложения (тема определяется яркостью фона).</summary>
        public static EditorThemeColors Detect(System.Windows.ResourceDictionary resources)
        {
            return For(StudioPalette.DetectDark(resources));
        }

        /// <summary>
        /// Токен подсветки для имени цвета из XSHD-определения AvalonEdit.
        /// Возвращает <c>null</c>, если имя не распознано (цвет остаётся дефолтным).
        /// </summary>
        public static string ResolveHighlightToken(string colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName))
            {
                return null;
            }

            string name = colorName.Trim();

            // Порядок важен: «TypeKeywords» должно попасть в Type, а не в Keyword,
            // «ControlFlow» — в управляющие конструкции, «AttributeValue» до «AttributeName» не пересекается.
            if (Contains(name, "declaration"))
            {
                return XmlDeclarationToken;
            }

            if (Contains(name, "markup") || Contains(name, "extension"))
            {
                return MarkupExtensionToken;
            }

            if (Contains(name, "attributename"))
            {
                return AttributeNameToken;
            }

            if (Contains(name, "attributevalue"))
            {
                return AttributeValueToken;
            }

            if (Contains(name, "entity"))
            {
                return EntityToken;
            }

            if (Contains(name, "cdata"))
            {
                return CDataToken;
            }

            if (Contains(name, "tag"))
            {
                return XmlTagToken;
            }

            if (Contains(name, "comment") || Contains(name, "remark"))
            {
                return CommentToken;
            }

            if (Contains(name, "preprocessor") || Contains(name, "pragma") || Contains(name, "region"))
            {
                return PreprocessorToken;
            }

            if (Contains(name, "string") || Contains(name, "char") || Contains(name, "verbatim"))
            {
                return StringToken;
            }

            return Contains(name, "number") || Contains(name, "digit")
                ? NumberToken
                : Contains(name, "method") || Contains(name, "call") || Contains(name, "function")
                ? MethodToken
                : Contains(name, "field") || Contains(name, "property") || Contains(name, "parameter") || Contains(name, "variable") || Contains(name, "enum")
                ? FieldToken
                : Contains(name, "type")
                ? TypeToken
                : Contains(name, "controlflow") || Contains(name, "control")
                ? ControlKeywordToken
                : Contains(name, "keyword") || Contains(name, "bool") || Contains(name, "null") || Contains(name, "truefalse") || Contains(name, "thisorbase")
                ? KeywordToken
                : null;
        }

        private static bool Contains(string name, string value)
        {
            return name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Color Rgb(byte r, byte g, byte b)
        {
            return Color.FromRgb(r, g, b);
        }
    }

    /// <summary>Набор токенов одной темы редактора (тёмной или светлой).</summary>
    public sealed class EditorThemeColors
    {
        private readonly Dictionary<string, Color> _tokens;

        internal EditorThemeColors(
            bool isDark,
            Color background,
            Color foreground,
            Color lineNumber,
            Color currentLine,
            Color selection,
            Color selectionForeground,
            Color link,
            Color comment,
            Color @string,
            Color keyword,
            Color controlKeyword,
            Color type,
            Color number,
            Color method,
            Color field,
            Color preprocessor,
            Color xmlTag,
            Color attributeName,
            Color attributeValue,
            Color markupExtension,
            Color entity,
            Color cdata,
            Color xmlDeclaration)
        {
            IsDark = isDark;
            _tokens = new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                [StudioEditorPalette.BackgroundToken] = background,
                [StudioEditorPalette.ForegroundToken] = foreground,
                [StudioEditorPalette.LineNumberToken] = lineNumber,
                [StudioEditorPalette.CurrentLineToken] = currentLine,
                [StudioEditorPalette.SelectionToken] = selection,
                [StudioEditorPalette.SelectionForegroundToken] = selectionForeground,
                [StudioEditorPalette.LinkToken] = link,
                [StudioEditorPalette.CommentToken] = comment,
                [StudioEditorPalette.StringToken] = @string,
                [StudioEditorPalette.KeywordToken] = keyword,
                [StudioEditorPalette.ControlKeywordToken] = controlKeyword,
                [StudioEditorPalette.TypeToken] = type,
                [StudioEditorPalette.NumberToken] = number,
                [StudioEditorPalette.MethodToken] = method,
                [StudioEditorPalette.FieldToken] = field,
                [StudioEditorPalette.PreprocessorToken] = preprocessor,
                [StudioEditorPalette.XmlTagToken] = xmlTag,
                [StudioEditorPalette.AttributeNameToken] = attributeName,
                [StudioEditorPalette.AttributeValueToken] = attributeValue,
                [StudioEditorPalette.MarkupExtensionToken] = markupExtension,
                [StudioEditorPalette.EntityToken] = entity,
                [StudioEditorPalette.CDataToken] = cdata,
                [StudioEditorPalette.XmlDeclarationToken] = xmlDeclaration
            };
        }

        /// <summary>Признак тёмной темы.</summary>
        public bool IsDark { get; }

        public Color Background => Get(StudioEditorPalette.BackgroundToken);

        public Color Foreground => Get(StudioEditorPalette.ForegroundToken);

        public Color LineNumber => Get(StudioEditorPalette.LineNumberToken);

        public Color CurrentLine => Get(StudioEditorPalette.CurrentLineToken);

        public Color Selection => Get(StudioEditorPalette.SelectionToken);

        public Color SelectionForeground => Get(StudioEditorPalette.SelectionForegroundToken);

        /// <summary>Цвет гиперссылок (URL в XAML, C#, VB, JSON).</summary>
        public Color Link => Get(StudioEditorPalette.LinkToken);

        /// <summary>Все токены палитры (имя → цвет).</summary>
        public IReadOnlyDictionary<string, Color> Tokens => _tokens;

        /// <summary>Цвет токена по имени.</summary>
        public Color Get(string tokenName)
        {
            return tokenName != null && _tokens.TryGetValue(tokenName, out Color color)
                ? color
                : IsDark ? Color.FromRgb(0xD4, 0xD4, 0xD4) : Color.FromRgb(0x00, 0x00, 0x00);
        }

        /// <summary>Цвет токена подсветки по имени цвета из XSHD-определения.</summary>
        public bool TryGetHighlightingColor(string colorName, out Color color)
        {
            string token = StudioEditorPalette.ResolveHighlightToken(colorName);
            if (token != null && _tokens.TryGetValue(token, out color))
            {
                return true;
            }

            color = default;
            return false;
        }
    }
}
