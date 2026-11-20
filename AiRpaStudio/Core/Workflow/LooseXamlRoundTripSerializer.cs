using System;
using System.Activities;
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
            xaml = RewriteFacades(xaml, registry);
            xaml = RewriteCodeTypes(xaml, registry);
            xaml = EnsureActivityNamespaces(xaml, documentRoot);
            return xaml;
        }

        /// <summary>
        /// Гарантирует, что корень XAML объявляет CLR-пространства имён всех активностей,
        /// которым они нужны (пользовательские и библиотечные активности). Идемпотентно: если
        /// URI уже объявлен, документ не меняется.
        /// </summary>
        private static string EnsureActivityNamespaces(string xaml, object documentRoot)
        {
            var activityTypes = new HashSet<Type>();
            foreach (Activity activity in ActivitiesOf(documentRoot))
            {
                activityTypes.Add(activity.GetType());
            }

            return XamlNamespaceHelper.EnsureClrNamespaces(xaml, activityTypes);
        }

        /// <summary>Все активности дерева документа (у процесса с <c>x:Class</c> — из реализации).</summary>
        private static IEnumerable<Activity> ActivitiesOf(object documentRoot)
        {
            if (documentRoot is ActivityBuilder builder && builder.Implementation != null)
            {
                return WorkflowInspectionServices.GetActivities(builder.Implementation);
            }

            if (documentRoot is DynamicActivity dynamicActivity && dynamicActivity.Implementation != null)
            {
                return WorkflowInspectionServices.GetActivities(dynamicActivity.Implementation());
            }

            if (documentRoot is Activity activity)
            {
                return WorkflowInspectionServices.GetActivities(activity);
            }

            return Enumerable.Empty<Activity>();
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

        /// <summary>
        /// Переписывает ссылки на code-behind CLR-типы (<c>TargetType="local:Code"</c>) обратно
        /// в короткую форму без синтетической сборки. Штатный WF-писатель сериализует
        /// <see cref="System.Type"/> из in-memory сборки как <c>xmlns:r="clr-namespace:RPA_1;assembly=RpaLooseCode_*"</c>
        /// и <c>TargetType="r:Code"</c>. Мы убираем <c>;assembly=RpaLooseCode_*</c> из объявления
        /// пространства имён и переписываем значение атрибута в <c>local:Code</c>. Фасады
        /// <c>DynamicActivity</c> (вложенные процессы) этим проходом не затрагиваются.
        /// </summary>
        private static string RewriteCodeTypes(string xaml, WorkflowTypeRegistry registry)
        {
            if (registry.CodeTypes == null || registry.CodeTypes.Count == 0)
            {
                return xaml;
            }

            XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            XElement root = document.Root;

            if (root == null)
            {
                return xaml;
            }

            var syntheticPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (XAttribute attribute in root.Attributes().Where(a => a.IsNamespaceDeclaration).ToList())
            {
                string prefix = attribute.Name.LocalName;
                if (prefix == "xmlns")
                {
                    continue;
                }

                if (TryParseSyntheticClrNamespace(attribute.Value, out string clrNamespace, out _))
                {
                    syntheticPrefixes[prefix] = clrNamespace;
                }
            }

            if (syntheticPrefixes.Count == 0)
            {
                return xaml;
            }

            // Учитываем уже объявленные обычные clr-namespace (без ;assembly=), чтобы EnsurePrefix
            // переиспользовал существующий local, а не плодил local2.
            var prefixByNamespace = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (XAttribute attribute in root.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                string prefix = attribute.Name.LocalName;
                if (prefix == "xmlns")
                {
                    continue;
                }

                if (attribute.Value.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal)
                    && attribute.Value.IndexOf(";assembly=", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    prefixByNamespace[attribute.Value] = prefix;
                }
            }

            // Значения атрибутов (TargetType="r:Code" и аналогичные) переписываем в "local:Code".
            foreach (XElement element in root.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList())
                {
                    if (!TryRewriteCodeTypeValue(attribute.Value, syntheticPrefixes, registry, root, prefixByNamespace, out string newValue))
                    {
                        continue;
                    }

                    attribute.Value = newValue;
                }
            }

            // Удаляем синтетические объявления пространств имён.
            foreach (XAttribute attribute in root.Attributes().Where(a => a.IsNamespaceDeclaration).ToList())
            {
                string prefix = attribute.Name.LocalName;
                if (prefix == "xmlns")
                {
                    continue;
                }

                if (syntheticPrefixes.ContainsKey(prefix))
                {
                    attribute.Remove();
                }
            }

            // Гарантируем объявление clr-namespace без assembly для всех code-behind пространств имён.
            foreach (string clrNamespace in syntheticPrefixes.Values.Distinct(StringComparer.Ordinal))
            {
                EnsurePrefix(root, prefixByNamespace, ClrNamespacePrefix + clrNamespace);
            }

            return WriteDocument(document);
        }

        private static bool TryRewriteCodeTypeValue(
            string value,
            Dictionary<string, string> syntheticPrefixes,
            WorkflowTypeRegistry registry,
            XElement root,
            Dictionary<string, string> prefixByNamespace,
            out string newValue)
        {
            newValue = value;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int colon = value.IndexOf(':');
            if (colon <= 0)
            {
                return false;
            }

            string prefix = value.Substring(0, colon);
            if (!syntheticPrefixes.TryGetValue(prefix, out string clrNamespace))
            {
                return false;
            }

            string typeName = value.Substring(colon + 1);
            string fullName = clrNamespace + "." + typeName;

            if (!registry.TryGetCodeType(fullName, out _))
            {
                return false;
            }

            string localPrefix = EnsurePrefix(root, prefixByNamespace, ClrNamespacePrefix + clrNamespace);
            newValue = localPrefix + ":" + typeName;
            return true;
        }

        private static bool TryParseSyntheticClrNamespace(
            string value,
            out string clrNamespace,
            out string assemblyName)
        {
            clrNamespace = null;
            assemblyName = null;

            if (string.IsNullOrWhiteSpace(value)
                || !value.StartsWith(ClrNamespacePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string rest = value.Substring(ClrNamespacePrefix.Length);
            int separator = rest.IndexOf(";assembly=", StringComparison.OrdinalIgnoreCase);
            if (separator < 0)
            {
                return false;
            }

            clrNamespace = rest.Substring(0, separator).Trim();
            assemblyName = rest.Substring(separator + ";assembly=".Length).Trim();

            return clrNamespace.Length > 0
                   && assemblyName.StartsWith("RpaLooseCode_", StringComparison.OrdinalIgnoreCase);
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
