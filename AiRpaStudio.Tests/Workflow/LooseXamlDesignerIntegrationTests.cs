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
        public void Rpa1_CodeTestOpensLoose_WithCodeType()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);

            LooseOpenReport report = DesignerBenchProxy.Shared.LooseOpen(
                Path.Combine(rpa1, "CodeTest.xaml"),
                xamlFiles);

            Assert.Multiple(() =>
            {
                Assert.That(report.Error, Is.Null.Or.Empty,
                    "loose-загрузка CodeTest.xaml упала." + Environment.NewLine + report.Description);
                Assert.That(report.LoadSucceeded, Is.True, report.Description);
            });
        }

        [Test]
        public void Rpa1_CodeTestRoundTrips_TargetTypeStaysLocalCode()
        {
            string rpa1 = FindRpa1();
            string[] xamlFiles = Directory.GetFiles(rpa1, "*.xaml", SearchOption.TopDirectoryOnly);
            string csproj = Directory.GetFiles(rpa1, "*.csproj", SearchOption.TopDirectoryOnly).First();

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(xamlFiles);
            RPA.ProjectModel.ProjectLoadResult loaded = RPA.ProjectModel.ProjectLoader.Load(csproj);
            registry.CodeTypes = loaded?.Project == null ? null : ProjectCodeTypeCatalog.Build(loaded.Project);

            var service = new LooseXamlDocumentPersistenceService(registry);
            var builder = (System.Activities.ActivityBuilder)service.Load(Path.Combine(rpa1, "CodeTest.xaml"));

            string flushed = null;
            var flushingService = new LooseXamlDocumentPersistenceService(registry, text => flushed = text);
            flushingService.Flush(builder);

            Assert.Multiple(() =>
            {
                Assert.That(flushed, Does.Contain("TargetType=\"local:Code\""),
                    "после Flush TargetType обязан остаться local:Code, а не clr-namespace с синтетической сборкой");
                Assert.That(flushed, Does.Contain("xmlns:local=\"clr-namespace:RPA_1\""),
                    "xmlns:local обязан быть объявлен без assembly");
                Assert.That(flushed, Does.Not.Contain("RpaLooseCode_"),
                    "в выводе не должно оставаться синтетической сборки code-behind");
            });

            string roundTrippedPath = Path.Combine(Path.GetTempPath(), "airpa_code_roundtrip_" + Guid.NewGuid().ToString("N") + ".xaml");
            File.WriteAllText(roundTrippedPath, flushed);


            try
            {
                object root = service.Load(roundTrippedPath);
                Assert.That(root, Is.InstanceOf<System.Activities.ActivityBuilder>(),
                    "round-trip-вывод обязан снова грузиться");

                System.Activities.ActivityBuilder roundTripBuilder = (System.Activities.ActivityBuilder)root;
                System.Activities.Statements.InvokeMethod invoke = roundTripBuilder.Implementation
                    as System.Activities.Statements.InvokeMethod;

                if (invoke == null)
                {
                    invoke = System.Activities.WorkflowInspectionServices
                        .GetActivities(roundTripBuilder.Implementation)
                        .OfType<System.Activities.Statements.InvokeMethod>()
                        .Single();
                }

                Assert.That(invoke, Is.Not.Null, "после round-trip InvokeMethod обязан присутствовать в корне документа");
                Assert.That(invoke.TargetType, Is.Not.Null);
                Assert.That(invoke.TargetType.FullName, Is.EqualTo("RPA_1.Code"),
                    "после round-trip TargetType обязан снова резолвиться в code-behind тип");
            }
            finally
            {
                if (File.Exists(roundTrippedPath))
                {
                    File.Delete(roundTrippedPath);
                }
            }
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
                Assert.That(source, Does.Contain("_load(filePath, registry, xamlText)"),
                    "реестр обязан передаваться загрузчику, иначе вложенные ссылки резолвятся как CLR-типы и требуют DLL");
            });
        }

        [Test]
        public void ActivityRunner_PassesLooseRegistryToDesignerOnRun()
        {
            string method = MethodBody(ReadSource(@"AiRpaStudio\Core\ActivityRunner.cs"), "Start");

            Assert.Multiple(() =>
            {
                Assert.That(method, Does.Contain("LoadActivityToDesignerHelper.LoadActivityToDesigner("));
                Assert.That(method, Does.Contain("GetLooseRegistryFor(pathToXaml)"),
                    "при запуске WF-проекта дизайнер обязан открываться loose-путём с реестром вложенных процессов, даже если вкладка ещё не открыта");
            });
        }

        [Test]
        public void WorkflowDesignerEx_SchedulesNamespaceFlushOnLooseEdit()
        {
            string source = ReadSource(@"AiRpaStudio\Core\WorkflowDesignerEx.cs");

            string onEdit = MethodBody(source, "OnEditingScopeCompleted");
            Assert.That(onEdit, Does.Contain("ScheduleNamespaceFlush()"),
                "после эффективной правки модели дизайнер обязан актуализировать XAML (добавить xmlns)");

            string schedule = MethodBody(source, "ScheduleNamespaceFlush");
            Assert.Multiple(() =>
            {
                Assert.That(schedule, Does.Contain("LooseTypeRegistry == null"),
                    "отложенный Flush нужен только для loose-пути — штатный путь сериализует сам");
                Assert.That(schedule, Does.Contain("Flush()"),
                    "дизайнер обязан сериализовать буфер после правки модели");
            });
        }


        [Test]
        public void LooseActivityXamlSchemaContext_ChecksCodeCatalogAfterFacades()
        {
            string method = MethodBody(
                ReadSource(@"AiRpaStudio\Core\Workflow\LooseActivityXamlSchemaContext.cs"),
                "GetXamlType");

            int facadeCheck = method.IndexOf("_registry.TryGet(", StringComparison.Ordinal);
            int codeCatalogCheck = method.IndexOf("_registry.TryGetCodeType(", StringComparison.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(facadeCheck, Is.GreaterThanOrEqualTo(0),
                    "резолв фасада (_registry.TryGet) обязан остаться первым шагом GetXamlType");
                Assert.That(codeCatalogCheck, Is.GreaterThanOrEqualTo(0),
                    "ветка резолва code-behind (_registry.TryGetCodeType) обязана существовать — её снятие ломает local:Code");
                Assert.That(facadeCheck, Is.LessThan(codeCatalogCheck),
                    "code-каталог обязан проверяться ПОСЛЕ фасадов: XAML-фасад при совпадении имени должен побеждать");
                Assert.That(method, Does.Contain("return new XamlType(codeType, this);"),
                    "code-behind тип обязан резолвиться в настоящий System.Type через new XamlType(codeType, this)");
            });
        }

        [Test]
        public void LooseXamlProjectService_AttachesCodeTypesToRegistry()
        {
            string method = MethodBody(
                ReadSource(@"AiRpaStudio\Core\Workflow\LooseXamlProjectService.cs"),
                "BuildRegistry");

            int scan = method.IndexOf("WorkflowTypeRegistry.Scan(EnumerateXamlFiles(project))", StringComparison.Ordinal);
            int attach = method.IndexOf("registry.CodeTypes = ProjectCodeTypeCatalog.Build(project)", StringComparison.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(scan, Is.GreaterThanOrEqualTo(0),
                    "реестр обязан строиться сканом XAML-файлов проекта");
                Assert.That(attach, Is.GreaterThanOrEqualTo(0),
                    "реестр обязан прикреплять code-каталог: иначе local:Code не резолвится в настоящий Type");
                Assert.That(scan, Is.LessThan(attach),
                    "code-каталог прикрепляется ПОСЛЕ скана фасадов");
            });
        }

        [Test]
        public void LoadActivityToDesignerHelper_UsesCatalogCarryingRegistryWithoutNewBranches()
        {
            string method = MethodBody(
                ReadSource(@"AiRpaStudio\Helpers\LoadActivityToDesignerHelper.cs"),
                "LoadActivityToDesigner");

            Assert.Multiple(() =>
            {
                Assert.That(method, Does.Contain("if (looseTypeRegistry != null)"),
                    "loose-путь выбирается по registry, а не по отдельному признаку code-каталога");
                Assert.That(method, Does.Contain("LooseXamlDesignerLoader.Open(workflowDesignerEx, sourceFile, looseTypeRegistry, xamlText)"),
                    "loose-загрузчик получает реестр (с CodeTypes) без новой сигнатуры");
                Assert.That(method, Does.Not.Contain("CodeTypes"),
                    "для code-behind резолва не должно появляться отдельных веток: каталог едет внутри registry");
                Assert.That(method, Does.Not.Contain("ProjectCodeTypeCatalog"),
                    "хелпер не должен строить/трогать code-каталог напрямую — это делает LooseXamlProjectService");
            });
        }

        [Test]
        public void DebuggerMappingsHelper_UsesCatalogCarryingSchemaWithoutNewBranches()
        {
            string method = MethodBody(
                ReadSource(@"AiRpaStudio\Helpers\DebuggerMappingsHelper.cs"),
                "DeserializeFreshDocument");

            Assert.Multiple(() =>
            {
                Assert.That(method, Does.Contain("registry != null"),
                    "loose-схема выбирается по registry, а не по отдельному признаку code-каталога");
                Assert.That(method, Does.Contain("new LooseActivityXamlSchemaContext(registry)"),
                    "десериализация обязана идти через LooseActivityXamlSchemaContext: она резолвит и фасады, и CodeTypes");
                Assert.That(method, Does.Not.Contain("CodeTypes"),
                    "в хелпере не должно появляться отдельных веток code-behind: каталог едет внутри registry");
                Assert.That(method, Does.Not.Contain("TryGetCodeType"),
                    "хелпер не должен резолвить code-типы напрямую — это делает схема");
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
