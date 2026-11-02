using System;
using System.IO;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;

namespace RPA.Helpers
{
    /// <summary>
    /// Resolves syntax highlighting and folding strategies for code documents
    /// based on their file extension.
    /// </summary>
    public static class CodeLanguageProvider
    {
        /// <summary>Language key used for XAML documents.</summary>
        public const string XamlLanguage = "XAML";

        /// <summary>Returns a language key for the file path, or null when the language is unsupported.</summary>
        public static string GetLanguageForExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            string extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase)
                ? XamlLanguage
                : string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                ? "C#"
                : string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                ? "Json"
                : string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xshd", StringComparison.OrdinalIgnoreCase)
                ? "XML"
                : null;
        }

        /// <summary>Returns the highlighting definition for a file path, or null.</summary>
        public static IHighlightingDefinition GetDefinitionForExtension(string filePath)
        {
            return GetHighlightingDefinition(GetLanguageForExtension(filePath));
        }

        /// <summary>Returns the highlighting definition for a language key, or null.</summary>
        public static IHighlightingDefinition GetHighlightingDefinition(string language)
        {
            return StudioEditorTheme.GetDefinition(language);
        }

        /// <summary>Возвращает определение подсветки XAML (цвета — из StudioEditorTheme).</summary>
        public static IHighlightingDefinition GetXamlDefinition()
        {
            return StudioEditorTheme.GetDefinition(XamlLanguage);
        }

        /// <summary>Creates a folding strategy for a language key, or null when not supported.</summary>
        public static XmlFoldingStrategy CreateFoldingStrategy(string language)
        {
            if (language == null)
            {
                return null;
            }

            if (language == XamlLanguage || language == "XML")
            {
                return new XmlFoldingStrategy();
            }

            // C# folding will be added together with the full C# editor (Roslyn).
            return null;
        }
    }
}
