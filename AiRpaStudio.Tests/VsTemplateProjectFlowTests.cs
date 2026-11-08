using System;
using System.IO;

namespace AiRpaStudio.Tests;

/// <summary>
/// Поток создания проекта из VS-шаблона: после успешного создания проект сразу загружается
/// в студию, без промежуточного окна «открыть в Visual Studio / загрузить в студию».
/// </summary>
[TestFixture]
public class VsTemplateProjectFlowTests
{
    [Test]
    public void CmdVsNewProjectFromTemplate_LoadsProjectIntoStudioWithoutPrompt()
    {
        string code = ReadSource(@"AiRpaStudio\Models\Main.cs");
        int methodStart = code.IndexOf("public void CmdVsNewProjectFromTemplate", StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThan(0), "в Main.cs нет метода CmdVsNewProjectFromTemplate");

        int nextMethod = code.IndexOf("public void ", methodStart + 1, StringComparison.Ordinal);
        string method = nextMethod > 0
            ? code.Substring(methodStart, nextMethod - methodStart)
            : code.Substring(methodStart);

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("LoadVsProjectIntoStudio(creator.ProjectFilePath)"),
                "после создания проект должен сразу загружаться в студию");
            Assert.That(method, Does.Not.Contain("OfferVsProjectActions"),
                "промежуточное окно с выбором действия не нужно");
        });
    }

    [Test]
    public void OfferVsProjectActions_IsRemoved()
    {
        string code = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Not.Contain("OfferVsProjectActions"),
                "метод OfferVsProjectActions (MessageBox после шаблона) должен быть удалён");
            Assert.That(code, Does.Not.Contain("lang_vs_project_created"),
                "подпись окна «Проект создан» больше не используется в Main.cs");
        });
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
