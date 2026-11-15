using System;
using System.IO;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Поведение коллекции несохранённых буферов закрытых документов
/// (<see cref="UnsavedDocumentBufferStore"/>): чистые правила без WPF/WF.
/// </summary>
public class UnsavedDocumentBufferStoreTests
{
    private string tempDir;

    [SetUp]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "UnsavedBufferStoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    private string PathOf(string name) => Path.Combine(tempDir, name);

    [Test]
    public void Set_TryGet_RoundTripsBuffer()
    {
        var store = new UnsavedDocumentBufferStore();
        string path = PathOf("a.xaml");

        store.Set(path, "<Grid/>", UnsavedDocumentBufferKind.Code);

        Assert.That(store.TryGet(path, out UnsavedDocumentBuffer buffer), Is.True);
        Assert.That(buffer.Text, Is.EqualTo("<Grid/>"));
        Assert.That(buffer.Kind, Is.EqualTo(UnsavedDocumentBufferKind.Code));
        Assert.That(buffer.IsCode, Is.True);
        Assert.That(buffer.IsWorkflow, Is.False);
    }

    [Test]
    public void Key_IsCaseInsensitive()
    {
        var store = new UnsavedDocumentBufferStore();
        string path = PathOf("Sample.XAML");
        store.Set(path, "text", UnsavedDocumentBufferKind.Workflow);

        Assert.That(store.TryGet(path.ToUpperInvariant(), out _), Is.True);
        Assert.That(store.Contains(path.ToLowerInvariant()), Is.True);
    }

    [Test]
    public void Set_OverwritesExistingBuffer()
    {
        var store = new UnsavedDocumentBufferStore();
        string path = PathOf("b.txt");
        store.Set(path, "first", UnsavedDocumentBufferKind.Code);
        store.Set(path, "second", UnsavedDocumentBufferKind.Workflow);

        Assert.That(store.Count, Is.EqualTo(1));
        Assert.That(store.TryGet(path, out UnsavedDocumentBuffer buffer), Is.True);
        Assert.That(buffer.Text, Is.EqualTo("second"));
        Assert.That(buffer.Kind, Is.EqualTo(UnsavedDocumentBufferKind.Workflow));
    }

    [Test]
    public void Remove_RemovesBuffer()
    {
        var store = new UnsavedDocumentBufferStore();
        string path = PathOf("c.txt");
        store.Set(path, "text", UnsavedDocumentBufferKind.Code);

        Assert.That(store.Remove(path), Is.True);
        Assert.That(store.Count, Is.Zero);
        Assert.That(store.Remove(path), Is.False);
    }

    [Test]
    public void Snapshot_ReturnsCopy()
    {
        var store = new UnsavedDocumentBufferStore();
        store.Set(PathOf("a"), "a", UnsavedDocumentBufferKind.Code);
        store.Set(PathOf("b"), "b", UnsavedDocumentBufferKind.Code);

        var snapshot = store.Snapshot();
        Assert.That(snapshot.Count, Is.EqualTo(2));

        store.Clear();

        Assert.That(snapshot.Count, Is.EqualTo(2), "снимок не должен меняться после очистки store");
        Assert.That(store.Count, Is.Zero);
    }

    [Test]
    public void Clear_RemovesAll()
    {
        var store = new UnsavedDocumentBufferStore();
        store.Set(PathOf("a"), "a", UnsavedDocumentBufferKind.Code);
        store.Clear();

        Assert.That(store.Count, Is.Zero);
    }

    [Test]
    public void Set_RelativePath_Throws()
    {
        var store = new UnsavedDocumentBufferStore();
        Assert.Throws<ArgumentException>(() => store.Set("relative.txt", "x", UnsavedDocumentBufferKind.Code));
    }

    [Test]
    public void Set_EmptyPath_Throws()
    {
        var store = new UnsavedDocumentBufferStore();
        Assert.Throws<ArgumentException>(() => store.Set("", "x", UnsavedDocumentBufferKind.Code));
    }

    [Test]
    public void TryGet_RelativePath_ReturnsFalse()
    {
        var store = new UnsavedDocumentBufferStore();
        Assert.That(store.TryGet("relative.txt", out _), Is.False);
    }
}
