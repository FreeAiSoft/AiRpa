using System.IO;
using NUnit.Framework;
using Agent.Core.Services;

namespace AiRpaStudio.Tests
{
    [TestFixture]
    public class DocumentationViewerTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "airpa_viewer_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_dir, "ru"));
            Directory.CreateDirectory(Path.Combine(_dir, "en"));

            File.WriteAllText(Path.Combine(_dir, "ru", "index.md"), "# Справка\n[Дизайнер](workflow-designer.md)");
            File.WriteAllText(Path.Combine(_dir, "ru", "workflow-designer.md"), "# Дизайнер\nсодержимое");
            File.WriteAllText(Path.Combine(_dir, "en", "index.md"), "# Help\n[Designer](workflow-designer.md)");
            File.WriteAllText(Path.Combine(_dir, "en", "workflow-designer.md"), "# Designer\ncontent");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void ReadIndex_ReturnsIndexOfRequestedLocale()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ReadIndex(), Does.Contain("# Справка"));
        }

        [Test]
        public void ReadIndex_FallsBackToOtherLocale_WhenRequestedIndexMissing()
        {
            File.Delete(Path.Combine(_dir, "en", "index.md"));

            var viewer = new DocumentationViewer(_dir, "en");

            Assert.That(viewer.ReadIndex(), Does.Contain("# Справка"));
        }

        [Test]
        public void ResolveLink_ResolvesRelativeFileWithinLocale()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ResolveLink("index.md", "workflow-designer.md"),
                Is.EqualTo("workflow-designer.md"));
        }

        [Test]
        public void ResolveLink_RejectsExternalLinks()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ResolveLink("index.md", "https://example.com/x.md"), Is.Null);
            Assert.That(viewer.ResolveLink("index.md", "mailto:a@b.c"), Is.Null);
        }

        [Test]
        public void ResolveLink_RejectsNonMarkdown()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ResolveLink("index.md", "file.txt"), Is.Null);
        }

        [Test]
        public void ResolveLink_RejectsPathTraversal()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ResolveLink("index.md", "../en/workflow-designer.md"), Is.Null);
            Assert.That(viewer.ResolveLink("index.md", "..\\..\\secret.md"), Is.Null);
        }

        [Test]
        public void ResolveLink_RejectsMissingFile()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ResolveLink("index.md", "missing.md"), Is.Null);
        }

        [Test]
        public void ResolveLink_StripsAnchor()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ResolveLink("index.md", "workflow-designer.md#section"),
                Is.EqualTo("workflow-designer.md"));
        }

        [Test]
        public void ReadDocument_ReturnsContent()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ReadDocument("workflow-designer.md"), Does.Contain("# Дизайнер"));
        }

        [Test]
        public void ReadDocument_MissingReturnsEmpty()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ReadDocument("missing.md"), Is.Empty);
        }

        [Test]
        public void ReadDocument_RejectsTraversal()
        {
            var viewer = new DocumentationViewer(_dir, "ru");

            Assert.That(viewer.ReadDocument("../en/workflow-designer.md"), Is.Empty);
        }
    }
}
