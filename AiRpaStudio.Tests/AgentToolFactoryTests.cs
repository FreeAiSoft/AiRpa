using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;
using Agent.Core.AgentTools;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Core.Services;

namespace RPA.Chat.Tests
{
    [TestFixture]
    public class AgentToolFactoryTests
    {
        private static AgentToolFactory CreateFactory(bool enableSubagents = false, System.Func<string> getDocsDirectory = null)
        {
            return new AgentToolFactory(
                new StubAiProvider(),
                () => System.IO.Path.GetTempPath(),
                NullStudioDocumentService.Instance,
                new FakeBuildFeedbackService(),
                (q, o, ct) => Task.FromResult("answer"),
                (sys, task, ct) => Task.FromResult("sub-result"),
                enableSubagents,
                subagentMaxDepth: 2,
                getDocsDirectory);
        }

        [Test]
        public void CreateRegistry_ContainsStudioTools()
        {
            var registry = CreateFactory().CreateRegistry();

            var names = new[]
            {
                "read_files", "list_files", "search_codebase", "git_status", "git_diff",
                "fetch_web_content", "read_errors", "read_output", "ask_question",
                "submit_and_exit", "run_commands", "editor", "build", "download_file", "git_commit"
            };

            foreach (var name in names)
                Assert.That(registry.Find(name), Is.Not.Null, "Missing tool: " + name);
        }

        [Test]
        public void CreateRegistry_RegistersNoMcpTools()
        {
            // AgentPermissions.Mcp no longer exists; verify no tool declares it (there is no Mcp category at all).
            var registry = CreateFactory().CreateRegistry();
            var all = registry.ForMode(AgentMode.Act, AgentPermissions.All);

            // No tool name may reference MCP.
            Assert.That(all.Any(t => t.Name.IndexOf("mcp", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
        }

        [Test]
        public void PlanMode_ExcludesMutatingTools()
        {
            var registry = CreateFactory().CreateRegistry();
            var plan = registry.ForMode(AgentMode.Plan, AgentPermissions.All).Select(t => t.Name).ToList();
            var act = registry.ForMode(AgentMode.Act, AgentPermissions.All).Select(t => t.Name).ToList();

            Assert.That(plan, Does.Not.Contain("build"));
            Assert.That(plan, Does.Not.Contain("editor"));
            Assert.That(plan, Does.Not.Contain("run_commands"));
            Assert.That(plan, Does.Not.Contain("download_file"));
            Assert.That(plan, Does.Not.Contain("git_commit"));

            Assert.That(act, Does.Contain("build"));
            Assert.That(act, Does.Contain("editor"));
        }

        [Test]
        public void CreateRegistry_ContainsDocumentationTools()
        {
            var registry = CreateFactory().CreateRegistry();

            Assert.That(registry.Find("search_docs"), Is.Not.Null, "Missing tool: search_docs");
            Assert.That(registry.Find("read_docs"), Is.Not.Null, "Missing tool: read_docs");
        }

        [Test]
        public void DocsTools_AreReadOnlyAndAvailableInPlanMode()
        {
            var registry = CreateFactory().CreateRegistry();
            var plan = registry.ForMode(AgentMode.Plan, AgentPermissions.All).Select(t => t.Name).ToList();

            Assert.That(plan, Does.Contain("search_docs"));
            Assert.That(plan, Does.Contain("read_docs"));

            Assert.That(registry.Find("search_docs").Mutates, Is.False);
            Assert.That(registry.Find("read_docs").Mutates, Is.False);
        }

        [Test]
        public void SearchDocs_ReturnsMatchesFromInjectedDirectory()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airpa_docs_factory_" + System.IO.Path.GetRandomFileName());
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "nuget-manager.md"), "# Менеджер NuGet пакетов\n\nПоиск пакетов в nuget.org через NuGet.Protocol.");

                var factory = CreateFactory(getDocsDirectory: () => dir);
                var tool = factory.CreateRegistry().Find("search_docs");

                var result = tool.ExecuteAsync(new Newtonsoft.Json.Linq.JObject { ["query"] = "NuGet" }, CancellationToken.None).Result;

                Assert.That(result, Does.Contain("nuget-manager.md"));
                Assert.That(result, Does.Contain("NuGet"));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ReadDocs_ReturnsFullContentFromInjectedDirectory()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airpa_docs_factory_" + System.IO.Path.GetRandomFileName());
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "settings.md"), "# Настройки\n\nХранятся через Properties.Settings.");

                var factory = CreateFactory(getDocsDirectory: () => dir);
                var tool = factory.CreateRegistry().Find("read_docs");

                var result = tool.ExecuteAsync(new Newtonsoft.Json.Linq.JObject { ["path"] = "settings.md" }, CancellationToken.None).Result;

                Assert.That(result, Does.Contain("Properties.Settings"));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        [Test]
        public void SpawnAgent_RegisteredOnlyWhenEnabled()
        {
            var without = CreateFactory(enableSubagents: false).CreateRegistry();
            var with = CreateFactory(enableSubagents: true).CreateRegistry();

            Assert.That(without.Find("spawn_agent"), Is.Null);
            Assert.That(with.Find("spawn_agent"), Is.Not.Null);
        }

        [Test]
        public void ReadFiles_PrefersOpenDocumentBuffer()
        {
            var dir = System.IO.Path.GetTempPath();
            var path = System.IO.Path.Combine(dir, "agent_tool_factory_read_test.txt");
            System.IO.File.WriteAllText(path, "disk content");

            try
            {
                var documents = new FakeDocumentService();
                documents.Buffers[path] = "buffer content";

                var factory = new AgentToolFactory(
                    new StubAiProvider(),
                    () => dir,
                    documents,
                    new FakeBuildFeedbackService(),
                    (q, o, ct) => Task.FromResult("answer"),
                    (sys, task, ct) => Task.FromResult("sub-result"),
                    false,
                    2);

                var tool = factory.CreateRegistry().Find("read_files");
                var result = tool.ExecuteAsync(new Newtonsoft.Json.Linq.JObject { ["path"] = path }, CancellationToken.None).Result;

                Assert.That(result, Does.Contain("buffer content"));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        private sealed class FakeDocumentService : IStudioDocumentService
        {
            public readonly Dictionary<string, string> Buffers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string GetOpenDocumentContent(string path)
            {
                string content;
                return Buffers.TryGetValue(path, out content) ? content : null;
            }

            public string ReadOpenDocumentWindow(string path, int errorLine, int contextLines) => null;

            public bool OpenFile(string path) => false;
        }

        private sealed class FakeBuildFeedbackService : IBuildFeedbackService
        {
            public Task BuildAsync(string configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default) => Task.FromResult(true);

            public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());

            public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default) => Task.FromResult("");
        }
    }
}
