using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

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

        private static readonly object RegisterGate = new object();

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
            return language == null
                ? null
                : language == XamlLanguage ? GetXamlDefinition() : HighlightingManager.Instance.GetDefinition(language);
        }

        /// <summary>Loads and registers the custom XAML highlighting definition from the embedded resource.</summary>
        public static IHighlightingDefinition GetXamlDefinition()
        {
            lock (RegisterGate)
            {
                IHighlightingDefinition existing = HighlightingManager.Instance.GetDefinition(XamlLanguage);
                if (existing != null)
                {
                    return existing;
                }

                using Stream stream = OpenEmbeddedResource("Xaml.xshd");
                using XmlReader reader = XmlReader.Create(stream);
                IHighlightingDefinition definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                HighlightingManager.Instance.RegisterHighlighting(XamlLanguage, new[] { ".xaml" }, definition);
                return definition;
            }
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

        private static Stream OpenEmbeddedResource(string fileName)
        {
            Assembly assembly = typeof(CodeLanguageProvider).Assembly;
            string resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

            return resourceName == null
                ? throw new FileNotFoundException("Embedded resource not found: " + fileName)
                : assembly.GetManifestResourceStream(resourceName);
        }
    }
}
