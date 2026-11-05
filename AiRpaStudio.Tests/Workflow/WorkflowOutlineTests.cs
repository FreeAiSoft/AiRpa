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
using RPA.Core.Generation;
using RPA.Core.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-03: структура процесса (<c>workflow_outline</c>) — дерево активностей вместо сырого XAML.
    /// <para>
    /// Проверяется то, на что опирается агент: стабильные адреса узлов (они же адреса правок),
    /// аргументы и переменные, привязка ошибок WF к узлам и поведение на битых файлах.
    /// Всё headless: ни конструктора, ни диспетчера, ни <c>MessageBox</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowOutlineServiceTests
    {
        private static readonly string[] ValidFixtures =
        {
            "sequence.xaml",
            "if.xaml",
            "trycatch.xaml",
            "flowchart.xaml",
            "variables.xaml"
        };

        [Test]
        public void Outline_SequenceFixture_ReadsTree()
        {
            WorkflowOutline outline = Read("sequence.xaml");

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.True, DescribeIssues(outline));
                Assert.That(outline.Root, Is.Not.Null);
                Assert.That(outline.Root.Id, Is.EqualTo("root"));
                Assert.That(outline.Root.ShortTypeName, Is.EqualTo("Sequence"));
                Assert.That(outline.Root.TypeName, Is.EqualTo("System.Activities.Statements.Sequence"));
                Assert.That(outline.Root.DisplayName, Is.EqualTo("Fixture: sequence"));
                Assert.That(outline.ActivityCount, Is.EqualTo(4), "Sequence + WriteLine + Sequence + WriteLine");
                Assert.That(outline.Root.ChildCount, Is.EqualTo(2));
            });
        }

        [Test]
        public void Outline_BuildsStablePathIds()
        {
            WorkflowOutline outline = Read("sequence.xaml");

            WorkflowOutlineNode first = outline.Find("root/Activities[0]");
            WorkflowOutlineNode nested = outline.Find("root/Activities[1]/Activities[0]");

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(first.ShortTypeName, Is.EqualTo("WriteLine"));
                Assert.That(first.ParentId, Is.EqualTo("root"));
                Assert.That(first.Index, Is.EqualTo(0));
                Assert.That(first.ContainerName, Is.EqualTo("Activities[0]"));

                Assert.That(nested, Is.Not.Null, "вложенная активность адресуется полным путём");
                Assert.That(nested.ShortTypeName, Is.EqualTo("WriteLine"));
                Assert.That(nested.DisplayName, Is.EqualTo("Log second step"));
            });
        }

        /// <summary>
        /// Адреса структуры и адреса модель-ридера обязаны совпадать: по ним будут выполняться правки
        /// (<c>AG-04</c>), и расхождение превратило бы outline в справочник «куда-то не туда».
        /// </summary>
        [TestCaseSource(nameof(ValidFixtures))]
        public void Outline_IdsMatchModelReader(string fileName)
        {
            string path = FixturePath(fileName);

            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(path);
            WorkflowSnapshot snapshot = WorkflowModelReader.Read(path);

            string[] outlineIds = outline.All().Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            string[] snapshotIds = snapshot.All().Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

            Assert.That(snapshot.Ok, Is.True, string.Join(Environment.NewLine, snapshot.Errors));
            Assert.That(outlineIds, Is.EqualTo(snapshotIds), "адреса outline и модель-ридера разошлись: " + fileName);
        }

        [Test]
        public void Outline_IfFixture_ShowsBothBranches()
        {
            WorkflowOutline outline = Read("if.xaml");

            Assert.Multiple(() =>
            {
                Assert.That(outline.Find("root/Then"), Is.Not.Null, "ветка Then обязана быть в структуре");
                Assert.That(outline.Find("root/Else"), Is.Not.Null, "ветка Else обязана быть в структуре");
                Assert.That(outline.Find("root/Then").ShortTypeName, Is.EqualTo("WriteLine"));
                Assert.That(outline.Find("root/Else").DisplayName, Is.EqualTo("Log else branch"));
            });
        }

        [Test]
        public void Outline_TryCatchFixture_ShowsCatchHandler()
        {
            WorkflowOutline outline = Read("trycatch.xaml");

            WorkflowOutlineNode handler = outline.Find("root/Catches[0]/Action/Handler");

            Assert.Multiple(() =>
            {
                Assert.That(handler, Is.Not.Null, "тело обработчика обязано быть в структуре: " + DescribeIssues(outline));
                Assert.That(handler.ShortTypeName, Is.EqualTo("Sequence"));
                Assert.That(outline.Find("root/Catches[0]/Action/Handler/Activities[0]").DisplayName,
                    Is.EqualTo("Log inside catch"));
            });
        }

        [Test]
        public void Outline_FlowchartFixture_ShowsSteps()
        {
            WorkflowOutline outline = Read("flowchart.xaml");

            List<WorkflowOutlineNode> nodes = outline.All().ToList();
            WorkflowOutlineNode step = nodes.FirstOrDefault(node =>
                string.Equals(node.DisplayName, "Log flowchart step", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.True, DescribeIssues(outline));
                Assert.That(step, Is.Not.Null,
                    "шаг блок-схемы обязан быть в структуре, иначе агент его не увидит: " +
                    string.Join(", ", nodes.Select(node => node.Id)));
                Assert.That(step.ShortTypeName, Is.EqualTo("WriteLine"));
            });
        }

        [Test]
        public void Outline_Arguments_AreIncludedByDefault()
        {
            WorkflowOutline outline = Read("sequence.xaml");

            WorkflowOutlineArgument text = outline.Find("root/Activities[0]").Arguments
                .SingleOrDefault(argument => argument.Name == "Text");

            Assert.Multiple(() =>
            {
                Assert.That(outline.IncludeArguments, Is.True);
                Assert.That(text, Is.Not.Null);
                Assert.That(text.Direction, Is.EqualTo("In"));
                Assert.That(text.TypeName, Is.EqualTo("String"));
                Assert.That(text.Expression, Is.EqualTo("\"first\""), "простое значение показывается как литерал в кавычках");
            });
        }

        [Test]
        public void Outline_Arguments_CanBeOmitted()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(
                FixturePath("sequence.xaml"), depth: 0, includeArguments: false);

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.True, DescribeIssues(outline));
                Assert.That(outline.All().SelectMany(node => node.Arguments), Is.Empty);
                Assert.That(outline.All().Select(node => node.Id), Does.Contain("root/Activities[0]"),
                    "дерево остаётся, даже если аргументы не запрошены");
            });
        }

        [Test]
        public void Outline_Variables_AreReported()
        {
            WorkflowOutline outline = Read("variables.xaml");

            IReadOnlyList<WorkflowOutlineVariable> variables = outline.Root.Variables;

            Assert.Multiple(() =>
            {
                Assert.That(variables.Select(variable => variable.Name), Is.EqualTo(new[] { "userName", "retries" }));
                Assert.That(variables[0].TypeName, Is.EqualTo("String"));
                Assert.That(variables[1].TypeName, Is.EqualTo("Int32"));
            });
        }

        [Test]
        public void Outline_DepthOne_KeepsOnlyRoot()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(FixturePath("sequence.xaml"), depth: 1);

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.True, DescribeIssues(outline));
                Assert.That(outline.Root.Children, Is.Empty);
                Assert.That(outline.Root.IsTruncated, Is.True, "агент должен видеть, что вложенные скрыты глубиной");
                Assert.That(outline.Root.ChildCount, Is.EqualTo(2));
                Assert.That(outline.ShownDepth, Is.EqualTo(1));
                Assert.That(outline.ActivityCount, Is.EqualTo(4), "счётчик активностей считается по модели, а не по показанному");
            });
        }

        [Test]
        public void Outline_DepthTwo_ShowsChildrenButNotGrandChildren()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(FixturePath("sequence.xaml"), depth: 2);

            WorkflowOutlineNode nested = outline.Find("root/Activities[1]");

            Assert.Multiple(() =>
            {
                Assert.That(outline.Root.Children, Has.Count.EqualTo(2));
                Assert.That(nested, Is.Not.Null);
                Assert.That(nested.Children, Is.Empty);
                Assert.That(nested.IsTruncated, Is.True);
                Assert.That(nested.ChildCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Outline_IsStableBetweenCalls()
        {
            string path = FixturePath("sequence.xaml");

            string[] first = StudioWorkflowOutlineService.Default.GetOutline(path).All().Select(node => node.Id).ToArray();
            string[] second = StudioWorkflowOutlineService.Default.GetOutline(path).All().Select(node => node.Id).ToArray();

            Assert.That(second, Is.EqualTo(first), "адреса не должны зависеть от вызова: на них ссылаются правки");
        }

        [Test]
        public void Outline_BrokenByValidation_KeepsTreeAndBindsIssuesToNodes()
        {
            WorkflowOutline outline = Read("sequence-broken.xaml");

            WorkflowOutlineIssue error = outline.Issues.FirstOrDefault(issue =>
                issue.IsError && issue.ElementId == "root/Activities[0]");

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.True, "структура читается даже у невалидного процесса");
                Assert.That(outline.HasErrors, Is.True, "ошибки валидации обязаны попасть в структуру");
                Assert.That(error, Is.Not.Null,
                    "ошибка обязана быть привязана к узлу, иначе агент не знает, что править: " +
                    string.Join(" | ", outline.Issues));
                Assert.That(error.Message, Is.Not.Empty);
            });
        }

        [Test]
        public void Outline_MalformedFile_ReturnsLoadError()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(FixturePath("malformed.xaml"));

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.False);
                Assert.That(outline.Root, Is.Null);
                Assert.That(outline.Errors.Select(issue => issue.Code), Does.Contain("xaml.load"));
            });
        }

        [Test]
        public void Outline_MissingFile_ReturnsPathError()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(
                Path.Combine(Path.GetTempPath(), "нет-процесса-" + Guid.NewGuid().ToString("N") + ".xaml"));

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.False);
                Assert.That(outline.Errors.Select(issue => issue.Code), Does.Contain("xaml.path"));
            });
        }

        [Test]
        public void Outline_EmptyPath_ReturnsPathError()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(null);

            Assert.Multiple(() =>
            {
                Assert.That(outline.Ok, Is.False);
                Assert.That(outline.Errors.Single().Code, Is.EqualTo("xaml.path"));
            });
        }

        [Test]
        public void Outline_Find_AcceptsIdAndUniqueDisplayName()
        {
            WorkflowOutline outline = Read("sequence.xaml");

            Assert.Multiple(() =>
            {
                Assert.That(outline.Find("root").Id, Is.EqualTo("root"));
                Assert.That(outline.Find("root/Activities[0]").DisplayName, Is.EqualTo("Log first step"));
                Assert.That(outline.Find("Log second step").Id, Is.EqualTo("root/Activities[1]/Activities[0]"),
                    "уникальная подпись работает как адрес");
                Assert.That(outline.Find("НетТакой"), Is.Null);
                Assert.That(outline.Find(null), Is.Null);
            });
        }

        private static WorkflowOutline Read(string fileName)
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(FixturePath(fileName));
            Assert.That(outline.Ok, Is.True, DescribeIssues(outline));

            return outline;
        }

        private static string DescribeIssues(WorkflowOutline outline)
        {
            return outline == null
                ? "outline == null"
                : string.Join(Environment.NewLine, outline.Issues.Select(issue => issue.ToString()));
        }

        private static string FixturePath(string fileName) => Path.Combine(FixturesDirectory, fileName);

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
    }

    /// <summary>
    /// AG-03: формат ответа <c>workflow_outline</c> и контракт инструмента (read-only, доступен в Plan,
    /// служба подменяется фейком — тест не зависит от WF-загрузки).
    /// </summary>
    [TestFixture]
    public class WorkflowOutlineToolTests
    {
        [Test]
        public void Registry_ContainsWorkflowOutlineTool()
        {
            AgentToolRegistry registry = CreateFactory(NullWorkflowOutlineService.Instance).CreateRegistry();

            IAgentTool tool = registry.Find("workflow_outline");

            Assert.Multiple(() =>
            {
                Assert.That(tool, Is.Not.Null, "нет инструмента workflow_outline");
                Assert.That(tool.Mutates, Is.False, "чтение структуры ничего не меняет");
                Assert.That(tool.Permission, Is.EqualTo(AgentPermissions.ReadFiles));
                Assert.That(registry.ForMode(AgentMode.Plan, AgentPermissions.All).Select(item => item.Name),
                    Does.Contain("workflow_outline"), "структуру нужно читать и в режиме Plan");
            });
        }

        [Test]
        public void Tool_PassesDepthAndArgumentsToService()
        {
            var service = new FakeOutlineService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "workflow_outline", new JObject
            {
                ["path"] = "Main.xaml",
                ["depth"] = 2,
                ["include_arguments"] = false
            });

            Assert.Multiple(() =>
            {
                Assert.That(service.LastDepth, Is.EqualTo(2));
                Assert.That(service.LastIncludeArguments, Is.False);
                Assert.That(service.LastPath, Does.EndWith("Main.xaml"));
            });
        }

        [Test]
        public void Tool_DefaultsToWholeTreeWithArguments()
        {
            var service = new FakeOutlineService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "workflow_outline", new JObject { ["path"] = "Main.xaml" });

            Assert.Multiple(() =>
            {
                Assert.That(service.LastDepth, Is.EqualTo(0));
                Assert.That(service.LastIncludeArguments, Is.True);
            });
        }

        [Test]
        public void Tool_RequiresPath()
        {
            AgentToolRegistry registry = CreateFactory(new FakeOutlineService()).CreateRegistry();

            Assert.That(Run(registry, "workflow_outline", new JObject()),
                Does.Contain("Missing or empty \"path\""));
        }

        [Test]
        public void Tool_RejectsInvalidDepth()
        {
            AgentToolRegistry registry = CreateFactory(new FakeOutlineService()).CreateRegistry();

            Assert.That(Run(registry, "workflow_outline", new JObject { ["path"] = "Main.xaml", ["depth"] = -1 }),
                Does.Contain("Invalid \"depth\""));
            Assert.That(Run(registry, "workflow_outline", new JObject { ["path"] = "Main.xaml", ["depth"] = "abc" }),
                Does.Contain("Invalid \"depth\""));
        }

        [Test]
        public void Tool_RejectsInvalidIncludeArguments()
        {
            AgentToolRegistry registry = CreateFactory(new FakeOutlineService()).CreateRegistry();

            Assert.That(Run(registry, "workflow_outline", new JObject
            {
                ["path"] = "Main.xaml",
                ["include_arguments"] = "может быть"
            }), Does.Contain("Invalid \"include_arguments\""));
        }

        [Test]
        public void Tool_WithoutService_ExplainsInsteadOfThrowing()
        {
            AgentToolRegistry registry = CreateFactory(NullWorkflowOutlineService.Instance).CreateRegistry();

            Assert.That(Run(registry, "workflow_outline", new JObject { ["path"] = "Main.xaml" }),
                Does.Contain("Workflow outline service is unavailable"));
        }

        [Test]
        public void Tool_RendersTreeFromRealService()
        {
            string path = Path.Combine(FixturesDirectory, "sequence.xaml");
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string result = Run(registry, "workflow_outline", new JObject { ["path"] = path });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("Структура процесса"));
                Assert.That(result, Does.Contain("root: Sequence «Fixture: sequence»"));
                Assert.That(result, Does.Contain("root/Activities[0]: WriteLine"));
                Assert.That(result, Does.Contain("аргументы: Text(In, String) = \"first\""));
                Assert.That(result, Does.Contain("id — путь в дереве"));
            });
        }

        [Test]
        public void Tool_MissingFile_ExplainsInsteadOfThrowing()
        {
            AgentToolRegistry registry = CreateFactory(StudioWorkflowOutlineService.Default).CreateRegistry();

            string result = Run(registry, "workflow_outline", new JObject
            {
                ["path"] = Path.Combine(Path.GetTempPath(), "нет-" + Guid.NewGuid().ToString("N") + ".xaml")
            });

            Assert.That(result, Does.Contain("не прочитана"));
            Assert.That(result, Does.Contain("не найден"));
        }

        [Test]
        public void Text_TruncatedNode_ExplainsDepth()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(
                Path.Combine(FixturesDirectory, "sequence.xaml"), depth: 1);

            string text = WorkflowOutlineText.Describe(outline);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("глубина ограничена"));
                Assert.That(text, Does.Contain("глубина: 1"));
            });
        }

        [Test]
        public void Text_ShowsValidationIssuesWithElementIds()
        {
            WorkflowOutline outline = StudioWorkflowOutlineService.Default.GetOutline(
                Path.Combine(FixturesDirectory, "sequence-broken.xaml"));

            string text = WorkflowOutlineText.Describe(outline);

            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("Ошибки и предупреждения:"));
                Assert.That(text, Does.Contain("  - ошибка root/Activities[0]"),
                    "замечание печатается с адресом узла, иначе агент не знает, что править");
                Assert.That(text, Does.Contain("ошибки: "));
            });
        }

        private static string Run(AgentToolRegistry registry, string toolName, JObject arguments)
        {
            IAgentTool tool = registry.Find(toolName);
            Assert.That(tool, Is.Not.Null, "нет инструмента " + toolName);

            return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
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

        /// <summary>
        /// Страж подключения: без этой строки в студии инструмент отвечает «службы нет», а тесты этого
        /// не замечают — они подключают службу сами. Проверяется текст `MainView` (как в других стражах студии).
        /// </summary>
        [Test]
        public void StudioHost_ProvidesOutlineService()
        {
            string mainView = File.ReadAllText(Path.Combine(RepositoryRoot(), "AiRpaStudio", "Views", "MainView.xaml.cs"));

            Assert.That(mainView, Does.Contain("ChatEnvironment.WorkflowOutline"),
                "MainView обязан отдать службу структуры процесса в ChatEnvironment");
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

        private sealed class FakeOutlineService : IWorkflowOutlineService
        {
            public string LastPath { get; private set; }

            public int LastDepth { get; private set; }

            public bool LastIncludeArguments { get; private set; }

            public WorkflowOutline GetOutline(string path, int depth = 0, bool includeArguments = true)
            {
                LastPath = path;
                LastDepth = depth;
                LastIncludeArguments = includeArguments;

                var outline = new WorkflowOutline(path, depth, includeArguments) { Ok = true };
                outline.Root = new WorkflowOutlineNode("root", string.Empty, 0, string.Empty, "System.Activities.Statements.Sequence", "Main");
                outline.ActivityCount = 1;

                return outline;
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
}
