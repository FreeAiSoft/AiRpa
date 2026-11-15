using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Страж: «Открыть активность» для XAML-файла, открытого в редакторе кода, переносит буфер редактора
/// (и признак «изменён») в дизайнер в той же вкладке, а код-документ убирается из модели.
/// Симметрично <see cref="DesignerToCodeTabTests"/> для направления «дизайнер → код».
/// </summary>
public class CodeToDesignerTabTests
{
    [Test]
    public void Main_OpenXamlDesigner_ReusesCodeTabAndCarriesDirty()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "OpenXamlDesigner");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("BeginReplaceCodeTab(filePath)"),
                "открытие в дизайнере помечает вкладку кода для замены");
            Assert.That(method, Does.Contain("OpenOrActivate(filePath, replaceCodeTab ? codeDocument.Text"),
                "дизайнер грузится из буфера редактора кода");
            Assert.That(method, Does.Contain("OpenCodeDocuments.Remove(codeDocument)"),
                "после успешного открытия код-документ убирается из модели");
            Assert.That(method, Does.Contain("designer.IsEdited = true"),
                "признак «изменён» переносится из редактора в дизайнер");
        });
    }

    [Test]
    public void Main_CmdTryLoadFile_RoutesXamlToOpenXamlDesigner()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "CmdTryLoadFile");

        Assert.That(method, Does.Contain("OpenXamlDesigner(filePath)"),
            "ветка .xaml в CmdTryLoadFile обязана идти через OpenXamlDesigner");
    }

    [Test]
    public void MainView_ReplacesCodeTabInPlace()
    {
        string source = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("internal void BeginReplaceCodeTab(string filePath)"),
                "представление должно уметь помечать вкладку кода для замены");
            Assert.That(source, Does.Contain("private LayoutDocument FindCodeTab(string filePath)"),
                "представление находит вкладку кода по ContentId");
            Assert.That(source, Does.Contain("internal LayoutDocument TakeReplaceableCodeTab(string filePath)"),
                "представление забирает помеченную вкладку кода для замены");
            Assert.That(source, Does.Contain("internal void DetachCodeTabHandlers(LayoutDocument document)"),
                "код-обработчики снимаются перед заменой");
            Assert.That(source, Does.Contain("layoutDocument.Closed += CodeTab_Closed;"),
                "обработчик закрытия вкладки кода именованный (для отписки)");
            Assert.That(source, Does.Contain("layoutDocument.IsSelectedChanged += CodeTab_IsSelectedChanged;"),
                "обработчик выбора вкладки кода именованный (для отписки)");
        });
    }

    [Test]
    public void DocGroupHelper_ReusesCodeTabInPlace()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\DocGroupHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_takeReplaceableCodeTab?.Invoke(fullFilePath)"),
                "AddDocument забирает заменяемую вкладку кода по пути файла");
            Assert.That(source, Does.Contain("docDocument.Content = host;"),
                "содержимое вкладки кода заменяется дизайнером");
            Assert.That(source, Does.Contain("docDocument.ContentId = null;"),
                "после замены вкладка больше не идентифицируется как код-вкладка");
            Assert.That(source, Does.Contain("internal void DetachDesignerTabHandlers(LayoutDocument document)"),
                "дизайнер-обработчики снимаются перед заменой дизайнер → код");
        });
    }

    [Test]
    public void LooseXamlDocumentPersistenceService_LoadsFromInitialText()
    {
        string source = ReadSource(@"AiRpaStudio\Core\Workflow\LooseXamlDocumentPersistenceService.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_initialText = initialText;"),
                "текст буфера сохраняется в конструкторе сервиса персистентности");
            Assert.That(source, Does.Contain("_initialText ?? File.ReadAllText(fileName)"),
                "сервис грузит из буфера, а к диску обращается только когда буфера нет");
        });
    }

    [Test]
    public void LooseXamlDesignerLoader_PassesInitialTextToPersistence()
    {
        string source = ReadSource(@"AiRpaStudio\Core\Workflow\LooseXamlDesignerLoader.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("internal static void Open(WorkflowDesigner designer, string sourceFile, WorkflowTypeRegistry registry, string xamlText = null)"),
                "загрузчик принимает текст буфера");
            Assert.That(source, Does.Contain("xamlText);"),
                "текст буфера передаётся в сервис персистентности");
        });
    }

    [Test]
    public void Main_OpenXamlDesigner_CancelsReplacementOnFailure()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "OpenXamlDesigner");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("view.CancelReplaceCodeTab();"),
                "при ошибке открытия ожидающая замена вкладки кода отменяется");
            Assert.That(method, Does.Contain("openResult.Ok && !openResult.AlreadyOpen"),
                "код-документ убирается только после успешного открытия, а не для уже открытого дизайнера");
        });
    }


    [Test]
    public void LoadActivityToDesigner_PassesBufferText()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\LoadActivityToDesignerHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("LooseXamlDesignerLoader.Open(workflowDesignerEx, sourceFile, looseTypeRegistry, xamlText)"),
                "loose-путь получает текст буфера");
            Assert.That(source, Does.Contain("workflowDesignerEx.Text = xamlText;"),
                "не-loose путь грузит дизайнер из текста буфера");
            Assert.That(source, Does.Contain("workflowDesignerEx.Load();"),
                "не-loose путь вызывает Load() без чтения диска");
            Assert.That(source, Does.Contain("new WorkflowFileItem { LoadedFile = sourceFile }"),
                "WorkflowFileItem выставляется вручную при загрузке из буфера");
        });
    }

    private static string MethodBody(string source, string methodName)
    {
        Match match = Regex.Match(
            source,
            @"(override|internal|public|private|protected)[^\r\n]*\b" + Regex.Escape(methodName) + @"\s*\(");

        Assert.That(match.Success, Is.True, $"не найден метод {methodName}");
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
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
