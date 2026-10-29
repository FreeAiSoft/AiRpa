using System.IO;
using System.Text;
using RPA.Models;

namespace AiRpaStudio.Tests;

public class CodeDocumentTests
{
    private string tempDir;
    private string tempFile;

    [SetUp]
    public void Setup()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "CodeDocumentTests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        tempFile = Path.Combine(tempDir, "sample.xaml");
    }

    [TearDown]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Load_ReadsFileAndIsNotDirty()
    {
        File.WriteAllText(tempFile, "<Grid/>", new UTF8Encoding(true));

        CodeDocument document = CodeDocument.Load(tempFile);

        Assert.That(document.Text, Is.EqualTo("<Grid/>"));
        Assert.That(document.IsDirty, Is.False);
        Assert.That(document.Title, Is.EqualTo("sample.xaml"));
        Assert.That(document.Language, Is.EqualTo("XAML"));
    }

    [Test]
    public void Text_Setter_MarksDirty()
    {
        File.WriteAllText(tempFile, "<Grid/>");

        CodeDocument document = CodeDocument.Load(tempFile);
        document.Text = "<Grid Background=\"Red\"/>";

        Assert.That(document.IsDirty, Is.True);
    }

    [Test]
    public void Save_WritesTextAndClearsDirty()
    {
        File.WriteAllText(tempFile, "<Grid/>");

        CodeDocument document = CodeDocument.Load(tempFile);
        document.Text = "<Grid Background=\"Red\"/>";
        document.Save();

        Assert.That(document.IsDirty, Is.False);
        Assert.That(File.ReadAllText(tempFile), Is.EqualTo("<Grid Background=\"Red\"/>"));
    }

    [Test]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => CodeDocument.Load(Path.Combine(tempDir, "missing.xaml")));
    }
}
