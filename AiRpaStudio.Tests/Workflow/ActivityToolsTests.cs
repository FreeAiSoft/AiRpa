using System;
using System.Collections.Generic;
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
    /// AG-01: инструменты <c>activity_search</c> / <c>activity_describe</c>.
    /// <para>
    /// Проверяется контракт инструментов (read-only, доступны в Plan, категория разрешений) и формат
    /// ответа; каталог подменяется фейком, поэтому тест не зависит от загруженных библиотек активностей.
    /// Один тест идёт по реальному каталогу студии — чтобы «фейк зелёный, а студия пустая» не проскочило.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ActivityToolsTests
    {
        private static readonly ActivityInfo SequenceInfo = new ActivityInfo(
            "Sequence",
            "System.Activities.Statements.Sequence",
            "Sequence",
            "Control Flow",
            "lang_toolbox_control_flow",
            "Запускает дочерние активности по порядку.",
            arguments: new ActivityArgumentInfo[0],
            containers: new[] { new ActivityContainerInfo("Activities", "collection", "Activity", "коллекция дочерних активностей") });

        private static readonly ActivityInfo WriteLineInfo = new ActivityInfo(
            "WriteLine",
            "System.Activities.Statements.WriteLine",
            "WriteLine",
            "Primitives",
            "lang_toolbox_primitives",
            arguments: new[] { new ActivityArgumentInfo("Text", ActivityArgumentDirection.In, "String", required: true) },
            containers: new ActivityContainerInfo[0]);

        [Test]
        public void Registry_ContainsActivityTools()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo, WriteLineInfo)).CreateRegistry();

            Assert.That(registry.Find("activity_search"), Is.Not.Null, "нет инструмента activity_search");
            Assert.That(registry.Find("activity_describe"), Is.Not.Null, "нет инструмента activity_describe");
        }

        [Test]
        public void ActivityTools_AreReadOnly_AndAvailableInPlanMode()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo)).CreateRegistry();
            List<string> plan = registry.ForMode(AgentMode.Plan, AgentPermissions.All).Select(tool => tool.Name).ToList();

            Assert.That(registry.Find("activity_search").Mutates, Is.False);
            Assert.That(registry.Find("activity_describe").Mutates, Is.False);
            Assert.That(registry.Find("activity_search").Permission, Is.EqualTo(AgentPermissions.ReadFiles));
            Assert.That(registry.Find("activity_describe").Permission, Is.EqualTo(AgentPermissions.ReadFiles));

            Assert.That(plan, Does.Contain("activity_search"));
            Assert.That(plan, Does.Contain("activity_describe"));
        }

        [Test]
        public void Search_ReturnsActivityMetadata()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo, WriteLineInfo)).CreateRegistry();

            string result = Run(registry, "activity_search", new JObject { ["query"] = "seq" });

            Assert.That(result, Does.Contain("Sequence"));
            Assert.That(result, Does.Contain("System.Activities.Statements.Sequence"));
            Assert.That(result, Does.Contain("Control Flow"));
            Assert.That(result, Does.Contain("контейнеры: Activities(collection, Activity)"));
        }

        [Test]
        public void Search_PassesCategoryAndLimitToCatalog()
        {
            var catalog = new FakeCatalog(SequenceInfo, WriteLineInfo);
            AgentToolRegistry registry = CreateFactory(catalog).CreateRegistry();

            Run(registry, "activity_search", new JObject { ["query"] = "line", ["category"] = "Primitives", ["limit"] = 5 });

            Assert.That(catalog.LastQuery, Is.EqualTo("line"));
            Assert.That(catalog.LastCategory, Is.EqualTo("Primitives"));
            Assert.That(catalog.LastLimit, Is.EqualTo(5));
        }

        [Test]
        public void Search_DefaultsLimit_WhenNotGiven()
        {
            var catalog = new FakeCatalog(SequenceInfo);
            AgentToolRegistry registry = CreateFactory(catalog).CreateRegistry();

            Run(registry, "activity_search", new JObject());

            Assert.That(catalog.LastLimit, Is.EqualTo(20));
            Assert.That(catalog.LastQuery, Is.Empty);
        }

        [Test]
        public void Search_RejectsInvalidLimit()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo)).CreateRegistry();

            string result = Run(registry, "activity_search", new JObject { ["limit"] = "abc" });

            Assert.That(result, Does.Contain("Invalid \"limit\""));
        }

        [Test]
        public void Search_ClampsTooBigLimit()
        {
            var catalog = new FakeCatalog(SequenceInfo);
            AgentToolRegistry registry = CreateFactory(catalog).CreateRegistry();

            Run(registry, "activity_search", new JObject { ["limit"] = 100000 });

            Assert.That(catalog.LastLimit, Is.EqualTo(200));
        }

        [Test]
        public void Search_NothingFound_ExplainsHowToWiden()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo)).CreateRegistry();

            string result = Run(registry, "activity_search", new JObject { ["query"] = "НетТакой" });

            Assert.That(result, Does.Contain("Ничего не найдено"));
        }

        [Test]
        public void Describe_ReturnsFullActivityDescription()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo, WriteLineInfo)).CreateRegistry();

            string result = Run(registry, "activity_describe", new JObject { ["typeName"] = "WriteLine" });

            Assert.That(result, Does.Contain("WriteLine — System.Activities.Statements.WriteLine"));
            Assert.That(result, Does.Contain("категория: Primitives"));
            Assert.That(result, Does.Contain("  - Text (In, String, обязателен)"));
            Assert.That(result, Does.Contain("дочерние активности не принимает"));
        }

        [Test]
        public void Describe_UnknownActivity_SuggestsSimilar()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo)).CreateRegistry();

            string result = Run(registry, "activity_describe", new JObject { ["typeName"] = "Seque" });

            Assert.That(result, Does.Contain("не найдена"));
            Assert.That(result, Does.Contain("activity_search"));
        }

        [Test]
        public void Describe_RequiresTypeName()
        {
            AgentToolRegistry registry = CreateFactory(new FakeCatalog(SequenceInfo)).CreateRegistry();

            string result = Run(registry, "activity_describe", new JObject());

            Assert.That(result, Does.Contain("Missing or empty \"typeName\""));
        }

        [Test]
        public void Tools_WithoutCatalog_ReturnExplanationInsteadOfThrowing()
        {
            AgentToolRegistry registry = CreateFactory(NullActivityCatalog.Instance).CreateRegistry();

            Assert.That(Run(registry, "activity_search", new JObject { ["query"] = "Sequence" }),
                Does.Contain("Activity catalog is unavailable"));
            Assert.That(Run(registry, "activity_describe", new JObject { ["typeName"] = "Sequence" }),
                Does.Contain("Activity catalog is unavailable"));
        }

        /// <summary>
        /// Сквозная проверка на реальном каталоге студии: фейк не должен скрывать «пустой каталог».
        /// </summary>
        [Test]
        public void RealStudioCatalog_AnswersThroughTools()
        {
            AgentToolRegistry registry = CreateFactory(StudioActivityCatalog.Default).CreateRegistry();

            string search = Run(registry, "activity_search", new JObject { ["query"] = "Sequence" });
            string describe = Run(registry, "activity_describe", new JObject { ["typeName"] = "Sequence" });

            Assert.That(search, Does.Contain("System.Activities.Statements.Sequence"));
            Assert.That(describe, Does.Contain("контейнеры"));
            Assert.That(describe, Does.Contain("Activities"));
        }

        private static string Run(AgentToolRegistry registry, string toolName, JObject arguments)
        {
            IAgentTool tool = registry.Find(toolName);
            Assert.That(tool, Is.Not.Null, "нет инструмента " + toolName);

            return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static AgentToolFactory CreateFactory(IActivityCatalog catalog)
        {
            return new AgentToolFactory(
                new StubAiProvider(),
                () => System.IO.Path.GetTempPath(),
                NullStudioDocumentService.Instance,
                new FakeBuildFeedbackService(),
                (question, options, ct) => Task.FromResult("answer"),
                (systemPrompt, task, ct) => Task.FromResult("sub-result"),
                enableSubagents: false,
                subagentMaxDepth: 2,
                getDocsDirectory: null,
                activityCatalog: catalog);
        }

        private sealed class FakeCatalog : IActivityCatalog
        {
            private readonly List<ActivityInfo> _items;

            public FakeCatalog(params ActivityInfo[] items)
            {
                _items = items?.ToList() ?? new List<ActivityInfo>();
            }

            public string LastQuery { get; private set; }

            public string LastCategory { get; private set; }

            public int LastLimit { get; private set; }

            public int Count => _items.Count;

            public IReadOnlyList<string> Categories =>
                _items.Select(item => item.Category).Distinct().ToArray();

            public IReadOnlyList<ActivityInfo> Search(string query, string category = null, int limit = 0)
            {
                LastQuery = query ?? string.Empty;
                LastCategory = category;
                LastLimit = limit;

                IEnumerable<ActivityInfo> items = _items;

                if (!string.IsNullOrWhiteSpace(query))
                {
                    items = items.Where(item => item.Key.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    items = items.Where(item => string.Equals(item.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                List<ActivityInfo> list = items.ToList();

                return limit > 0 && list.Count > limit ? list.Take(limit).ToList() : list;
            }

            public ActivityInfo Describe(string typeName)
            {
                return _items.FirstOrDefault(item =>
                    string.Equals(item.Key, (typeName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
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
