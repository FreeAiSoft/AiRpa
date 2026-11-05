using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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

namespace AiRpaStudio.Tests
{
    /// <summary>
    /// AG-11: промпт агента обязан описывать только существующие инструменты и цикл правки процесса.
    /// <para>
    /// До карточки промпты ACT звали <c>edit_open_document</c>, <c>apply_fix</c> и <c>write_file</c> — таких
    /// инструментов в реестре студии нет (файлы пишет <c>editor</c>), а 14 WF-инструментов не были описаны
    /// ни одним правилом и ни одним примером. Страж здесь один: любое имя инструмента, упомянутое в
    /// <see cref="PromptDefaults"/>, обязано существовать в реальном реестре <see cref="AgentToolFactory"/>,
    /// иначе тест валит сборку текста промпта, а не пользователь в чате.
    /// </para>
    /// <para>Правило волны 7: новый инструмент описывается в промпте тем же именем и попадает в этот страж.</para>
    /// </summary>
    [TestFixture]
    public class PromptGuidanceTests
    {
        /// <summary>Префиксы имён WF-инструментов (то же правило, что в <c>WorkflowToolRegistryGuardTests</c>).</summary>
        private static readonly string[] WorkflowPrefixes = { "activity_", "workflow_", "argument_", "variable_", "spec_" };

        /// <summary>
        /// Имена в стиле snake_case, которые в промптах не являются инструментами: параметры
        /// (<c>read_files</c>: страницы, <c>workflow_outline</c>: аргументы, <c>editor</c>: замена фрагмента), значение <c>action</c> и подстановка <c>{mode_instructions}</c>.
        /// Список намеренно короткий: всё остальное обязано быть инструментом реестра.
        /// </summary>
        private static readonly string[] AllowedNonToolNames =
        {
            "start_line", "end_line", "include_arguments", "old_text", "new_text", "tool_call", "mode_instructions"
        };

        /// <summary>Имена инструментов, которых в студии нет (VSIX-эпоха и отклонённый <c>AG-07</c>).</summary>
        private static readonly string[] LegacyToolNames =
        {
            "edit_open_document", "apply_fix", "write_file", "xaml_write", "xaml_read"
        };

        private static readonly Regex SnakeCaseToken =
            new Regex(@"(?<![A-Za-z0-9_])[a-z][a-z0-9]*(?:_[a-z0-9]+)+(?![A-Za-z0-9_])");

        // ------------------------------------------------------------------
        // Имена инструментов в промптах
        // ------------------------------------------------------------------

