using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Добавление XML-пространства имён (<c>xmlns:prefix="clr-namespace:...;assembly=..."</c>)
    /// в корень XAML-документа для CLR-типов активностей (этап «добавление активности из тулбокса»).
    /// <para>
    /// Системные активности WF (<c>System.Activities.*</c>) живут в стандартном пространстве имён
    /// <c>http://schemas.microsoft.com/netfx/2009/xaml/activities</c> и отдельного <c>xmlns</c> не требуют.
    /// Пользовательским и библиотечным активностям (Browser/Flow/OS/Debug/собственные) пространство имён
    /// нужно объявлять, иначе после сохранения ссылка на тип не резолвится.
    /// </para>
    /// </summary>
    internal static class XamlNamespaceHelper
    {
        private const string ClrNamespacePrefix = "clr-namespace:";

        /// <summary>
        /// URI CLR-пространства имён для типа: <c>clr-namespace:NS;assembly=Assembly</c>.
        /// Типу без namespace (<c>null</c>/пусто) объявление не требуется — возвращается <c>null</c>.
        /// </summary>
        internal static string BuildClrNamespace(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            string ns = type.Namespace;
            if (string.IsNullOrWhiteSpace(ns))
            {
                return null;
            }

            string assembly = type.Assembly?.GetName()?.Name;
            return string.IsNullOrWhiteSpace(assembly)
                ? ClrNamespacePrefix + ns
                : ClrNamespacePrefix + ns + ";assembly=" + assembly;
        }

        /// <summary>
        /// Предпочтительный префикс для типа — первые буквы сегментов его namespace
        /// (<c>Activities.Browser</c> → <c>ab</c>), как это делает штатный сериализатор WF.
        /// </summary>
        internal static string BuildPreferredPrefix(Type type)
        {
            string ns = type?.Namespace;
            if (string.IsNullOrWhiteSpace(ns))
            {
                return "local";
            }

            var prefix = new StringBuilder();
            foreach (string segment in ns.Split('.'))
            {
                if (segment.Length > 0 && char.IsLetter(segment[0]))
                {
                    prefix.Append(char.ToLowerInvariant(segment[0]));
                }
            }

            string result = prefix.ToString();
            if (result.Length == 0)
            {
                return "local";
            }

            // «local» зарезервирован под loose-ссылки на вложенные процессы (clr-namespace без assembly).
            return result == "local" ? "activity" : result;
        }

        /// <summary>Нужен ли типу отдельный <c>xmlns</c>: системные активности WF пропускаются.</summary>
        internal static bool ShouldDeclareNamespace(Type type)
        {
            if (type == null || string.IsNullOrWhiteSpace(type.Namespace))
            {
                return false;
            }

            return type.Namespace != "System.Activities"
                && !type.Namespace.StartsWith("System.Activities.", StringComparison.Ordinal);
        }

        /// <summary>
        /// Добавляет в корень XAML объявление <paramref name="clrNamespace"/>, если такого URI ещё нет.
        /// Если URI уже объявлен под другим префиксом — документ не меняется.
        /// </summary>
        internal static string EnsureClrNamespace(string xaml, string clrNamespace, string preferredPrefix = null)
        {
            if (string.IsNullOrWhiteSpace(xaml) || string.IsNullOrWhiteSpace(clrNamespace))
            {
                return xaml;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            }
            catch (XmlException)
            {
                // Битый XML не правим: пусть об этом скажет загрузка/валидация.
                return xaml;
            }

            XElement root = document?.Root;
            if (root == null)
            {
                return xaml;
            }

            foreach (XAttribute attribute in root.Attributes().Where(IsNamespaceDeclaration))
            {
                if (string.Equals(attribute.Value, clrNamespace, StringComparison.Ordinal))
                {
                    // Уже объявлен — дубль не нужен.
                    return xaml;
                }
            }

            string prefix = ChoosePrefix(root, preferredPrefix);
            root.Add(new XAttribute(XNamespace.Xmlns + prefix, clrNamespace));

            return WriteDocument(document);
        }

        /// <summary>Добавляет в корень XAML пространства имён для всех активностей, которым они нужны.</summary>
        internal static string EnsureClrNamespaces(string xaml, IEnumerable<Type> activityTypes)
        {
            if (string.IsNullOrWhiteSpace(xaml) || activityTypes == null)
            {
                return xaml;
            }

            string result = xaml;

            foreach (Type type in activityTypes)
            {
                if (!ShouldDeclareNamespace(type))
                {
                    continue;
                }

                string clrNamespace = BuildClrNamespace(type);
                if (clrNamespace == null)
                {
                    continue;
                }

                result = EnsureClrNamespace(result, clrNamespace, BuildPreferredPrefix(type));
            }

            return result;
        }

        private static bool IsNamespaceDeclaration(XAttribute attribute)
        {
            return attribute != null && attribute.IsNamespaceDeclaration;
        }

        private static string ChoosePrefix(XElement root, string preferredPrefix)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (XAttribute attribute in root.Attributes().Where(IsNamespaceDeclaration))
            {
                string name = attribute.Name.LocalName;
                if (name != "xmlns")
                {
                    used.Add(name);
                }
            }

            string candidate = SanitizePrefix(string.IsNullOrWhiteSpace(preferredPrefix) ? "local" : preferredPrefix);

            if (!used.Contains(candidate))
            {
                return candidate;
            }

            int index = 1;
            while (used.Contains(candidate + index))
            {
                index++;
            }

            return candidate + index;
        }

        private static string SanitizePrefix(string prefix)
        {
            var sb = new StringBuilder();
            foreach (char c in prefix)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
            }

            string result = sb.ToString();
            if (result.Length == 0)
            {
                return "local";
            }

            // XML-префикс не может начинаться с цифры.
            if (!char.IsLetter(result[0]) && result[0] != '_')
            {
                result = "_" + result;
            }

            return result;
        }

        private static string WriteDocument(XDocument document)
        {
            var settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false
            };

            var text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, settings))
            {
                document.Save(writer);
            }

            return text.ToString();
        }
    }
}
