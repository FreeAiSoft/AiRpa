using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using VibeCoder.Core.Models;

namespace RPA.Helpers
{
    /// <summary>
    /// Единая точка оформления редакторов кода и XAML (этап 12 плана docs\Vs2026_Theme_Plan.md, п.5).
    /// <para>
    /// Что делает:
    /// </para>
    /// <list type="bullet">
    /// <item>ставит фон, текст, номера строк, подсветку текущей строки и выделение из
    /// <see cref="StudioEditorPalette"/> (цвета как в Visual Studio);</item>
    /// <item>шрифт — только <see cref="StudioFonts.CodeFont"/> (Roboto);</item>
    /// <item>перекрашивает определения подсветки: XSHD читается в <see cref="XmlDocument"/>, у элементов
    /// <c>&lt;Color name="…"&gt;</c> подменяется <c>foreground</c> по имени цвета, и определение
    /// загружается заново. Поэтому один и тот же XSHD (в том числе встроенный в AvalonEdit
    /// <c>CSharp-Mode.xshd</c> с его Green/Blue/Magenta) работает и в тёмной, и в светлой теме.</item>
    /// </list>
    /// <para>
    /// Определения кэшируются по паре «язык + тема»: переключение темы создаёт новую копию, а не мутирует
    /// общее определение <see cref="HighlightingManager"/> (иначе открытые редакторы не перерисовались бы).
    /// </para>
    /// </summary>
    public static class StudioEditorTheme
    {
        private static readonly Dictionary<string, IHighlightingDefinition> Cache =
            new Dictionary<string, IHighlightingDefinition>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Имена XSHD-ресурсов по языку. Для остальных языков используется HighlightingManager.</summary>
        private static readonly Dictionary<string, string> ResourceNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [CodeLanguageProvider.XamlLanguage] = "Xaml.xshd",
                ["C#"] = "CSharp-Mode.xshd",
                ["VB"] = "VB-Mode.xshd",
                ["Json"] = "Json.xshd",
                ["XML"] = "XML-Mode.xshd"
            };

        /// <summary>Определение подсветки для языка под текущую тему приложения.</summary>
        public static IHighlightingDefinition GetDefinition(string language)
        {
            return GetDefinition(language, StudioPalette.Current);
        }

        /// <summary>Определение подсветки для языка под конкретную тему (кэшируется).</summary>
        public static IHighlightingDefinition GetDefinition(string language, StudioTheme theme)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            string key = language + "|" + theme;

            lock (Cache)
            {
                if (Cache.TryGetValue(key, out IHighlightingDefinition cached))
                {
                    return cached;
                }
            }

            IHighlightingDefinition definition = CreateDefinition(language, theme);
            if (definition == null)
            {
                return null;
            }

            lock (Cache)
            {
                Cache[key] = definition;
            }

