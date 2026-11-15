using System;
using System.IO;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи регрессов удаления файлов из дерева проекта: физическое удаление дополняется
/// удалением из .csproj и закрытием вкладок.
/// </summary>
public class ProjectDeleteWiringTests
{
    [Test]
    public void ProjectManager_DeleteFile_UnregistersAndRaisesFileDeleting()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\ProjectManager.cs"), "internal void DeleteFile");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("FileDeleting?.Invoke(filePath)"),
                "до удаления подписчики должны получить путь файла (закрытие вкладок)");
            Assert.That(method, Does.Contain("UnregisterFileFromVsProject(filePath)"),
                "после File.Delete элемент должен удаляться из .csproj");
        });
    }

    [Test]
    public void ProjectManager_DeleteFolder_UnregistersEveryFile()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\ProjectManager.cs"), "internal CallResult DeleteFolder");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("CollectFilesInFolder(folderNode)"),
                "перед удалением папки собираются все пути файлов");
            Assert.That(method, Does.Contain("FileDeleting?.Invoke(path)"));
            Assert.That(method, Does.Contain("UnregisterFileFromVsProject(path)"));
        });
    }

    [Test]
    public void ProjectManager_ExposesFileDeletingEvent()
    {
        string source = ReadSource(@"AiRpaStudio\Models\ProjectManager.cs");
        Assert.That(source, Does.Contain("internal event Action<string> FileDeleting;"));
    }

    [Test]
    public void Main_ClosesCodeTabOnFileDeleting()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.That(source, Does.Contain("ProjectManager.FileDeleting += CmdDiscardCodeDocument"),
            "Main должен подписываться на удаление файлов и закрывать вкладку редактора");
    }

    [Test]
    public void VsProjectWriter_HasRemoveFileItem()
    {
        string source = ReadSource(@"AiRpaStudio\VsProject\VsProjectWriter.cs");
        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public static bool RemoveFileItem(string projectPath, string relativePath)"));
            Assert.That(source, Does.Contain("RemoveKeepingIndent"));
        });
    }

    [Test]
    public void ActivityCreator_DoesNotUseActivityCreationService()
    {
        string source = ReadSource(@"AiRpaStudio\Models\ActivityCreator.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("ActivityCreationService"),
                "диалог «Добавить активность» должен создавать XAML, а не вызывать службу создания кодовых активностей");
            Assert.That(source, Does.Not.Contain("ActivityCreateRequest"));
            Assert.That(source, Does.Contain("BuildActivityXaml"));
            Assert.That(source, Does.Contain("GetManifestResourceStream"),
                "шаблон должен читаться из встроенного ресурса");
        });
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodName)
    {
        int signature = source.LastIndexOf(methodName, StringComparison.Ordinal);
        Assert.That(signature, Is.GreaterThanOrEqualTo(0), $"не найден метод {methodName}");

        int start = source.LastIndexOf('\n', signature);
        return source.Substring(start, Math.Min(3000, source.Length - start));
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