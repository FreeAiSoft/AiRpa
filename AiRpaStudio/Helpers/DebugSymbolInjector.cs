using System;
using System.Activities;
using System.Activities.Debugger;
using System.Activities.Debugger.Symbol;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xaml;
using System.Xml;
using System.Xml.Linq;
using RPA.Logging;

namespace RPA.Helpers
{
    /// <summary>
    /// Вписывает <c>sads:DebugSymbol.Symbol</c> в XAML рабочего процесса так, как это делает штатный
    /// WF-дизайнер (<c>DesignTimeXamlWriter</c> + <c>WorkflowDesignerXamlHelper</c>): сериализованный
    /// текст сначала получает заглушку символа на теле корневой активности, затем текст десериализуется
    /// через <see cref="XamlDebuggerXmlReader"/> (собирая <see cref="SourceLocation"/>), по собранным
    /// локациям строится <see cref="WorkflowSymbol"/>, и заглушка заменяется на <c>Encode()</c>.
    /// <para>
    /// Без символа <see cref="RuntimeSourceLocationMap"/> не находит тело процесса, и точка останова
    /// на нём молча не срабатывает («Not found SourceLocation for …»).
    /// </para>
    /// </summary>
    internal static class DebugSymbolInjector
    {
        private const string ActivitiesNamespace = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string DebugSymbolNamespace = "http://schemas.microsoft.com/netfx/2010/xaml/activities/debugger";
        private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        private static readonly string Placeholder = new WorkflowSymbol { FileName = @"C:\Empty.xaml" }.Encode();

        /// <summary>
        /// Возвращает XAML с вписанным отладочным символом, привязанным к <paramref name="filePath"/>.
        /// Если тело процесса отсутствует (пустая активность), символ собрать не из чего — возвращается
        /// исходный текст. Любая ошибка глушится в диагностику с возвратом исходного текста: символ —
        /// улучшение отладки, а не причина ломать сохранение.
        /// </summary>
        internal static string Inject(string xaml, string filePath, XamlSchemaContext schemaContext = null)
        {
            if (string.IsNullOrWhiteSpace(xaml) || string.IsNullOrWhiteSpace(filePath))
            {
                return xaml;
            }

            schemaContext = schemaContext ?? new XamlSchemaContext();

            try
            {
                string fullPath = Path.GetFullPath(filePath);

                string templated = InsertPlaceholder(xaml);
                if (templated == null)
                {
                    SourceLocationMappingDiagnostics.Write("DebugSymbolInjector: no workflow body, skip symbol for " + fullPath);
                    return xaml;
                }

                Deserialize(templated, schemaContext, out Dictionary<object, SourceLocation> sourceLocations, out Activity root);
                if (root == null)
                {
                    SourceLocationMappingDiagnostics.Write("DebugSymbolInjector: root activity not deserialized for " + fullPath);
                    return xaml;
                }

                ICollection<ActivitySymbol> symbols = SourceLocationProvider.GetSymbols(root, sourceLocations);
                if (symbols == null || symbols.Count == 0)
                {
                    SourceLocationMappingDiagnostics.Write("DebugSymbolInjector: no source symbols for " + fullPath);
                    return xaml;
                }

                string encoded = new WorkflowSymbol { FileName = fullPath, Symbols = symbols }.Encode();
                return templated.Replace(Placeholder, encoded);
            }
            catch (Exception ex)
            {
                SourceLocationMappingDiagnostics.WriteException(ex, "DebugSymbolInjector: failed for " + filePath);
                return xaml;
            }
        }

        private static string InsertPlaceholder(string xaml)
        {
            XDocument document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace);
            XElement root = document.Root;

            if (root == null
                || root.Name.LocalName != "Activity"
                || root.Name.NamespaceName != ActivitiesNamespace)
            {
                return null;
            }

            EnsureNamespace(root, "mc", MarkupCompatibilityNamespace);
            EnsureNamespace(root, "sads", DebugSymbolNamespace);
            MergeIgnorable(root, "sads");

            XElement body = FindBody(root);
            if (body == null)
            {
                return null;
            }

            // Идемпотентность: повторный Inject не должен плодить несколько элементов символа.
            body.Elements()
                .Where(e => e.Name.NamespaceName == DebugSymbolNamespace && e.Name.LocalName == "DebugSymbol.Symbol")
                .Remove();

            body.Add(new XElement(XName.Get("DebugSymbol.Symbol", DebugSymbolNamespace), Placeholder));

            return Save(document);
        }

        private static XElement FindBody(XElement root)
        {
            foreach (XElement child in root.Elements())
            {
                // Тело — первая активность в пространстве имён WF. x:Members (аргументы) живут в
                // пространстве xaml, ViewStateManager — в sap2010, поэтому отсекаются по namespace.
                if (child.Name.NamespaceName != ActivitiesNamespace)
                {
                    continue;
                }

                string localName = child.Name.LocalName;
                if (localName == "Activity" || localName.StartsWith("TextExpression.", StringComparison.Ordinal))
                {
                    continue;
                }

                return child;
            }

            return null;
        }

        private static void Deserialize(
            string xaml,
            XamlSchemaContext schemaContext,
            out Dictionary<object, SourceLocation> sourceLocations,
            out Activity root)
        {
            var locations = new Dictionary<object, SourceLocation>();
            Activity rootActivity = null;

            using (var textReader = new StringReader(xaml))
            using (var debuggerReader = new XamlDebuggerXmlReader(textReader, schemaContext))
            using (XamlReader builderReader = ActivityXamlServices.CreateBuilderReader(debuggerReader, schemaContext))
            {
                debuggerReader.SourceLocationFound += (sender, args) =>
                {
                    if (args.Target != null && args.SourceLocation != null && !locations.ContainsKey(args.Target))
                    {
                        locations.Add(args.Target, args.SourceLocation);
                    }
                };

                object loaded = XamlServices.Load(builderReader);
                rootActivity = ToRootActivity(loaded);
            }

            sourceLocations = locations;
            root = rootActivity;
        }

        private static Activity ToRootActivity(object deserialized)
        {
            if (deserialized is ActivityBuilder builder)
            {
                // ActivityBuilder.ConvertToDynamicActivity() — internal; собираем корень вручную.
                // Для GetSymbols важна структура Implementation: те же объекты Activity, что и в builder.
                return new DynamicActivity
                {
                    Name = builder.Name,
                    Implementation = () => builder.Implementation
                };
            }

            return deserialized as Activity;
        }

        private static void EnsureNamespace(XElement root, string prefix, string uri)
        {
            if (root.GetNamespaceOfPrefix(prefix) == uri)
            {
                return;
            }

            if (!string.IsNullOrEmpty(root.GetPrefixOfNamespace(uri)))
            {
                return;
            }

            root.SetAttributeValue(XNamespace.Xmlns + prefix, uri);
        }

        private static void MergeIgnorable(XElement root, string prefix)
        {
            XNamespace mc = MarkupCompatibilityNamespace;
            string current = (string)root.Attribute(mc + "Ignorable") ?? string.Empty;

            var prefixes = current
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (prefixes.Contains(prefix))
            {
                return;
            }

            prefixes.Add(prefix);
            root.SetAttributeValue(mc + "Ignorable", string.Join(" ", prefixes));
        }

        private static string Save(XDocument document)
        {
            var settings = new XmlWriterSettings
            {
                Indent = false,
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                CloseOutput = false
            };

            using (var output = new StringWriter())
            {
                using (XmlWriter writer = XmlWriter.Create(output, settings))
                {
                    document.Save(writer);
                }

                return output.ToString();
            }
        }
    }
}