        [Test]
        public void PromptTexts_EveryToolNameMentioned_IsRegisteredTool()
        {
            var registered = new HashSet<string>(
                AllTools(CreateRegistry()).Select(tool => tool.Name),
                StringComparer.OrdinalIgnoreCase);

            List<string> unknown = PromptToolNames()
                .Where(name => !registered.Contains(name))
                .Where(name => !AllowedNonToolNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.That(unknown, Is.Empty,
                "промпт зовёт несуществующие инструменты (или в промпт попал незадекларированный параметр): " +
                string.Join(", ", unknown));
        }

        [Test]
        public void PromptTexts_DoNotMentionLegacyTools()
        {
            List<string> found = LegacyToolNames
                .Where(name => PromptTexts().Any(text => text.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            Assert.That(found, Is.Empty,
                "в промпте остались инструменты VSIX-эпохи или отклонённые владельцем: " + string.Join(", ", found));
        }

        [Test]
        public void PromptTexts_MentionEveryWorkflowTool()
        {
            // Обратная сторона стража: инструмент, зарегистрированный для WF, обязан быть описан в промпте,
            // иначе агент о нём не знает (и это ровно та ошибка, которую закрывает карточка).
            List<string> registered = AllTools(CreateRegistry())
                .Where(tool => IsWorkflowTool(tool.Name))
                .Select(tool => tool.Name)
                .ToList();

            string prompt = string.Join("\n", PromptTexts());

            List<string> undocumented = registered
                .Where(name => prompt.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                .ToList();

            Assert.That(undocumented, Is.Empty,
                "WF-инструмент не описан в промпте: " + string.Join(", ", undocumented));
        }

        // ------------------------------------------------------------------
        // Режимы: Plan — только чтение, Act — цикл правки
        // ------------------------------------------------------------------

        [Test]
        public void PlanPrompt_OffersOnlyReadOnlyWorkflowTools()
        {
            AssertOnlyReadOnlyWorkflowTools(AgentPromptBuilder.Build(
                AgentMode.Plan, CreateRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All));
        }

        [Test]
        public void PlanNativePrompt_OffersOnlyReadOnlyWorkflowTools()
        {
            AssertOnlyReadOnlyWorkflowTools(AgentPromptBuilder.Build(
                AgentMode.Plan, CreateRegistry(), ToolCallingMode.Native, AgentPermissions.All));
        }

        [Test]
        public void PlanPrompt_DescribesWorkflowReadCycle()
        {
            string prompt = AgentPromptBuilder.Build(
                AgentMode.Plan, CreateRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(prompt, Does.Contain("workflow_outline"), "Plan обязан начинать с чтения структуры");
                Assert.That(prompt, Does.Contain("workflow_validate"), "Plan обязан описывать отчёт валидации");
                Assert.That(prompt, Does.Contain("activity_describe"), "Plan обязан читать каталог активностей");
                Assert.That(prompt, Does.Not.Contain("{\"action\":\"tool_call\",\"tool\":\"activity_insert\""),
                    "в Plan не бывает примеров правки");
            });
        }

        [Test]
        public void ActPrompt_DescribesWorkflowEditCycle()
        {
            string prompt = AgentPromptBuilder.Build(
                AgentMode.Act, CreateRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(prompt, Does.Contain("workflow_outline"), "цикл начинается с чтения структуры");
                Assert.That(prompt, Does.Contain("activity_insert"), "цикл описывает правку модели");
                Assert.That(prompt, Does.Contain("argument_set"), "цикл описывает правку аргументов");
                Assert.That(prompt, Does.Contain("workflow_validate"), "цикл описывает проверку после правки");
                Assert.That(prompt, Does.Contain("read_errors"), "цикл заканчивается разбором ошибок сборки");
            });
        }

        [Test]
        public void ActNativePrompt_DescribesWorkflowEditCycle()
        {
            string prompt = AgentPromptBuilder.Build(
                AgentMode.Act, CreateRegistry(), ToolCallingMode.Native, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(prompt, Does.Contain("workflow_outline"));
                Assert.That(prompt, Does.Contain("activity_insert"));
                Assert.That(prompt, Does.Contain("workflow_validate"));
                Assert.That(prompt, Does.Contain("read_errors"));
            });
        }

        // ------------------------------------------------------------------
        // Содержимое блока WF
        // ------------------------------------------------------------------

        [Test]
        public void ActPrompt_HandWrittenXamlRule_MentionsEditorAndDesignerRefusal()
        {
            string prompt = AgentPromptBuilder.Build(
                AgentMode.Act, CreateRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(prompt, Does.Contain("editor"), "руками XAML правится только инструментом editor");
                Assert.That(prompt, Does.Contain("last resort"), "ручная правка XAML — последнее средство");
                Assert.That(prompt, Does.Contain("designer"),
                    "промпт обязан объяснять отказ editor для файла, открытого в конструкторе");
            });
        }

        [Test]
        public void ActPrompt_MentionsActivityCreateOnlyInsideExistingProject()
        {
            string prompt = AgentPromptBuilder.Build(
                AgentMode.Act, CreateRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(prompt, Does.Contain("activity_create"));
                Assert.That(prompt, Does.Contain("EXISTING"));
                Assert.That(prompt, Does.Contain("from scratch"),
                    "граница «проект с нуля не создаём» должна быть в промпте, а не только в коде");
            });
        }

