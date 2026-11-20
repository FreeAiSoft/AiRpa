using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Страж: «Открыть в редакторе кода» для XAML-активности, открытой в дизайнере, переносит буфер
/// дизайнера (и признак «изменён») в редактор кода в той же вкладке, а дизайнер из вкладки убирается.
/// </summary>
public class DesignerToCodeTabTests
{
    [Test]
    public void Main_OpenInCodeEditor_TransfersDesignerBuffer()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "CmdOpenInCodeEditor");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("GetDesignerByFileIgnoreCase(filePath)"),
                "переключение в код ищет уже открытый дизайнер этого файла");
            Assert.That(method, Does.Contain("designer.Flush()"),
                "буфер дизайнера сериализуется в Text перед переносом");
            Assert.That(method, Does.Contain("designer.Text"),
                "текст редактора кода берётся из сериализованного XAML дизайнера");
            Assert.That(method, Does.Contain("CodeDocument.FromText(filePath, xamlText, designer.IsEdited)"),
                "признак «изменён» переносится из дизайнера в код-документ");
            Assert.That(method, Does.Contain("BeginReplaceDesignerTab(filePath)"),
                "вкладка дизайнера заменяется редактором кода, а не открывается вторая вкладка");
            Assert.That(method, Does.Contain("DetachDesignerForCodeView(filePath)"),
                "дизайнер убирается из модели без закрытия вкладки");
            Assert.That(method, Does.Contain("string.IsNullOrWhiteSpace(xamlText)"),
                "пустой буфер недогруженного дизайнера не открывается как пустой редактор");
            Assert.That(method, Does.Contain("CodeDocument.Load(filePath)"),
                "при пустом буфере XAML читается с диска, а не теряется");
        });
    }

    [Test]
    public void MainView_ReplacesDesignerTabInPlace()
    {
        string source = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("internal void BeginReplaceDesignerTab(string filePath)"),
                "представление должно уметь помечать вкладку для замены");
            Assert.That(source, Does.Contain("private LayoutDocument FindDesignerTab(string filePath)"),
                "представление находит вкладку дизайнера по Tag");
            Assert.That(source, Does.Contain("layoutDocument.Content = view;"),
                "содержимое дизайнера заменяется редактором кода");
            Assert.That(source, Does.Contain("layoutDocument.ContentId = \"code:\" + document.FilePath;"),
                "после замены вкладка идентифицируется как код-вкладка");
        });
    }

    [Test]
    public void DocGroupHelper_NotifiesActiveDocumentChanged()
    {
        string source = ReadSource(@"AiRpaStudio\Helpers\DocGroupHelper.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("activeDocumentChangedDelegate?.Invoke();"),
                "смена активной вкладки обязана пересчитывать кнопки сохранения");
            Assert.That(source, Does.Contain("Action activeDocumentChanged"),
                "делегат передаётся через конструктор DocGroupHelper");
        });
    }

    private static string MethodBody(string source, string methodName)
    {
        Match match = Regex.Match(
            source,
            @"(override|internal|public|private|protected)[^\r\n]*\b" + Regex.Escape(methodName) + @"\s*\(");

        Assert.That(match.Success, Is.True, $"не найден метод {methodName}");
        return source.Substring(match.Index, Math.Min(6000, source.Length - match.Index));
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
