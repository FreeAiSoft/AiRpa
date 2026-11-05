using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.AgentTools;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Core.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;
using RPA.Chat.Services.Workflow;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-06: отчёт валидации процесса (<c>workflow_validate</c>).
    /// <para>
    /// Проверяется главное требование карточки: вердикт «ошибок нет» выдаётся только после реально
    /// выполненной валидации WF, ошибки приходят с адресами, по которым агент правит, чтение файла
    /// не смешивается с валидацией, а неизвестный адрес — диагностика, а не пустой зелёный отчёт.
    /// Всё headless: отчёт строится над структурой процесса, без конструктора и без UI.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowValidationReportTests
    {
        [Test]
        public void Report_CountsErrorsAndWarnings()
        {
            WorkflowOutline outline = SampleOutline();
            outline.AddError("root/Activities[0]", "Assign: значение To обязательно", "wf.validate", "To");
            outline.AddError(string.Empty, "Sequence: нет ни одной активности", "wf.validate");
            outline.AddWarning("root/Then", "WriteLine: текст пуст", "wf.validate", "Text");
            outline.MarkValidationAvailable();

            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline);

            Assert.Multiple(() =>
            {
                Assert.That(report.Available, Is.True, "валидация выполнена — отчёт обязан это утверждать");
                Assert.That(report.Errors, Is.EqualTo(2));
                Assert.That(report.Warnings, Is.EqualTo(1));
                Assert.That(report.TotalIssues, Is.EqualTo(3));
                Assert.That(report.Ok, Is.False, "при ошибках вердикт «ошибок нет» запрещён");
                Assert.That(report.Source, Is.EqualTo(WorkflowOutlineSource.File));
                Assert.That(report.SourceName, Is.EqualTo("файл"));
            });
        }

        [Test]
        public void Report_SeparatesLoadIssuesFromValidationIssues()
        {
            var outline = new WorkflowOutline(@"C:\robot\Main.xaml") { Source = WorkflowOutlineSource.File };
            outline.AddError(string.Empty, "xaml.path: файл процесса не найден", "xaml.path");

            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline);

            Assert.Multiple(() =>
            {
                Assert.That(report.LoadIssues.Select(issue => issue.Code), Does.Contain("xaml.path"));
                Assert.That(report.ValidationIssues.Select(issue => issue.Code), Does.Not.Contain("xaml.path"),
                    "ошибка чтения не должна выглядеть как замечание валидации");
                Assert.That(report.Available, Is.False);
                Assert.That(report.UnavailableReason, Does.Contain("не прочитан"));
                Assert.That(WorkflowValidationReport.IsLoadIssue(
                    new WorkflowOutlineIssue(string.Empty, WorkflowOutlineIssueSeverity.Error, "x", "xaml.load")), Is.True);
                Assert.That(WorkflowValidationReport.IsValidationIssue(
                    new WorkflowOutlineIssue("root", WorkflowOutlineIssueSeverity.Error, "x", "wf.validate")), Is.True);
            });
        }

        [Test]
        public void Report_ValidationFailed_MarkedUnavailable()
        {
            WorkflowOutline outline = SampleOutline();
            outline.AddWarning(string.Empty, "валидация WF не выполнена: таймаут", "xaml.validate");
            outline.MarkValidationUnavailable("валидация WF не выполнена: таймаут");

            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline);

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.True, "структура прочитана");
                Assert.That(report.Available, Is.False,
                    "сбой ActivityValidationServices.Validate — это «неизвестно», а не «ошибок нет»");
                Assert.That(report.Ok, Is.False);
                Assert.That(report.UnavailableReason, Does.Contain("валидация WF не выполнена"));

                string text = WorkflowValidationText.Describe(report);
                Assert.That(text, Does.Contain("валидация: не выполнена"));
                Assert.That(text, Does.Not.Contain("ошибок нет:"), "недоступная валидация не бывает зелёной");
            });
        }

        [Test]
        public void Report_MissingFile_NotGreen()
        {
            string missing = Path.Combine(Path.GetTempPath(), "нет-" + Guid.NewGuid().ToString("N") + ".xaml");

            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(missing);
            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline);
            string text = WorkflowValidationText.Describe(report);

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.False);
                Assert.That(report.Available, Is.False);
                Assert.That(report.Ok, Is.False);
                Assert.That(report.LoadIssues, Is.Not.Empty, "причина обязана прийти списком замечаний чтения");
                Assert.That(text, Does.Contain("валидация: не выполнена"));
                Assert.That(text, Does.Contain("не найден"));
                Assert.That(text, Does.Not.Contain("ошибок нет:"));
            });
        }

        [Test]
        public void Report_FiltersByElementId()
        {
            WorkflowOutline outline = SampleOutline();
            outline.AddError("root/Activities[0]", "Assign: нет To", "wf.validate", "To");
            outline.AddError("root/Activities[0]/Then", "WriteLine: нет текста", "wf.validate", "Text");
            outline.AddError("root/Then", "WriteLine: чужой Then", "wf.validate");
            outline.AddWarning("root/Then2", "WriteLine: чужой Then2", "wf.validate");
            outline.AddWarning(string.Empty, "процесс без x:Class", "wf.validate");
            outline.MarkValidationAvailable();

            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline, "root/Activities[0]");

            Assert.Multiple(() =>
            {
                Assert.That(report.ScopeId, Is.EqualTo("root/Activities[0]"));
                Assert.That(report.Errors, Is.EqualTo(2), "сам элемент и его потомки");
                Assert.That(report.Warnings, Is.EqualTo(1), "замечание без адреса относится к процессу целиком и не скрывается");
                Assert.That(report.Issues.Select(issue => issue.ElementId),
                    Does.Not.Contain("root/Then").And.Not.Contain("root/Then2"),
                    "соседние адреса не должны попадать в область: root/Then ≠ root/Then2");
            });
        }

        [Test]
        public void Report_UnknownElementId_ReportsAddressNotFound()
        {
            WorkflowOutline outline = SampleOutline();
            outline.AddError("root/Activities[0]", "Assign: нет To", "wf.validate", "To");
            outline.MarkValidationAvailable();

            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline, "root/Activities[9]");

            Assert.Multiple(() =>
            {
                Assert.That(report.AddressNotFound, Is.True);
                Assert.That(report.Ok, Is.False, "неизвестный адрес — диагностика, а не «ошибок нет»");
                Assert.That(report.Issues, Is.Empty);
                Assert.That(report.KnownAddresses, Does.Contain("root/Activities[0]"), "подсказка со списком адресов");

                string text = WorkflowValidationText.Describe(report);
                Assert.That(text, Does.Contain("адрес не найден"));
                Assert.That(text, Does.Not.Contain("ошибок нет:"));
            });
        }

        [Test]
        public void Report_LimitsIssues_AndCountsTheRest()
        {
            WorkflowOutline outline = SampleOutline();
            for (int index = 0; index < 6; index++)
            {
                outline.AddError("root/Activities[0]", "ошибка " + index, "wf.validate");
            }

            outline.MarkValidationAvailable();

            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline, null, maxIssues: 2);

            Assert.Multiple(() =>
            {
                Assert.That(report.Issues.Count, Is.EqualTo(2));
                Assert.That(report.Errors, Is.EqualTo(6), "счётчик считает все, а не только показанные");
                Assert.That(report.TotalIssues, Is.EqualTo(6));
                Assert.That(report.HiddenIssues, Is.EqualTo(4));

                string text = WorkflowValidationText.Describe(report);
                Assert.That(text, Does.Contain("ещё 4 замечаний не показаны"));
                Assert.That(text, Does.Contain("всего 6"));
            });
        }

        [Test]
        public void Report_NullOutline_IsUnavailableNotGreen()
        {
            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(null);

            Assert.Multiple(() =>
            {
                Assert.That(report.Available, Is.False);
                Assert.That(report.Ok, Is.False);
                Assert.That(report.UnavailableReason, Is.Not.Null.And.Not.Empty);
            });
        }

        /// <summary>
        /// Единство с <c>AG-09</c>: адреса новых ошибок в ответе правки и в отчёте валидации совпадают,
        /// иначе агент правит один элемент, а инструмент показывает другой.
        /// </summary>
        [Test]
        public void Report_AndEditDelta_UseTheSameAddresses()
        {
            WorkflowOutline before = SampleOutline();
            before.AddError("root/Activities[0]", "Assign: нет To", "wf.validate", "To");
            before.MarkValidationAvailable();

            WorkflowOutline after = SampleOutline();
            after.AddError("root/Activities[0]", "Assign: нет To", "wf.validate", "To");
            after.AddError("root/Then", "WriteLine: нет Text", "wf.validate", "Text");
            after.MarkValidationAvailable();

            WorkflowValidationDelta delta = WorkflowValidationDelta.Compare(before, after);
            WorkflowValidationReport report = WorkflowValidationReport.FromOutline(after, "root/Then");

            Assert.Multiple(() =>
            {
                Assert.That(delta.NewErrors.Count, Is.EqualTo(1), "новая ошибка только одна");
                Assert.That(report.Issues.Count, Is.EqualTo(1));
                Assert.That(report.Issues.Single().ElementId, Is.EqualTo(delta.NewErrors.Single().ElementId),
                    "адрес правки и адрес валидации обязаны совпадать");
                Assert.That(report.Issues.Single().Message, Is.EqualTo(delta.NewErrors.Single().Message));
            });
        }

        /// <summary>Структура с четырьмя адресами: элемент с потомком и два похожих соседа (<c>Then</c>/<c>Then2</c>).</summary>
        private static WorkflowOutline SampleOutline()
        {
            var outline = new WorkflowOutline(@"C:\robot\Main.xaml")
            {
                Ok = true,
                Source = WorkflowOutlineSource.File,
                ActivityCount = 4
            };

            var root = new WorkflowOutlineNode(
                "root", string.Empty, 0, string.Empty, "System.Activities.Statements.Sequence", "Main");

            var assign = new WorkflowOutlineNode(
                "root/Activities[0]", "root", 0, "Activities[0]", "System.Activities.Statements.Assign", "Assign 1");
            assign.AddChild(new WorkflowOutlineNode(
                "root/Activities[0]/Then", "root/Activities[0]", 0, "Then", "System.Activities.Statements.WriteLine", "Log inner"));

            root.AddChild(assign);
            root.AddChild(new WorkflowOutlineNode(
                "root/Activities[0]/Then2", "root/Activities[0]", 1, "Then2", "System.Activities.Statements.WriteLine", "Log inner 2"));
            root.AddChild(new WorkflowOutlineNode(
                "root/Then", "root", 1, "Then", "System.Activities.Statements.WriteLine", "Log then"));
            root.AddChild(new WorkflowOutlineNode(
                "root/Then2", "root", 2, "Then2", "System.Activities.Statements.WriteLine", "Log then2"));

            outline.Root = root;

            return outline;
        }
    }

    /// <summary>
    /// AG-06: контракт инструмента <c>workflow_validate</c> — read-only, доступен в Plan, аргументы
    /// проверяются до вызова службы, отсутствие службы объясняется, а не бросается исключением.
    /// </summary>
    [TestFixture]
    public class WorkflowValidateToolTests
    {
        [Test]
        public void Registry_ContainsWorkflowValidateTool()
        {
            AgentToolRegistry registry = CreateFactory(NullWorkflowOutlineService.Instance).CreateRegistry();

            IAgentTool tool = registry.Find("workflow_validate");

            Assert.Multiple(() =>
            {
                Assert.That(tool, Is.Not.Null, "нет инструмента workflow_validate");
                Assert.That(tool.Mutates, Is.False, "валидация ничего не меняет в процессе");
                Assert.That(tool.Permission, Is.EqualTo(AgentPermissions.ReadFiles));
                Assert.That(registry.ForMode(AgentMode.Plan, AgentPermissions.All).Select(item => item.Name),
                    Does.Contain("workflow_validate"), "вердикт нужен и в режиме Plan");
                Assert.That(registry.ForMode(AgentMode.Act, AgentPermissions.All).Select(item => item.Name),
                    Does.Contain("workflow_validate"));
            });
        }

        [Test]
        public void Tool_RequiresPath()
        {
            AgentToolRegistry registry = CreateFactory(new FakeOutlineService(SampleOutline())).CreateRegistry();

            Assert.That(Run(registry, "workflow_validate", new JObject()),
                Does.Contain("Missing or empty \"path\""));
        }

        [Test]
        public void Tool_RejectsInvalidMaxIssues()
        {
            AgentToolRegistry registry = CreateFactory(new FakeOutlineService(SampleOutline())).CreateRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(Run(registry, "workflow_validate", new JObject
                {
                    ["path"] = "Main.xaml",
                    ["max_issues"] = -1
                }), Does.Contain("Invalid \"max_issues\""));

                Assert.That(Run(registry, "workflow_validate", new JObject
                {
                    ["path"] = "Main.xaml",
                    ["max_issues"] = "много"
                }), Does.Contain("Invalid \"max_issues\""));
            });
        }

        [Test]
        public void Tool_WithoutService_ExplainsInsteadOfThrowing()
        {
            AgentToolRegistry registry = CreateFactory(NullWorkflowOutlineService.Instance).CreateRegistry();

            Assert.That(Run(registry, "workflow_validate", new JObject { ["path"] = "Main.xaml" }),
                Does.Contain("Workflow outline service is unavailable"));
        }

        [Test]
        public void Tool_PassesElementIdAndDoesNotReadArguments()
        {
            WorkflowOutline outline = SampleOutline();
            outline.AddError("root/Activities[0]", "Assign: нет To", "wf.validate", "To");
            outline.MarkValidationAvailable();

            var service = new FakeOutlineService(outline);
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "workflow_validate", new JObject
            {
                ["path"] = "Main.xaml",
                ["elementId"] = "root/Activities[0]"
            });

            Assert.Multiple(() =>
            {
                Assert.That(service.LastPath, Does.EndWith("Main.xaml"));
                Assert.That(service.LastIncludeArguments, Is.False,
                    "аргументы не нужны для вердикта и занимают контекст модели");
                Assert.That(service.LastDepth, Is.EqualTo(0), "нужно всё дерево: адрес элемента может быть вложенным");
            });
        }

        [Test]
        public void Tool_ReportsDesignerSource()
        {
            WorkflowOutline outline = SampleOutline();
            outline.Source = WorkflowOutlineSource.Designer;
            outline.MarkValidationAvailable();

            AgentToolRegistry registry = CreateFactory(new FakeOutlineService(outline)).CreateRegistry();

            string result = Run(registry, "workflow_validate", new JObject { ["path"] = "Main.xaml" });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("источник: конструктор"),
                    "правки в конструкторе ещё не сохранены — агент обязан видеть, что проверено именно там");
                Assert.That(result, Does.Contain("ошибок нет:"));
            });
        }

        [Test]
        public void Tool_ValidFixture_SaysNoErrors()
        {
            string path = Path.Combine(FixturesDirectory, "sequence.xaml");
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string result = Run(registry, "workflow_validate", new JObject { ["path"] = path });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("валидация: ошибок 0"));
                Assert.That(result, Does.Contain("источник: файл"));
                Assert.That(result, Does.Contain("ошибок нет:"));
                Assert.That(result, Does.Contain("workflow_validate"), "подсказка про цикл правка → валидация");
            });
        }

        [Test]
        public void Tool_BrokenFixture_ListsIssuesWithAddresses()
        {
            string path = Path.Combine(FixturesDirectory, "sequence-broken.xaml");
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string result = Run(registry, "workflow_validate", new JObject { ["path"] = path });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("ошибка root/Activities[0]"),
                    "без адреса агент не знает, какой элемент править: " + result);
                Assert.That(result, Does.Contain("код: wf.validate"));
                Assert.That(result, Does.Not.Contain("ошибок нет:"), "у сломанного процесса зелёного вердикта нет");
                Assert.That(result, Does.Not.Contain("валидация: не выполнена"), "валидация выполнена, ошибки реальные");
            });
        }

        [Test]
        public void Tool_BrokenFixture_ElementFilter_NarrowsReport()
        {
            string path = Path.Combine(FixturesDirectory, "sequence-broken.xaml");
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string all = Run(registry, "workflow_validate", new JObject { ["path"] = path });
            string scoped = Run(registry, "workflow_validate", new JObject
            {
                ["path"] = path,
                ["elementId"] = "root/Activities[0]"
            });

            Assert.Multiple(() =>
            {
                Assert.That(scoped, Does.Contain("область: root/Activities[0]"));
                Assert.That(scoped, Does.Contain("ошибка root/Activities[0]"));
                Assert.That(all, Does.Contain("ошибка"), "полный отчёт тоже обязан содержать ошибки");
            });
        }

        [Test]
        public void Tool_UnknownElementId_ExplainsInsteadOfGreenReport()
        {
            string path = Path.Combine(FixturesDirectory, "sequence.xaml");
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string result = Run(registry, "workflow_validate", new JObject
            {
                ["path"] = path,
                ["elementId"] = "root/Activities[99]"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("адрес не найден"));
                Assert.That(result, Does.Contain("известные адреса"));
                Assert.That(result, Does.Not.Contain("ошибок нет:"));
            });
        }

        [Test]
        public void Tool_MissingFile_ExplainsInsteadOfThrowing()
        {
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string result = Run(registry, "workflow_validate", new JObject
            {
                ["path"] = Path.Combine(Path.GetTempPath(), "нет-" + Guid.NewGuid().ToString("N") + ".xaml")
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("валидация: не выполнена"));
                Assert.That(result, Does.Contain("не найден"));
                Assert.That(result, Does.Not.Contain("ошибок нет:"));
            });
        }

        [Test]
        public void Tool_MalformedFile_ReportsLoadIssueOnly()
        {
            string path = Path.Combine(FixturesDirectory, "malformed.xaml");
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string result = Run(registry, "workflow_validate", new JObject { ["path"] = path });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("валидация: не выполнена"),
                    "битый XAML — это «не проверено», а не «ошибок нет»: " + result);
                Assert.That(result, Does.Contain("замечания чтения"));
                Assert.That(result, Does.Contain("xaml.load"));
                Assert.That(result, Does.Not.Contain("ошибок нет:"));
            });
        }

        private static WorkflowOutline SampleOutline()
        {
            var outline = new WorkflowOutline(@"C:\robot\Main.xaml")
            {
                Ok = true,
                Source = WorkflowOutlineSource.File,
                ActivityCount = 2
            };

            var root = new WorkflowOutlineNode(
                "root", string.Empty, 0, string.Empty, "System.Activities.Statements.Sequence", "Main");
            root.AddChild(new WorkflowOutlineNode(
                "root/Activities[0]", "root", 0, "Activities[0]", "System.Activities.Statements.WriteLine", "Log"));

            outline.Root = root;

            return outline;
        }

        private static string Run(AgentToolRegistry registry, string toolName, JObject arguments)
        {
            IAgentTool tool = registry.Find(toolName);
            Assert.That(tool, Is.Not.Null, "нет инструмента " + toolName);

            return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static AgentToolFactory CreateFactory(IWorkflowOutlineService outline)
        {
            return new AgentToolFactory(
                new StubAiProvider(),
                () => Path.GetTempPath(),
                NullStudioDocumentService.Instance,
                new FakeBuildFeedbackService(),
                (question, options, ct) => Task.FromResult("answer"),
                (systemPrompt, task, ct) => Task.FromResult("sub-result"),
                enableSubagents: false,
                subagentMaxDepth: 2,
                getDocsDirectory: null,
                activityCatalog: null,
                workflowOutline: outline);
        }

        private static string FixturesDirectory =>
            Path.Combine(RepositoryRoot(), "AiRpaStudio.Tests", "Fixtures", "Workflow");

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");

            return directory.FullName;
        }

        private sealed class FakeOutlineService : IWorkflowOutlineService
        {
            private readonly Func<WorkflowOutline> _outline;

            public FakeOutlineService(WorkflowOutline outline)
            {
                _outline = () => outline;
            }

            public string LastPath { get; private set; }

            public int LastDepth { get; private set; }

            public bool LastIncludeArguments { get; private set; }

            public WorkflowOutline GetOutline(string path, int depth = 0, bool includeArguments = true)
            {
                LastPath = path;
                LastDepth = depth;
                LastIncludeArguments = includeArguments;

                return _outline();
            }
        }

        private sealed class FakeBuildFeedbackService : IBuildFeedbackService
        {
            public Task BuildAsync(string configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());

            public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);
        }
    }

    /// <summary>AG-06: формат ответа — сводка, вердикт и подсказка; «недоступно» ≠ «ошибок нет».</summary>
    [TestFixture]
    public class WorkflowValidationTextTests
    {
        [Test]
        public void Text_Summary_ContainsCountsAndSource()
        {
            var outline = new WorkflowOutline(@"C:\robot\Main.xaml")
            {
                Ok = true,
                Source = WorkflowOutlineSource.Designer,
                ActivityCount = 3
            };

            outline.Root = new WorkflowOutlineNode(
                "root", string.Empty, 0, string.Empty, "System.Activities.Statements.Sequence", "Main");
            outline.AddError("root", "Sequence: ошибка", "wf.validate", "DisplayName");
            outline.AddWarning("root", "Sequence: предупреждение", "wf.validate");
            outline.MarkValidationAvailable();

            string text = WorkflowValidationText.Describe(WorkflowValidationReport.FromOutline(outline));

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("валидация: ошибок 1, предупреждений 1; источник: конструктор"));
                Assert.That(text, Does.Contain("процесс: C:\\robot\\Main.xaml"));
                Assert.That(text, Does.Contain("  - ошибка root: Sequence: ошибка (код: wf.validate, свойство: DisplayName)"),
                    "адрес, код и свойство печатаются в одной строке: " + text);
                Assert.That(text, Does.Contain("workflow_validate"));
            });
        }

        [Test]
        public void Text_EmptyState_DistinguishesNoErrorsFromUnavailable()
        {
            var valid = new WorkflowOutline(@"C:\robot\Main.xaml")
            {
                Ok = true,
                Source = WorkflowOutlineSource.File,
                Root = new WorkflowOutlineNode("root", string.Empty, 0, string.Empty, "System.Activities.Statements.Sequence", "Main")
            };
            valid.MarkValidationAvailable();

            var failed = new WorkflowOutline(@"C:\robot\Main.xaml")
            {
                Ok = true,
                Source = WorkflowOutlineSource.File,
                Root = new WorkflowOutlineNode("root", string.Empty, 0, string.Empty, "System.Activities.Statements.Sequence", "Main")
            };
            failed.MarkValidationUnavailable("валидация WF не выполнена: внутренняя ошибка");

            string green = WorkflowValidationText.Describe(WorkflowValidationReport.FromOutline(valid));
            string grey = WorkflowValidationText.Describe(WorkflowValidationReport.FromOutline(failed));

            Assert.Multiple(() =>
            {
                Assert.That(green, Does.Contain("ошибок нет:"), "выполненная валидация без ошибок — зелёный вердикт");
                Assert.That(grey, Does.Not.Contain("ошибок нет:"), "невыполненная валидация не бывает зелёной");
                Assert.That(grey, Does.Contain("валидация: не выполнена"));
                Assert.That(grey, Does.Contain("≠"), "агент должен прочитать, что это «неизвестно»");
            });
        }
    }
}
