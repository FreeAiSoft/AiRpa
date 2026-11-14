using System;
using System.Activities;
using System.Activities.Debugger.Symbol;
using System.Activities.Statements;
using System.Activities.XamlIntegration;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using RPA.Core.Generation;
using RPA.Core.Workflow;
using RPA.Helpers;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Сквозная регрессия: XAML, сохранённый студией (генерация и loose-конструктор), обязан быть
    /// покрыт <see cref="RuntimeSourceLocationMap"/> — иначе точка останова на теле процесса не сработает.
    /// </summary>
    [TestFixture]
    public class WorkflowSaveDebugSymbolRegressionTests
    {
        private const string DebugSymbolNs = "http://schemas.microsoft.com/netfx/2010/xaml/activities/debugger";
        private const string ActivitiesNs = "http://schemas.microsoft.com/netfx/2009/xaml/activities";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "airpa_save_sym_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [Test]
        public void SavedWorkflow_IsCoveredByRuntimeSourceLocationMap()
        {
            string path = Path.Combine(_tempDir, "Main.xaml");
            WorkflowXamlSerializer.Save(SimpleBuilder(), path);

            Activity activity = ActivityXamlServices.Load(path, new ActivityXamlServicesSettings { CompileExpressions = true });
            var mapping = RuntimeSourceLocationMap.Build(activity);

            Assert.That(mapping, Is.Not.Null);
            Assert.That(mapping.Count, Is.GreaterThan(0),
                "сохранённый XAML должен давать непустой рантайм-маппинг");

            string fullPath = Path.GetFullPath(path);
            bool bodyCovered = mapping.Any(entry =>
                entry.Key is Sequence
                && entry.Value != null
                && string.Equals(entry.Value.FileName, fullPath, StringComparison.OrdinalIgnoreCase));

            Assert.That(bodyCovered, Is.True,
                "тело Sequence после Save обязано быть покрыто SourceLocation, " +
                "иначе точка останова даст «Not found SourceLocation»");
        }

        [Test]
        public void LooseFlush_EmbedsDebugSymbolUsingLoadedFile()
        {
            string sub = Write("SubProcess1.xaml", ActivityXaml("RPA_1.SubProcess1", "<Sequence />"));
            string main = Write("Main.xaml",
                "<Activity x:Class=\"RPA_1.Main\" xmlns=\"" + ActivitiesNs + "\" xmlns:x=\"" + XamlNs +
                "\" xmlns:local=\"clr-namespace:RPA_1\">" +
                "<Sequence><local:SubProcess1 /></Sequence>" +
                "</Activity>");

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub });

            string flushed = null;
            var service = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);
            var builder = (ActivityBuilder)service.Load(main);
            service.Flush(builder);

            Assert.That(flushed, Is.Not.Null);
            Assert.That(flushed, Does.Contain("sads:DebugSymbol.Symbol"),
                "Flush loose-конструктора обязан вернуть отладочный символ");

            WorkflowSymbol symbol = WorkflowSymbol.Decode(SymbolValue(flushed));
            Assert.That(symbol.FileName, Is.EqualTo(Path.GetFullPath(main)));
            Assert.That(symbol.Symbols, Is.Not.Null);
            Assert.That(symbol.Symbols.Count, Is.GreaterThan(0));
        }

        [Test]
        public void LooseFlush_WithoutLoadedFile_DoesNotThrow()
        {
            string sub = Write("SubProcess1.xaml", ActivityXaml("RPA_1.SubProcess1", "<Sequence />"));
            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(new[] { sub });
            var builder = new ActivityBuilder { Name = "RPA_1.Main", Implementation = new Sequence() };

            string flushed = null;
            var service = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);

            Assert.DoesNotThrow(() => service.Flush(builder));
            Assert.That(flushed, Is.Not.Null);
        }

        private static ActivityBuilder SimpleBuilder()
        {
            return new ActivityBuilder
            {
                Name = "T.Main",
                Implementation = new Sequence
                {
                    Activities =
                    {
                        new WriteLine { Text = "hi" }
                    }
                }
            };
        }

        private string Write(string fileName, string content)
        {
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        private static string ActivityXaml(string xClass, string body)
        {
            return "<Activity x:Class=\"" + xClass + "\" xmlns=\"" + ActivitiesNs + "\" xmlns:x=\"" + XamlNs + "\">" +
                   body +
                   "</Activity>";
        }

        private static string SymbolValue(string xaml)
        {
            XDocument document = XDocument.Parse(xaml);
            return document.Descendants()
                .FirstOrDefault(e => e.Name.NamespaceName == DebugSymbolNs && e.Name.LocalName == "DebugSymbol.Symbol")
                ?.Value;
        }
    }
}