            return definition;
        }

        /// <summary>Оформление редактора под текущую тему приложения.</summary>
        public static void Apply(TextEditor editor)
        {
            Apply(editor, StudioPalette.Current);
        }

        /// <summary>Оформление редактора под конкретную тему (без подсветки синтаксиса).</summary>
        public static void Apply(TextEditor editor, StudioTheme theme)
        {
            ApplyChrome(editor, theme);
        }

        /// <summary>Оформление и подсветка редактора для языка.</summary>
        public static void Apply(TextEditor editor, string language)
        {
            StudioTheme theme = StudioPalette.Current;
            ApplyChrome(editor, theme);
            editor.SyntaxHighlighting = GetDefinition(language, theme);
        }

        /// <summary>Фон, текст, шрифт, номера строк, выделение и текущая строка.</summary>
        public static void ApplyChrome(TextEditor editor, StudioTheme theme)
        {
            if (editor == null)
            {
                return;
            }

            EditorThemeColors colors = StudioEditorPalette.For(theme);

            editor.Background = Brush(colors.Background);
            editor.Foreground = Brush(colors.Foreground);
            editor.FontFamily = StudioFonts.CodeFont;
            editor.LineNumbersForeground = Brush(colors.LineNumber);

            if (editor.TextArea != null)
            {
                editor.TextArea.SelectionBrush = Brush(colors.Selection);
                editor.TextArea.SelectionForeground = Brush(colors.SelectionForeground);
            }

            editor.Options.HighlightCurrentLine = true;

            if (editor.TextArea?.TextView != null)
            {
                editor.TextArea.TextView.CurrentLineBackground = Brush(colors.CurrentLine);
                editor.TextArea.TextView.LinkTextForegroundBrush = Brush(colors.Link);
            }
        }

        /// <summary>
        /// Цвет гиперссылок редактора под текущую тему приложения.
        /// <para>
        /// Отдельно от <see cref="ApplyChrome"/> нужно редакторам выражений: VB-редактор и
        /// <c>RoslynCodeEditor</c> красят фон и текст сами, поэтому вызывать полный <c>ApplyChrome</c> там нельзя,
        /// а ссылки (URL в тексте выражения) всё равно должны читаться.
        /// </para>
        /// </summary>
        public static void ApplyLinkColor(TextEditor editor)
        {
            ApplyLinkColor(editor, StudioPalette.Current);
        }

        /// <summary>Цвет гиперссылок редактора под конкретную тему.</summary>
        public static void ApplyLinkColor(TextEditor editor, StudioTheme theme)
        {
            ApplyLinkColor(editor, StudioEditorPalette.For(theme));
        }

        /// <summary>
        /// Цвет гиперссылок по палитре: <c>#3794FF</c> вместо <c>Brushes.Blue</c> по умолчанию
        /// (AvalonEdit ставит <c>Brushes.Blue</c> в <c>TextView.LinkTextForegroundBrush</c> и перекрывает
        /// им подсветку синтаксиса; на тёмном фоне #0000FF почти не виден).
        /// </summary>
        public static void ApplyLinkColor(TextEditor editor, EditorThemeColors colors)
        {
            if (editor?.TextArea?.TextView == null || colors == null)
            {
                return;
            }

            editor.TextArea.TextView.LinkTextForegroundBrush = Brush(colors.Link);
        }

        private static IHighlightingDefinition CreateDefinition(string language, StudioTheme theme)
        {
            if (ResourceNames.TryGetValue(language, out string resourceName))
            {
                IHighlightingDefinition themed = TryLoadThemed(resourceName, StudioEditorPalette.For(theme));
                if (themed != null)
                {
                    return themed;
                }
            }

            return HighlightingManager.Instance.GetDefinition(language);
        }

        private static IHighlightingDefinition TryLoadThemed(string resourceName, EditorThemeColors colors)
        {
            try
            {
                using Stream stream = OpenResource(resourceName);
                if (stream == null)
                {
                    return null;
                }

                XmlDocument document = new XmlDocument { XmlResolver = null };
                document.Load(stream);

                // Цвета подменяются по именам: XSHD остаётся один на две темы.
                ApplyHighlightingColors(document.DocumentElement, colors);

                using XmlNodeReader reader = new XmlNodeReader(document);
                return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch (Exception ex) when (ex is IOException || ex is XmlException || ex is InvalidOperationException
                                       || ex is NotSupportedException || ex is ArgumentException)
            {
                // Без своей копии остаётся дефолтное определение AvalonEdit (светлые цвета, но редактор работает).
                return null;
            }
        }

        private static void ApplyHighlightingColors(XmlElement root, EditorThemeColors colors)
        {
            if (root == null || colors == null)
            {
                return;
            }

            XmlNodeList colorNodes = root.SelectNodes("//*[local-name()='Color']");
            if (colorNodes == null)
            {
                return;
            }

            foreach (XmlNode node in colorNodes)
            {
                if (!(node is XmlElement element))
                {
                    continue;
                }

                string name = element.GetAttribute("name");
                if (string.IsNullOrEmpty(name) || !colors.TryGetHighlightingColor(name, out Color color))
                {
                    continue;
                }

                element.SetAttribute("foreground", ToHex(color));
            }
        }

        private static Stream OpenResource(string fileName)
        {
            foreach (Assembly assembly in new[] { typeof(StudioEditorTheme).Assembly, typeof(HighlightingManager).Assembly })
            {
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return assembly.GetManifestResourceStream(name);
                    }
                }
            }

            return null;
        }

        private static string ToHex(Color color)
        {
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }

        private static SolidColorBrush Brush(Color color)
        {
            return WfThemeStyles.Brush(color);
        }
    }
}
