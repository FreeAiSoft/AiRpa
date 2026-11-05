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
using NUnit.Framework;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;
using RPA.Chat.Services.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-10: страж реестра WF-инструментов агента.
    /// <para>
    /// До этой карточки разрешения и режимы проверялись группами по отдельности
    /// (<c>AgentToolFactoryTests</c>, <c>ActivityToolsTests</c>, <c>Workflow*ToolTests</c>), поэтому новый
    /// инструмент можно было зарегистрировать вообще без строки в страже — тесты оставались зелёными.
    /// Здесь одна таблица ожиданий на все WF-инструменты и сверка <b>множествами</b>: лишний или забытый
    /// инструмент валит тест.
    /// </para>
    /// <para>
    /// Правило волны 7: новый WF-инструмент появляется вместе со строкой в <see cref="Table"/>.
    /// Негативы («сняли защиту — страж упал») проверяются в этом же файле мета-тестами
    /// <c>Guard_*</c>: они подкладывают в реестр инструмент с намеренно неверными флагами и требуют,
    /// чтобы проверки сработали. Так страж нельзя «успокоить», не тронув production-код.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowToolRegistryGuardTests
    {
        /// <summary>Префиксы имён инструментов, которые правилами карточки считаются «WF-инструментами».</summary>
        private static readonly string[] WorkflowPrefixes = { "activity_", "workflow_", "argument_", "variable_" };

        /// <summary>
        /// Таблица ожиданий: имя → (мутирует ли, категория разрешений). Единственный источник правды для стража.
        /// </summary>
        private static readonly WorkflowToolSpec[] Table =
        {
            // read-only: каталог, структура и отчёт валидации
            new WorkflowToolSpec("activity_search", mutates: false, AgentPermissions.ReadFiles),
            new WorkflowToolSpec("activity_describe", mutates: false, AgentPermissions.ReadFiles),
            new WorkflowToolSpec("workflow_outline", mutates: false, AgentPermissions.ReadFiles),
            new WorkflowToolSpec("workflow_validate", mutates: false, AgentPermissions.ReadFiles),

            // мутирующие: правка модели конструктора
            new WorkflowToolSpec("activity_insert", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("activity_delete", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("activity_move", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("activity_wrap", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("activity_undo", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("activity_redo", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("argument_set", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("variable_add", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("variable_remove", mutates: true, AgentPermissions.EditFiles),
            new WorkflowToolSpec("activity_create", mutates: true, AgentPermissions.EditFiles)
        };

        // ------------------------------------------------------------------
        // Множество и флаги
        // ------------------------------------------------------------------

        [Test]
        public void Registry_WorkflowToolSet_EqualsTable()
        {
            List<IAgentTool> tools = WorkflowToolsIn(CreateRegistry(), AgentMode.Act, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(Unknown(tools), Is.Empty,
                    "WF-инструмент зарегистрирован, но забыт в таблице стража: " + string.Join(", ", Unknown(tools)));
                Assert.That(Absent(tools), Is.Empty,
                    "в таблице стража есть инструмент, которого нет в реестре: " + string.Join(", ", Absent(tools)));
                Assert.That(tools.Count, Is.EqualTo(Table.Length), "количество WF-инструментов разошлось с таблицей");
            });
        }

        [Test]
        public void Registry_WorkflowToolFlags_MatchTable()
        {
            AgentToolRegistry registry = CreateRegistry();
            List<string> mismatches = FlagMismatches(registry);

            Assert.That(mismatches, Is.Empty,
                "флаги инструмента разошлись с таблицей стража: " + string.Join("; ", mismatches));
        }

        // ------------------------------------------------------------------
        // Режимы: Plan — только чтение, Act — всё
        // ------------------------------------------------------------------

        [Test]
        public void PlanMode_HidesEveryMutatingWorkflowTool()
        {
            List<string> leaking = MutatingToolsLeakingIntoPlan(CreateRegistry());

            Assert.That(leaking, Is.Empty,
                "мутирующий WF-инструмент виден в Plan при AgentPermissions.All: " + string.Join(", ", leaking));
        }

        [Test]
        public void PlanMode_ShowsEveryReadOnlyWorkflowTool()
        {
            List<string> plan = WorkflowToolsIn(CreateRegistry(), AgentMode.Plan, AgentPermissions.All)
                .Select(tool => tool.Name)
                .ToList();

            List<string> hidden = Table.Where(row => !row.Mutates)
                .Select(row => row.Name)
                .Where(name => !plan.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            Assert.That(hidden, Is.Empty,
                "read-only WF-инструмент не виден в Plan: " + string.Join(", ", hidden));
        }

        [Test]
        public void ActMode_ShowsEveryWorkflowTool()
        {
            List<string> act = WorkflowToolsIn(CreateRegistry(), AgentMode.Act, AgentPermissions.All)
                .Select(tool => tool.Name)
                .ToList();

            Assert.That(act, Is.EquivalentTo(Table.Select(row => row.Name)),
                "в Act обязаны быть все WF-инструменты");
        }

        // ------------------------------------------------------------------
        // Разрешения: категории
        // ------------------------------------------------------------------

        [Test]
        public void ReadOnlyWorkflowTools_DoNotRequireEditFiles()
        {
            List<string> offenders = ReadOnlyToolsRequiringEditFiles(CreateRegistry());

            Assert.That(offenders, Is.Empty,
                "read-only WF-инструмент требует EditFiles: " + string.Join(", ", offenders));
        }

        [Test]
        public void MutatingWorkflowTools_RequireEditFiles()
        {
            List<string> offenders = MutatingToolsWithoutEditFiles(CreateRegistry());

            Assert.That(offenders, Is.Empty,
                "мутирующий WF-инструмент не требует EditFiles: " + string.Join(", ", offenders));
        }

        [Test]
        public void ReadOnlyWorkflowTools_AreVisibleWithReadFilesOnly()
        {
            AgentToolRegistry registry = CreateRegistry();

            List<string> visible = WorkflowToolsIn(registry, AgentMode.Plan, AgentPermissions.ReadFiles)
                .Select(tool => tool.Name)
                .ToList();

            Assert.That(visible, Is.EquivalentTo(Table.Where(row => !row.Mutates).Select(row => row.Name)),
                "при одном разрешении ReadFiles в Plan обязаны остаться ровно 4 read-only WF-инструмента");
        }

        // ------------------------------------------------------------------
        // Настройки студии → разрешения → содержимое реестра
        // ------------------------------------------------------------------

        [Test]
        public void SettingsWithoutEditFiles_RemoveEveryMutatingWorkflowTool()
        {
            // Ровно то, что делает ChatViewModel: настройки → маска разрешений → реестр.
            AgentPermissions allowed = AgentPermissionsPolicy.Resolve(new SettingsData
            {
                AllowReadFiles = true,
                AllowEditFiles = false,
                AllowExecuteCommand = true,
                AllowWebFetch = true,
                AllowSubagents = true
            });

            List<string> visible = WorkflowToolsIn(CreateRegistry(), AgentMode.Act, allowed)
                .Select(tool => tool.Name)
                .ToList();

            Assert.That(visible, Is.EquivalentTo(Table.Where(row => !row.Mutates).Select(row => row.Name)),
                "без EditFiles в Act не должно остаться ни одного мутирующего WF-инструмента");
        }

        [Test]
        public void SettingsWithoutReadFiles_RemoveEveryReadOnlyWorkflowTool()
        {
            // Зафиксированное решение AG-10: правки без чтения разрешены (мутирующие инструменты видны),
            // а чтение структуры запрещено — вместе с workflow_outline.
            AgentPermissions allowed = AgentPermissionsPolicy.Resolve(new SettingsData
            {
                AllowReadFiles = false,
                AllowEditFiles = true,
                AllowExecuteCommand = true,
                AllowWebFetch = true,
                AllowSubagents = true
            });

            List<string> visible = WorkflowToolsIn(CreateRegistry(), AgentMode.Act, allowed)
                .Select(tool => tool.Name)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(visible, Is.EquivalentTo(Table.Where(row => row.Mutates).Select(row => row.Name)),
                    "без ReadFiles в Act обязаны остаться только мутирующие WF-инструменты");
                Assert.That(visible, Does.Not.Contain("workflow_outline"),
                    "workflow_outline — чтение структуры, он тоже уходит без ReadFiles");
            });
        }

        // ------------------------------------------------------------------
        // Инвариант «нет MCP»
        // ------------------------------------------------------------------

        [Test]
        public void WorkflowTools_DoNotMentionMcp()
        {
            List<IAgentTool> tools = WorkflowToolsIn(CreateRegistry(), AgentMode.Act, AgentPermissions.All);

            List<string> offenders = tools
                .Where(tool => MentionsMcp(tool.Name) || MentionsMcp(tool.Description))
                .Select(tool => tool.Name)
                .ToList();

            Assert.That(offenders, Is.Empty,
                "категории MCP больше нет: WF-инструмент не должен упоминать её в имени или описании: "
                + string.Join(", ", offenders));
        }

        [Test]
        public void RpaChatAndAgentCore_DoNotReferenceStudioMcp()
        {
            string root = FindRepositoryRoot();

            string[] forbidden =
            {
                "RPA.Mcp", @"RPA\Mcp", @"AiRpaStudio\Mcp", "AiRpaStudio/Mcp"
            };

            var offenders = new List<string>();

            foreach (string project in new[] { "RPA.Chat", "Agent.Core" })
            {
                foreach (string file in SourceFiles(Path.Combine(root, project)))
                {
                    string text = File.ReadAllText(file);
                    foreach (string needle in forbidden)
                    {
                        int index = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                        if (index >= 0)
                        {
                            offenders.Add(project + "/" + Path.GetFileName(file) + " → " + needle);
                        }
                    }
                }
            }

            Assert.That(offenders, Is.Empty,
                "слой MCP (AiRpaStudio\\Mcp) не должен попадать в описания инструментов: " + string.Join(", ", offenders));
        }

        // ------------------------------------------------------------------
        // Мета-тесты: страж обязан ловить снятую защиту
        // ------------------------------------------------------------------

        [Test]
        public void Guard_DetectsWorkflowToolMissingFromTable()
        {
            AgentToolRegistry registry = CopyOfRegistry();
            registry.Register(new AgentTool(
                "activity_fake",
                "Инструмент, которого нет в таблице стража.",
                "{}",
                false,
                (arguments, cancellationToken) => Task.FromResult("fake"),
                permission: AgentPermissions.ReadFiles));

            List<string> unknown = Unknown(WorkflowToolsIn(registry, AgentMode.Act, AgentPermissions.All));

            Assert.That(unknown, Is.EqualTo(new[] { "activity_fake" }),
                "новый WF-инструмент без строки в таблице должен быть пойман");
        }

        [Test]
        public void Guard_DetectsForgottenRows()
        {
            // Реестр без одного инструмента: строки таблицы, которых нет в реестре, обязаны всплыть.
            AgentToolRegistry registry = new AgentToolRegistry();
            foreach (IAgentTool tool in CreateRegistry().ForMode(AgentMode.Act, AgentPermissions.All))
            {
                if (!string.Equals(tool.Name, "variable_remove", StringComparison.OrdinalIgnoreCase))
                {
                    registry.Register(tool);
                }
            }

            Assert.That(Absent(WorkflowToolsIn(registry, AgentMode.Act, AgentPermissions.All)),
                Is.EqualTo(new[] { "variable_remove" }));
        }

        [Test]
        public void Guard_DetectsMutatingToolWithoutEditFiles()
        {
            // Сняли EditFiles у мутирующего инструмента — страж категории обязан упасть.
            AgentToolRegistry registry = ReplaceTool("variable_remove", mutates: true, permission: AgentPermissions.ReadFiles);

            Assert.That(MutatingToolsWithoutEditFiles(registry), Does.Contain("variable_remove"));
        }

        [Test]
        public void Guard_DetectsReadOnlyToolRequiringEditFiles()
        {
            // Read-only инструмент перевели в EditFiles — страж «чтение не требует прав» падает
            // и разрешение в его отсутствие пропадает в Plan.
            AgentToolRegistry registry = ReplaceTool("workflow_outline", mutates: false, permission: AgentPermissions.EditFiles);

            Assert.Multiple(() =>
            {
                Assert.That(ReadOnlyToolsRequiringEditFiles(registry), Does.Contain("workflow_outline"));
                Assert.That(ClosedPlanSet(registry), Does.Not.Contain("workflow_outline"));
            });
        }

        [Test]
        public void Guard_DetectsClearedMutatesFlag()
        {
            // Забыли mutates: true у аргументов — инструмент протекает в Plan.
            AgentToolRegistry registry = ReplaceTool("argument_set", mutates: false, permission: AgentPermissions.EditFiles);

            Assert.Multiple(() =>
            {
                Assert.That(MutatingToolsLeakingIntoPlan(registry), Does.Contain("argument_set"));
                Assert.That(FlagMismatches(registry), Is.Not.Empty);
            });
        }

        // ------------------------------------------------------------------
        // Проверки (используются и мета-тестами)
        // ------------------------------------------------------------------

        private static List<string> Unknown(IEnumerable<IAgentTool> tools)
        {
            return tools
                .Where(tool => !Table.Any(row => string.Equals(row.Name, tool.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(tool => tool.Name)
                .ToList();
        }

        private static List<string> Absent(IEnumerable<IAgentTool> tools)
        {
            var present = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);

            return Table
                .Where(row => !present.Contains(row.Name))
                .Select(row => row.Name)
                .ToList();
        }

        private static List<string> FlagMismatches(AgentToolRegistry registry)
        {
            var mismatches = new List<string>();

            foreach (WorkflowToolSpec row in Table)
            {
                IAgentTool tool = registry.Find(row.Name);
                if (tool == null)
                {
                    mismatches.Add(row.Name + ": нет в реестре");
                    continue;
                }

                if (tool.Mutates != row.Mutates)
                {
                    mismatches.Add(row.Name + ": mutates=" + tool.Mutates + ", ожидалось " + row.Mutates);
                }

                if (tool.Permission != row.Permission)
                {
                    mismatches.Add(row.Name + ": permission=" + tool.Permission + ", ожидалось " + row.Permission);
                }
            }

            return mismatches;
        }

        private static List<string> MutatingToolsLeakingIntoPlan(AgentToolRegistry registry)
        {
            var plan = new HashSet<string>(
                registry.ForMode(AgentMode.Plan, AgentPermissions.All).Select(tool => tool.Name),
                StringComparer.OrdinalIgnoreCase);

            return Table
                .Where(row => row.Mutates)
                .Select(row => row.Name)
                .Where(plan.Contains)
                .ToList();
        }

        /// <summary>Read-only инструменты не должны требовать EditFiles (чтение — отдельное разрешение).</summary>
        private static List<string> ReadOnlyToolsRequiringEditFiles(AgentToolRegistry registry)
        {
            return Table
                .Where(row => !row.Mutates)
                .Select(row => registry.Find(row.Name))
                .Where(tool => tool != null && (tool.Permission & AgentPermissions.EditFiles) != 0)
                .Select(tool => tool.Name)
                .ToList();
        }

        private static List<string> MutatingToolsWithoutEditFiles(AgentToolRegistry registry)
        {
            return Table
                .Where(row => row.Mutates)
                .Select(row => registry.Find(row.Name))
                .Where(tool => tool != null && (tool.Permission & AgentPermissions.EditFiles) == 0)
                .Select(tool => tool.Name)
                .ToList();
        }

        /// <summary>Plan при одном разрешении ReadFiles — «закрытый» набор: только чтение и только с ReadFiles.</summary>
        private static List<string> ClosedPlanSet(AgentToolRegistry registry)
        {
            return registry.ForMode(AgentMode.Plan, AgentPermissions.ReadFiles)
                .Where(tool => IsWorkflowTool(tool.Name))
                .Select(tool => tool.Name)
                .ToList();
        }

        // ------------------------------------------------------------------
        // Инфраструктура
        // ------------------------------------------------------------------

        private static bool IsWorkflowTool(string name)
        {
            return !string.IsNullOrEmpty(name)
                && WorkflowPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MentionsMcp(string text)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf("mcp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<IAgentTool> WorkflowToolsIn(
            AgentToolRegistry registry,
            AgentMode mode,
            AgentPermissions allowed)
        {
            return registry.ForMode(mode, allowed)
                .Where(tool => IsWorkflowTool(tool.Name))
                .ToList();
        }

        private static AgentToolRegistry CreateRegistry()
        {
            var factory = new AgentToolFactory(
                new StubAiProvider(),
                () => Path.GetTempPath(),
                NullStudioDocumentService.Instance,
                new FakeBuildFeedbackService(),
                (question, options, ct) => Task.FromResult("answer"),
                (systemPrompt, task, ct) => Task.FromResult("sub-result"),
                enableSubagents: false,
                subagentMaxDepth: 2,
                getDocsDirectory: null,
                activityCatalog: NullActivityCatalog.Instance);

            return factory.CreateRegistry();
        }

        private static AgentToolRegistry CopyOfRegistry()
        {
            var registry = new AgentToolRegistry();
            foreach (IAgentTool tool in CreateRegistry().ForMode(AgentMode.Act, AgentPermissions.All))
            {
                registry.Register(tool);
            }

            return registry;
        }

        /// <summary>Копия реестра, в которой у одного инструмента подменены флаги (для мета-тестов).</summary>
        private static AgentToolRegistry ReplaceTool(string name, bool mutates, AgentPermissions permission)
        {
            var registry = new AgentToolRegistry();

            foreach (IAgentTool tool in CreateRegistry().ForMode(AgentMode.Act, AgentPermissions.All))
            {
                if (string.Equals(tool.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    registry.Register(new AgentTool(
                        tool.Name,
                        tool.Description,
                        tool.ParametersSchema,
                        mutates,
                        (arguments, cancellationToken) => Task.FromResult("stub"),
                        permission: permission));
                }
                else
                {
                    registry.Register(tool);
                }
            }

            return registry;
        }

        private static IEnumerable<string> SourceFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                yield break;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                string relative = file.Substring(directory.Length);
                if (relative.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0
                    || relative.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                yield return file;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
            {
                string relative = file.Substring(directory.Length);
                if (relative.IndexOf(@"\obj\", StringComparison.OrdinalIgnoreCase) >= 0
                    || relative.IndexOf(@"\bin\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                yield return file;
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
            return directory.FullName;
        }

        private sealed class WorkflowToolSpec
        {
            public WorkflowToolSpec(string name, bool mutates, AgentPermissions permission)
            {
                Name = name;
                Mutates = mutates;
                Permission = permission;
            }

            public string Name { get; }

            public bool Mutates { get; }

            public AgentPermissions Permission { get; }
        }

        private sealed class FakeBuildFeedbackService : IBuildFeedbackService
        {
            public Task BuildAsync(string configuration, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());

            public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);
        }
    }
}
