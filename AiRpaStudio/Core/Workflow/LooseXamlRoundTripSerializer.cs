using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xaml;
using System.Xml;
using System.Xml.Linq;
using System.Activities.XamlIntegration;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Обратная сериализация loose-XAML (шаг 5 плана LooseXamlTypeService):
    /// сериализует <see cref="System.Activities.ActivityBuilder"/> штатным WF-писателем
    /// (<see cref="ActivityXamlServices.CreateBuilderWriter"/>), а затем заменяет
    /// <c>&lt;DynamicActivity Name="RPA_1.SubProcessN"/&gt;</c> обратно на
    /// <c>&lt;local:SubProcessN/&gt;</c>. Ссылки остаются ссылками, вложенные файлы не трогаются.
    /// </summary>
    internal static class LooseXamlRoundTripSerializer
    {
        private const string ClrNamespacePrefix = "clr-namespace:";
        private const string DynamicActivityName = "DynamicActivity";
        private const string NameAttribute = "Name";

        internal static string Serialize(object documentRoot, WorkflowTypeRegistry registry)
        {
            if (documentRoot == null)
            {
                throw new ArgumentNullException(nameof(documentRoot));
            }

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            string xaml = SerializeObject(documentRoot);
            return RewriteFacades(xaml, registry);
        }

        /// <summary>
        /// Тот же сериализатор, которым сохраняет студия: <c>ActivityBuilderXamlWriter</c> поверх
        /// <c>XamlXmlWriter</c>. Корень <c>ActivityBuilder</c> превращается в
        /// <c>&lt;Activity x:Class="..."&gt;</c>, вложенные фасады — в
        /// <c>&lt;DynamicActivity Name="..."&gt;</c>.
        /// </summary>
        private static string SerializeObject(object documentRoot)
        {
            var text = new StringBuilder();

            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false
            };

            using (XmlWriter xml = XmlWriter.Create(text, settings))
            using (var inner = new XamlXmlWriter(xml, new XamlSchemaContext()))
            using (XamlWriter writer = ActivityXamlServices.CreateBuilderWriter(inner))
            {
                XamlServices.Save(writer, documentRoot);
            }

            return text.ToString();
        }

        /// <summary>
        /// Превращает каждый <c>DynamicActivity</c>, чей <c>Name</c> зарегистрирован в реестре,
        /// обратно в ссылку на локальный тип <c>&lt;local:SubProcessN/&gt;</c>. Атрибуты фасада
        /// (в первую очередь <c>sap2010:WorkflowViewState.IdRef</c>) сохраняются; атрибут
        /// <c>Name</c> отбрасывается, потому что имя типа уже закодировано именем элемента.
        /// </summary>
        private static string RewriteFacades(string xaml, WorkflowTypeRegistry registry)
        {
            XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            XElement root = document.Root;

            if (root == null)
            {
                return xaml;
            }

            var prefixByNamespace = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (XAttribute attribute in root.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                string prefix = attribute.Name.LocalName;
                if (prefix == "xmlns")
                {
                    continue;
                }

                if (attribute.Value.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal))
                {
                    prefixByNamespace[attribute.Value] = prefix;
                }
            }

            foreach (XElement element in root.Descendants().Where(IsDynamicActivity).ToList())
            {
                string fullName = (string)element.Attribute(NameAttribute);
                if (string.IsNullOrWhiteSpace(fullName) || !registry.TryGet(fullName, out XamlTypeFacade facade))
                {
                    continue;
                }

                string clrNamespace = ClrNamespacePrefix + facade.Namespace;
                string prefix = EnsurePrefix(root, prefixByNamespace, clrNamespace);

                var replacement = new XElement(XName.Get(facade.Name, clrNamespace));

                foreach (XAttribute attribute in element.Attributes().ToList())
                {
                    // Name — это x:Class, он уже стал именем элемента.
                    if (attribute.Name.LocalName == NameAttribute
                        && string.IsNullOrEmpty(attribute.Name.NamespaceName))
                    {
                        continue;
                    }

                    // DisplayName мы выставляем сами (для подписи блока в конструкторе). Если он равен
                    // короткому имени класса, в файл его писать не нужно — иначе открытие→сохранение
                    // добавило бы лишний атрибут. Пользовательское DisplayName (≠ имени класса) сохраняется.
                    if (attribute.Name.LocalName == "DisplayName"
                        && string.IsNullOrEmpty(attribute.Name.NamespaceName)
                        && string.Equals(attribute.Value, facade.Name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    replacement.Add(new XAttribute(attribute.Name, attribute.Value));
                }

                element.ReplaceWith(replacement);
            }

            return WriteDocument(document);
        }

        private static bool IsDynamicActivity(XElement element)
        {
            return element.Name.LocalName == DynamicActivityName;
        }

        private static string EnsurePrefix(
            XElement root,
            Dictionary<string, string> prefixByNamespace,
            string clrNamespace)
        {
            if (prefixByNamespace.TryGetValue(clrNamespace, out string existing))
            {
                return existing;
            }

            string prefix = "local";
            int index = 1;
            while (prefixByNamespace.Values.Contains(prefix, StringComparer.Ordinal))
            {
                prefix = "local" + index;
                index++;
            }

            root.Add(new XAttribute(XNamespace.Xmlns + prefix, clrNamespace));
            prefixByNamespace[clrNamespace] = prefix;
            return prefix;
        }

        private static string WriteDocument(XDocument document)
        {
            var text = new StringBuilder();

            var settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false
            };

            using (XmlWriter writer = XmlWriter.Create(text, settings))
            {
                document.Save(writer);
            }

            return text.ToString();
        }
    }
}
