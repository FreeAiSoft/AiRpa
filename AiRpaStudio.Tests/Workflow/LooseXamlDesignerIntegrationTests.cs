using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using AiRpaStudio.Tests.Design;
using NUnit.Framework;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Интеграционные тесты loose-XAML на эталоне <c>RPA-1</c> (шаги 4 и 6 плана
    /// LooseXamlTypeService): <c>Main.xaml</c> открывается без сборки и без <c>bin\*.dll</c>,
    /// вложенная ссылка видна как фасад, round-trip оставляет ссылки ссылками.
    /// </summary>
    [TestFixture]
    public class LooseXamlDesignerIntegrationTests
    {
        [Test]
        public void Rpa1_MainOpensLoose_WithSubProcessFacade()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            LooseOpenReport report = DesignerBenchProxy.Shared.LooseOpen(
                Path.Combine(rpa1, "Main.xaml"),
                xamlFiles);

            Assert.Multiple(() =>
            {
                Assert.That(report.Error, Is.Null.Or.Empty,
                    "loose-загрузка эталона упала." + Environment.NewLine + report.Description);
                Assert.That(report.LoadSucceeded, Is.True, report.Description);
                Assert.That(report.WorkflowFileItem, Is.EqualTo(Path.Combine(rpa1, "Main.xaml")),
                    "при IDocumentPersistenceService дизайнер сам не кладёт WorkflowFileItem — его выставляет загрузчик");
                Assert.That(report.FacadeNames, Does.Contain("RPA_1.SubProcess1"),
                    "вложенная ссылка local:SubProcess1 обязана стать фасадом DynamicActivity." +
                    Environment.NewLine + report.Description);
            });
        }

        [Test]
        public void Rpa1_MainRoundTrips_ReferencesStayReferences()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(xamlFiles);
            var service = new LooseXamlDocumentPersistenceService(registry);

            var builder = (System.Activities.ActivityBuilder)service.Load(Path.Combine(rpa1, "Main.xaml"));

            string flushed = null;
            var flushingService = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);
            flushingService.Flush(builder);

            Assert.Multiple(() =>
            {
                Assert.That(flushed, Does.Contain("<local:SubProcess1"),
                    "после Flush ссылка обязана остаться ссылкой local:SubProcess1");
                Assert.That(flushed, Does.Not.Contain("DynamicActivity"),
                    "фасады не должны «распаковываться» в DynamicActivity");
                Assert.That(flushed, Does.Contain("xmlns:local=\"clr-namespace:RPA_1\""),
                    "пространство имён local обязано быть объявлено для обратной сериализации");
            });
        }

        [Test]
        public void Rpa1_SubProcess1OpensLoose_WithFacades()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            LooseOpenReport report = DesignerBenchProxy.Shared.LooseOpen(
                Path.Combine(rpa1, "SubProcess1.xaml"),
                xamlFiles);

            Assert.Multiple(() =>
            {
                Assert.That(report.Error, Is.Null.Or.Empty,
                    "loose-загрузка SubProcess1 упала." + Environment.NewLine + report.Description);
                Assert.That(report.LoadSucceeded, Is.True, report.Description);
                Assert.That(report.FacadeNames, Does.Contain("RPA_1.SubProcess2"),
                    "вложенная ссылка local:SubProcess2 обязана стать фасадом DynamicActivity.");
                Assert.That(report.FacadeNames, Does.Contain("RPA_1.SubProcess3"),
                    "вложенная ссылка local:SubProcess3 обязана стать фасадом DynamicActivity.");
            });
        }

        [Test]
        public void Rpa1_SubProcess1RoundTrips_ReferencesStayReferences()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(xamlFiles);
            var service = new LooseXamlDocumentPersistenceService(registry);

            var builder = (System.Activities.ActivityBuilder)service.Load(Path.Combine(rpa1, "SubProcess1.xaml"));

            string flushed = null;
            var flushingService = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);
            flushingService.Flush(builder);

            Assert.Multiple(() =>
            {
                Assert.That(flushed, Does.Contain("<local:SubProcess2"),
                    "после Flush ссылка SubProcess2 обязана остаться ссылкой");
                Assert.That(flushed, Does.Contain("<local:SubProcess3"),
                    "после Flush ссылка SubProcess3 обязана остаться ссылкой");
                Assert.That(flushed, Does.Not.Contain("DynamicActivity"),
                    "фасады вложенного процесса не должны «распаковываться» в DynamicActivity");
            });
        }

        [Test]
        public void LoadActivityToDesigner_SkipsAutoSaveForWorkflowProject()
        {
            string method = MethodBody(ReadSource(@"AiRpaStudio\Helpers\LoadActivityToDesignerHelper.cs"), "LoadActivityToDesigner");

            Assert.That(method, Does.Contain("!IsInsideWorkflowProject(sourceFile)"),
                "эталонный WF-проект не должен перезаписываться штатной «нормализацией» после открытия");
        }

        [Test]
        public void StudioWorkflowDesignerHost_PassesLooseRegistryToLoader()
        {
            string source = ReadSource(@"AiRpaStudio\Core\Generation\StudioWorkflowDesignerHost.cs");

            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Contain("GetLooseRegistryFor(filePath)"),
                    "вкладка прямого открытия обязана получать loose-реестр проекта");
                Assert.That(source, Does.Contain("_load(filePath, registry)"),
                    "реестр обязан передаваться загрузчику, иначе вложенные ссылки резолвятся как CLR-типы и требуют DLL");
            });
        }

        private static string MethodBody(string source, string methodName)
        {
            Match match = Regex.Match(
                source,
                @"(override|internal|public|private|protected)[^\r\n]*\b" + Regex.Escape(methodName) + @"\s*\(");

            Assert.That(match.Success, Is.True, $"не найден метод {methodName}");
            return source.Substring(match.Index, Math.Min(8000, source.Length - match.Index));
        }

        private static string ReadSource(string relativePath)
        {
            string path = Path.Combine(FindRepositoryRoot(), relativePath);
            Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
            return File.ReadAllText(path);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new InvalidOperationException("AiRpa.sln не найден.");
            }

            return directory.FullName;
        }

        private static string FindRpa1()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new InvalidOperationException("AiRpa.sln не найден — RPA-1 негде искать.");
            }

            string rpa1 = Path.Combine(directory.FullName, "RPA-1");
            Assert.That(Directory.Exists(rpa1), Is.True, "нет эталонного проекта RPA-1: " + rpa1);
            return rpa1;
        }
    }
}
