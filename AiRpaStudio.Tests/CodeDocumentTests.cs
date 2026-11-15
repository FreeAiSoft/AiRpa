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

    [Test]
    public void FromText_CarriesTextAndDirtyState()
    {
        CodeDocument document = CodeDocument.FromText(tempFile, "<Grid Background=\"Red\"/>", isDirty: true);

        Assert.Multiple(() =>
        {
            Assert.That(document.Text, Is.EqualTo("<Grid Background=\"Red\"/>"));
            Assert.That(document.IsDirty, Is.True);
            Assert.That(document.Title, Is.EqualTo("sample.xaml"));
            Assert.That(document.Language, Is.EqualTo("XAML"));
        });
    }

    [Test]
    public void FromText_CleanDocument_IsNotDirty()
    {
        CodeDocument document = CodeDocument.FromText(tempFile, "<Grid/>", isDirty: false);

        Assert.That(document.IsDirty, Is.False);
    }

    [Test]
    public void FromText_NullPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => CodeDocument.FromText(null, "text", isDirty: false));
    }

    [Test]
    public void Save_WritesUtf8WithoutBom()
    {
        File.WriteAllText(tempFile, "<Grid/>");

        CodeDocument document = CodeDocument.Load(tempFile);
        document.Text = "<Grid Background=\"Red\"/>";
        document.Save();

        byte[] bytes = File.ReadAllBytes(tempFile);
        Assert.Multiple(() =>
        {
            Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(bytes[0], Is.Not.EqualTo(0xEF), "BOM (EF BB BF) запрещён: файлы студии — UTF-8 без BOM");
            Assert.That(bytes[1], Is.Not.EqualTo(0xBB), "BOM (EF BB BF) запрещён");
            Assert.That(bytes[2], Is.Not.EqualTo(0xBF), "BOM (EF BB BF) запрещён");
        });
    }

}
