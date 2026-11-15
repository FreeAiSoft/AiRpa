using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Страж: звёздочка у вкладки редактора кода и кнопки сохранения учитывают один и тот же
/// <c>CodeDocument</c>. Раньше редактор писал правки во «внутренний» документ (<c>CodeDocument.Load</c>
/// внутри <c>CodeEditorView.LoadFile</c>), а вкладка и модель следили за другим экземпляром — звёздочка
/// не появлялась, а «Сохранить всё» не видел правку.
/// </summary>
public class CodeEditorDirtyTrackingTests
{
    [Test]
    public void MainView_AddCodeDocumentTab_UsesLoadDocument()
    {
        string source = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("view.LoadDocument(document)"),
                "редактор обязан подключаться к документу из OpenCodeDocuments, а не читать диск заново");
            Assert.That(source, Does.Contain("new Binding(\"IsDirty\") { Source = document }"),
                "заголовок вкладки обязан следить за IsDirty того же документа");
        });
    }

    [Test]
    public void CodeEditorView_LoadDocument_SetsSharedDocument()
    {
        string source = ReadSource(@"AiRpaStudio\Controls\CodeEditorView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public void LoadDocument(CodeDocument document)"),
                "редактор должен уметь подключаться к существующему документу");
            Assert.That(source, Does.Contain("Document = document;"),
                "LoadDocument подключает переданный экземпляр CodeDocument");
        });
    }

    [Test]
    public void MainViewModel_SaveButtons_ReadMainPolicy()
    {
        string source = ReadSource(@"AiRpaStudio\ViewModels\MainViewModel.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("public bool CanSave => main.CanSave;"),
                "CanSave обязан вычисляться моделью Main (дизайнеры + редакторы кода)");
            Assert.That(source, Does.Contain("public bool CanSaveAll => main.CanSaveAll;"),
                "CanSaveAll обязан вычисляться моделью Main");
        });
    }

    [Test]
    public void Main_Save_SavesActiveCodeDocument()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "CmdWorkflowSave");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("ActiveCodeDocument()"),
                "активная вкладка кода должна сохраняться той же командой «Сохранить»");
            Assert.That(method, Does.Contain("SaveCodeDocument(activeCode)"),
                "сохранение активного код-документа идёт через обёртку с обработкой ошибок");
            Assert.That(method, Does.Contain("CustomWfDesigner.WorkflowSave()"),
                "для активной вкладки дизайнера сохраняется дизайнер");
        });
    }

    [Test]
    public void Main_SaveAll_DelegatesToSaveAllChanges()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "CmdWorkflowSaveAll");

        Assert.That(method, Does.Contain("SaveAllChanges()"),
            "«Сохранить всё» обязан сохранять и дизайнеры, и редакторы кода через SaveAllChanges");
    }

    [Test]
    public void Main_SubscribesToCodeDocumentChanges()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("OpenCodeDocuments.CollectionChanged += OpenCodeDocuments_CollectionChanged_ForSave"),
                "Main обязан следить за набором открытых код-документов");
            Assert.That(source, Does.Contain("document.PropertyChanged += CodeDocument_PropertyChanged"),
                "Main обязан следить за IsDirty каждого открытого код-документа");
            Assert.That(source, Does.Contain("e.PropertyName == nameof(CodeDocument.IsDirty)"),
                "пересчёт кнопок запускается только при изменении грязности");
        });
    }

    private static string MethodBody(string source, string methodName)
    {
        Match match = Regex.Match(
            source,
            @"(override|internal|public|private|protected)[^\r\n]*\b" + Regex.Escape(methodName) + @"\s*\(");

        Assert.That(match.Success, Is.True, $"не найден метод {methodName}");
        return source.Substring(match.Index, Math.Min(2000, source.Length - match.Index));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден файл {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
