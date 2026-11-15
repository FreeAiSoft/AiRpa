using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи: несохранённые буферы закрытых документов (редакторы кода/XAML и дизайнеры WF)
/// складываются в <c>UnsavedDocumentBuffers</c>, переносятся обратно при повторном открытии и
/// пишутся в файл при «Сохранить всё».
/// </summary>
public class UnsavedDocumentBufferWiringTests
{
    [Test]
    public void Main_CmdCloseCodeDocument_StashesDirtyBuffer()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public void CmdCloseCodeDocument");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("document.IsDirty"), "кладётся только изменённый буфер");
            Assert.That(method, Does.Contain("UnsavedDocumentBuffers.Set"), "закрытие изменённой вкладки кода кладёт буфер в коллекцию");
        });
    }

    [Test]
    public void Main_CmdDiscardCodeDocument_DoesNotStash()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public void CmdDiscardCodeDocument");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Not.Contain("UnsavedDocumentBuffers.Set"), "удаление файла не сохраняет буфер");
            Assert.That(method, Does.Contain("UnsavedDocumentBuffers.Remove"), "удаление файла убирает буфер из коллекции");
        });
    }

    [Test]
    public void Main_FileDeleting_SubscribesDiscard()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");
        Assert.That(source, Does.Contain("ProjectManager.FileDeleting += CmdDiscardCodeDocument"),
            "при удалении файла вкладка кода должна закрываться через discard");
    }

    [Test]
    public void Main_CmdOpenInCodeEditor_LoadsStashedBuffer()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public void CmdOpenInCodeEditor");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("UnsavedDocumentBuffers.TryGet"), "повторное открытие читает буфер из коллекции");
            Assert.That(method, Does.Contain("CodeDocument.FromText(filePath, stashed.Text, isDirty: true)"), "буфер переносится как изменённый");
            Assert.That(method, Does.Contain("UnsavedDocumentBuffers.Remove(filePath)"), "после переноса запись из коллекции удаляется");
        });
    }

    [Test]
    public void Main_OpenXamlDesigner_LoadsStashedBuffer()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "private DesignerOpenResult OpenXamlDesigner");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("stashed.Text"), "дизайнер открывается из несохранённого буфера");
            Assert.That(method, Does.Contain("designer.IsEdited = true"), "признак «изменён» переносится в дизайнер");
            Assert.That(method, Does.Contain("UnsavedDocumentBuffers.Remove(filePath)"), "после открытия буфер удаляется из коллекции");
        });
    }

    [Test]
    public void CustomWfDesigner_RemoveDesignerByLoadedFile_StashesEditedDesigner()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "internal void RemoveDesignerByLoadedFile");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("stashUnsaved"), "закрытие вкладки дизайнера поддерживает сохранение буфера");
            Assert.That(method, Does.Contain("StashUnsavedDesigner"), "буфер дизайнера сериализуется в коллекцию");
        });

        string helper = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "private void StashUnsavedDesigner");
        Assert.Multiple(() =>
        {
            Assert.That(helper, Does.Contain("designer.Flush()"), "перед сохранением буфер дизайнера сериализуется");
            Assert.That(helper, Does.Contain("UnsavedDocumentBuffers.Set"), "буфер кладётся в коллекцию несохранённых");
        });
    }

    [Test]
    public void Main_SaveAllChanges_WritesStashedBuffers()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public void SaveAllChanges()");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("UnsavedDocumentBuffers.Snapshot()"), "«Сохранить всё» перебирает закрытые буферы");
            Assert.That(method, Does.Contain("File.WriteAllText(buffer.FilePath, buffer.Text"), "закрытый буфер пишется в файл");
            Assert.That(method, Does.Contain("UnsavedDocumentBuffers.Remove(buffer.FilePath)"), "после записи буфер удаляется из коллекции");
        });
    }

    [Test]
    public void Main_HasUnsavedChanges_IncludesStashedBuffers()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");
        int index = source.IndexOf("public bool HasUnsavedChanges", StringComparison.Ordinal);
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        string block = source.Substring(index, 400);

        Assert.That(block, Does.Contain("UnsavedDocumentBuffers.Count > 0"),
            "закрытый несохранённый буфер делает проект «изменённым»");
    }

    [Test]
    public void Main_CanSaveAll_IncludesStashedBuffers()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public bool CanSaveAll");

        Assert.That(method, Does.Contain("UnsavedDocumentBuffers.Count > 0"),
            "«Сохранить всё» активно, когда есть несохранённый буфер закрытого документа");
    }

    [Test]
    public void Main_ConfirmSaveOrDiscard_Discard_ClearsStash()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public bool ConfirmSaveOrDiscardChanges()");

        int discard = method.IndexOf("UnsavedChangesResult.Discard", StringComparison.Ordinal);
        Assert.That(discard, Is.GreaterThanOrEqualTo(0));
        string afterDiscard = method.Substring(discard, 120);
        Assert.That(afterDiscard, Does.Contain("UnsavedDocumentBuffers.Clear()"),
            "«Не сохранять» очищает коллекцию несохранённых буферов");
    }

    [Test]
    public void ProjectFile_IncludesUnsavedDocumentBufferStore()
    {
        string csproj = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");
        Assert.That(csproj, Does.Contain(@"Core\UnsavedDocumentBufferStore.cs"), "новый компонент не добавлен в проект");
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodSignature)
    {
        Match match = Regex.Match(source, Regex.Escape(methodSignature));
        Assert.That(match.Success, Is.True, $"не найден метод {methodSignature}");
        return source.Substring(match.Index, Math.Min(2500, source.Length - match.Index));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
