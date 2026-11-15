using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Чистая логика <see cref="ProjectUnsavedBuffersStore"/>: per-project персист несохранённых
/// буферов закрытых документов.
/// </summary>
public class ProjectUnsavedBuffersStoreTests
{
    private const string Project = @"C:\Projects\Demo";

    private static ProjectUnsavedBufferEntry Entry(string path, string text, string kind)
    {
        return new ProjectUnsavedBufferEntry { FilePath = path, Text = text, Kind = kind };
    }

    [Test]
    public void Set_Get_RoundTrip_KeepsBuffersAndKinds()
    {
        string json = ProjectUnsavedBuffersStore.Set(null, Project, new[]
        {
            Entry(@"C:\Projects\Demo\A.xaml", "xaml", "workflow"),
            Entry(@"C:\Projects\Demo\B.cs", "code", "code")
        });

        System.Collections.Generic.IReadOnlyList<ProjectUnsavedBufferEntry> buffers =
            ProjectUnsavedBuffersStore.Get(json, Project);

        Assert.Multiple(() =>
        {
            Assert.That(buffers.Count, Is.EqualTo(2));
            Assert.That(buffers[0].FilePath, Is.EqualTo(@"C:\Projects\Demo\A.xaml"));
            Assert.That(buffers[0].Kind, Is.EqualTo("workflow"));
            Assert.That(buffers[1].Text, Is.EqualTo("code"));
            Assert.That(buffers[1].Kind, Is.EqualTo("code"));
        });
    }

    [Test]
    public void Get_Missing_ReturnsEmpty()
    {
        Assert.That(ProjectUnsavedBuffersStore.Get(null, Project), Is.Empty);
    }

    [Test]
    public void Set_EmptyList_RemovesEntry()
    {
        string json = ProjectUnsavedBuffersStore.Set(null, Project, new[]
        {
            Entry(@"C:\Projects\Demo\A.xaml", "x", "workflow")
        });

        json = ProjectUnsavedBuffersStore.Set(json, Project, System.Array.Empty<ProjectUnsavedBufferEntry>());

        Assert.That(ProjectUnsavedBuffersStore.Get(json, Project), Is.Empty);
    }

    [Test]
    public void Parse_BrokenJson_ReturnsEmpty()
    {
        Assert.That(ProjectUnsavedBuffersStore.Parse("{not json"), Is.Empty);
    }

    [Test]
    public void Entry_ToBuffer_RestoresKind()
    {
        UnsavedDocumentBuffer buffer = Entry(@"C:\Projects\Demo\A.xaml", "x", "workflow").ToBuffer();

        Assert.Multiple(() =>
        {
            Assert.That(buffer.FilePath, Is.EqualTo(@"C:\Projects\Demo\A.xaml"));
            Assert.That(buffer.Text, Is.EqualTo("x"));
            Assert.That(buffer.IsWorkflow, Is.True);
        });
    }
}