        [Test]
        public void ActPrompt_ContainsFourWorkflowExamples()
        {
            string prompt = AgentPromptBuilder.Build(
                AgentMode.Act, CreateRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All);

            var examples = Regex.Matches(prompt, @"Example [1-4]\b").Cast<Match>().Select(m => m.Value).Distinct().ToList();

            Assert.That(examples.Count, Is.EqualTo(4),
                "в промпте ACT должно быть четыре примера «запрос → tool-call», найдено: " + examples.Count);
        }

        [Test]
        public void ActPrompt_ExamplesCoverInsertArgumentValidationAndCreation()
        {
            string prompt = AgentPromptBuilder.Build(
                AgentMode.Act, CreateRegistry(), ToolCallingMode.JsonChat, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(prompt, Does.Contain("\"tool\":\"activity_insert\""), "пример вставки активности");
                Assert.That(prompt, Does.Contain("\"tool\":\"argument_set\""), "пример аргумента-выражения");
                Assert.That(prompt, Does.Contain("\"tool\":\"workflow_validate\""), "пример реакции на ошибку валидации");
                Assert.That(prompt, Does.Contain("\"tool\":\"activity_create\""), "пример создания активности");
            });
        }

        [Test]
        public void WorkflowGuidance_IsAbsentWhenRegistryHasNoWorkflowTools()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("my_tool", "test tool", "{}", mutates: false,
                (arguments, cancellationToken) => Task.FromResult("result")));

            string json = AgentPromptBuilder.Build(AgentMode.Act, registry, ToolCallingMode.JsonChat, AgentPermissions.All);
            string native = AgentPromptBuilder.Build(AgentMode.Act, registry, ToolCallingMode.Native, AgentPermissions.All);

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Not.Contain("Workflow (WF)"),
                    "промпт не должен описывать WF-инструменты, которых нет в реестре");
                Assert.That(native, Does.Not.Contain("Workflow (WF)"));
            });
        }

        // ------------------------------------------------------------------
        // Проверки
        // ------------------------------------------------------------------

        private static void AssertOnlyReadOnlyWorkflowTools(string prompt)
        {
            Dictionary<string, bool> mutates = AllTools(CreateRegistry())
                .ToDictionary(tool => tool.Name, tool => tool.Mutates, StringComparer.OrdinalIgnoreCase);

            List<string> leaking = SnakeCaseToken.Matches(prompt)
                .Cast<Match>()
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => mutates.ContainsKey(name) && mutates[name])
                .ToList();

            Assert.That(leaking, Is.Empty,
                "Plan-промпт предлагает мутирующие инструменты: " + string.Join(", ", leaking));
        }

        private static bool IsWorkflowTool(string name)
        {
            return !string.IsNullOrEmpty(name)
                && WorkflowPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Весь текст промптов по умолчанию — единственный источник, который правит эта карточка.</summary>
        private static List<string> PromptTexts()
        {
            return typeof(PromptDefaults)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.FieldType == typeof(string))
                .Select(field => (string)field.GetValue(null) ?? "")
                .ToList();
        }

        private static IEnumerable<string> PromptToolNames()
        {
            return PromptTexts()
                .SelectMany(text => SnakeCaseToken.Matches(text).Cast<Match>().Select(match => match.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static List<IAgentTool> AllTools(AgentToolRegistry registry)
        {
            return registry.ForMode(AgentMode.Act, AgentPermissions.All).ToList();
        }

        /// <summary>Реальный реестр студии: те же инструменты, что видит пользователь.</summary>
        private static AgentToolRegistry CreateRegistry()
        {
            var factory = new AgentToolFactory(
                new StubAiProvider(),
                () => Path.GetTempPath(),
                NullStudioDocumentService.Instance,
                new FakeBuildFeedbackService(),
                (question, options, cancellationToken) => Task.FromResult("answer"),
                (systemPrompt, task, cancellationToken) => Task.FromResult("sub-result"),
                enableSubagents: false,
                subagentMaxDepth: 2,
                getDocsDirectory: null,
                activityCatalog: NullActivityCatalog.Instance);

            return factory.CreateRegistry();
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
