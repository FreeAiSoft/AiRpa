using System.IO;
using System.Linq;
using NUnit.Framework;
using Agent.Core.Services;

namespace AiRpaStudio.Tests
{
    [TestFixture]
    public class DocumentationIndexTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "airpa_docs_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_dir, "ru"));
            Directory.CreateDirectory(Path.Combine(_dir, "en"));

            File.WriteAllText(Path.Combine(_dir, "00-index.md"), "# Справка AiRpaStudio\n\nОбзор документации.");
            File.WriteAllText(Path.Combine(_dir, "ru", "workflow-designer.md"), "# Дизайнер рабочих процессов\n\nДизайнер построен на WorkflowDesigner.");
            File.WriteAllText(Path.Combine(_dir, "ru", "mcp-server.md"), "# MCP-сервер\n\nMCP-сервер запускается через --mcp-server.");
            File.WriteAllText(Path.Combine(_dir, "en", "workflow-designer.md"), "# Workflow Designer\n\nThe designer is built on WorkflowDesigner.");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void Build_IndexesAllMarkdownFilesRecursively()
        {
            var index = DocumentationIndex.Build(_dir);

            Assert.That(index.IsEmpty, Is.False);
            Assert.That(index.Entries.Count, Is.EqualTo(4));
            Assert.That(index.Entries.Select(e => e.RelativePath),
                Is.EquivalentTo(new[] { "00-index.md", "ru/workflow-designer.md", "ru/mcp-server.md", "en/workflow-designer.md" }));
        }

        [Test]
        public void Build_ParsesTitleFromFirstHeading()
        {
            var index = DocumentationIndex.Build(_dir);

            var ruDesigner = index.Entries.First(e => e.RelativePath == "ru/workflow-designer.md");
            Assert.That(ruDesigner.Title, Is.EqualTo("Дизайнер рабочих процессов"));

            var enDesigner = index.Entries.First(e => e.RelativePath == "en/workflow-designer.md");
            Assert.That(enDesigner.Title, Is.EqualTo("Workflow Designer"));
        }

        [Test]
        public void Build_MissingDirectory_ReturnsEmpty()
        {
            var index = DocumentationIndex.Build(Path.Combine(_dir, "does-not-exist"));

            Assert.That(index.IsEmpty, Is.True);
            Assert.That(index.Entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void Search_TitleMatchRanksAboveBodyOnlyMatch()
        {
            var index = DocumentationIndex.Build(_dir);

            // "дизайнер" appears in the ru title AND body; "mcp" only in title of mcp-server.
            var designerHits = index.Search("дизайнер");
            Assert.That(designerHits, Is.Not.Empty);
            Assert.That(designerHits[0].RelativePath, Is.EqualTo("ru/workflow-designer.md"));
        }

        [Test]
        public void Search_ReturnsSnippetWithContext()
        {
            var index = DocumentationIndex.Build(_dir);

            var hits = index.Search("WorkflowDesigner");
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].Snippet, Does.Contain("WorkflowDesigner"));
        }

        [Test]
        public void Search_NoMatch_ReturnsEmpty()
        {
            var index = DocumentationIndex.Build(_dir);

            Assert.That(index.Search("zzzznomatch"), Is.Empty);
        }

        [Test]
        public void Read_ReturnsFullContent()
        {
            var index = DocumentationIndex.Build(_dir);

            var content = index.Read("ru/mcp-server.md");

            Assert.That(content, Is.Not.Null);
            Assert.That(content, Does.Contain("--mcp-server"));
        }

        [Test]
        public void Read_AcceptsBackslashSeparators()
        {
            var index = DocumentationIndex.Build(_dir);

            Assert.That(index.Read("ru\\mcp-server.md"), Does.Contain("MCP-сервер"));
        }

        [Test]
        public void Read_UnknownPath_ReturnsNull()
        {
            var index = DocumentationIndex.Build(_dir);

            Assert.That(index.Read("ru/unknown.md"), Is.Null);
        }

        [Test]
        public void BuildReference_ListsTitlesAndPaths()
        {
            var index = DocumentationIndex.Build(_dir);

            var reference = index.BuildReference();

            Assert.That(reference, Does.Contain("ru/workflow-designer.md"));
            Assert.That(reference, Does.Contain("Дизайнер рабочих процессов"));
            Assert.That(reference, Does.Contain("en/workflow-designer.md"));
            Assert.That(reference, Does.Contain("Workflow Designer"));
        }

        [Test]
        public void BuildReference_EmptyIndex_ReturnsEmpty()
        {
            Assert.That(DocumentationIndex.Empty.BuildReference(), Is.Empty);
        }
    }
}
