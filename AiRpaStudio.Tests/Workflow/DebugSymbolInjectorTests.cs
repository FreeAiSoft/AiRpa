using System;
using System.Activities;
using System.Activities.Debugger.Symbol;
using System.Activities.Statements;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using RPA.Core.Generation;
using RPA.Helpers;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Регрессия «точка останова не срабатывает»: сериализаторы должны вписывать
    /// <c>sads:DebugSymbol.Symbol</c> в тело процесса, иначе <see cref="RuntimeSourceLocationMap"/>
    /// не находит тело и отладка молча пропускает точку («Not found SourceLocation»).
    /// </summary>
    [TestFixture]
    public class DebugSymbolInjectorTests
    {
        private const string DebugSymbolNs = "http://schemas.microsoft.com/netfx/2010/xaml/activities/debugger";

        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "airpa_sym_" + Guid.NewGuid().ToString("N"));
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
        public void Inject_EmbedsSymbolIntoBody()
        {
            string path = Path.Combine(_tempDir, "Main.xaml");
            string injected = DebugSymbolInjector.Inject(WorkflowXamlSerializer.Serialize(SimpleBuilder()), path);

            Assert.That(injected, Does.Contain("xmlns:sads=\"" + DebugSymbolNs + "\""),
                "пространство имён sads обязано появиться в корне");
            Assert.That(injected, Does.Contain("mc:Ignorable=\"sap sap2010 sads\""),
                "префикс sads обязан попасть в mc:Ignorable");

            Assert.That(SymbolValue(injected), Is.Not.Null.And.Not.Empty,
                "символ обязан быть вписан в тело процесса");
        }

        [Test]
        public void Inject_SymbolDecodesToFilePathAndNonEmptySymbols()
        {
            string path = Path.Combine(_tempDir, "Main.xaml");
            string injected = DebugSymbolInjector.Inject(WorkflowXamlSerializer.Serialize(SimpleBuilder()), path);

            WorkflowSymbol symbol = WorkflowSymbol.Decode(SymbolValue(injected));

            Assert.That(symbol.FileName, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(symbol.Symbols, Is.Not.Null);
            Assert.That(symbol.Symbols.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Inject_IsIdempotent()
        {
            string path = Path.Combine(_tempDir, "Main.xaml");
            string xaml = WorkflowXamlSerializer.Serialize(SimpleBuilder());

            string once = DebugSymbolInjector.Inject(xaml, path);
            string twice = DebugSymbolInjector.Inject(once, path);

            Assert.That(SymbolCount(twice), Is.EqualTo(1),
                "повторный Inject не должен плодить несколько элементов символа");
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

        private static string SymbolValue(string xaml)
        {
            XDocument document = XDocument.Parse(xaml);
            return document.Descendants()
                .FirstOrDefault(e => e.Name.NamespaceName == DebugSymbolNs && e.Name.LocalName == "DebugSymbol.Symbol")
                ?.Value;
        }

        private static int SymbolCount(string xaml)
        {
            XDocument document = XDocument.Parse(xaml);
            return document.Descendants()
                .Count(e => e.Name.NamespaceName == DebugSymbolNs && e.Name.LocalName == "DebugSymbol.Symbol");
        }
    }
}
