using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи точки сборки состояния вкладок (баг «не персистится состояние вкладок»).
/// <para>
/// Логика формата проверяется в <see cref="WorkflowSessionStateTests"/>, здесь — что студия зовёт
/// сохранение и восстановление в обоих режимах проекта. До исправления сохранение подписывалось
/// <b>только</b> внутри <c>CustomWfDesigner.LoadProject</c> (классический project.json), поэтому в
/// проекте Visual Studio (.sln/.csproj) вкладки не сохранялись, а при старте не восстанавливались.
/// </para>
/// </summary>
public class StudioSessionWiringTests
{
    [Test]
    public void Main_SavesSessionOnAnyTabChange()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("WorkflowDesigners.CollectionChanged += WorkflowTabs_CollectionChanged"),
                "изменение набора вкладок дизайнера должно сохранять сессию");
            Assert.That(source, Does.Contain("OpenCodeDocuments.CollectionChanged += WorkflowTabs_CollectionChanged"),
                "вкладки редактора кода тоже входят в сессию");
            Assert.That(source, Does.Contain("_isRestoringSession"),
                "во время восстановления сохранять нельзя: промежуточное состояние затрёт список вкладок");
        });
    }

    [Test]
    public void Main_RestoresSessionInBothProjectModes()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("private void LoadProjectCore(string path, ProjectTreeBuildResult tree)"),
                "native и VS-проекты обязаны применять дерево единым путём");
            Assert.That(source, Does.Contain("RestoreSessionState(path)"),
                "оба режима восстанавливают вкладки единым вызовом RestoreSessionState(path)");
        });
    }

    [Test]
    public void Restore_DoesNotLaunchExternalApplications()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        // CmdTryLoadFile для неизвестного расширения запускает внешнее приложение: при восстановлении
        // сессии такого быть не должно.
        Assert.That(source, Does.Contain("CodeLanguageProvider.GetLanguageForExtension(document.FilePath)"),
            "восстанавливаем только вкладки дизайнера и известные редактору кода расширения");
    }

    [Test]
    public void MainView_SavesSessionOnClosing()
    {
        string onClosing = MethodBody(ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs"), "OnClosing");

        Assert.That(onClosing, Does.Contain("SaveSessionState"),
            "финальное сохранение при закрытии окна: пользователь мог закрыть студию сразу после правки вкладок");
    }

    [Test]
    public void CustomWfDesigner_DoesNotOwnSessionPersistence()
    {
        string source = ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("RestoreOtherDesigners"),
                "своё восстановление вкладок из LoadProject дублировало сессию и теряло порядок/активную вкладку");
            Assert.That(source, Does.Not.Contain("CollectionChanged -="),
                "подписка-хак внутри LoadProject отключала сохранение на время загрузки проекта");
            Assert.That(source, Does.Not.Contain("Settings.Default.DesignerFiles"),
                "единственный владелец настройки — Main.SaveSessionState, иначе состояние пишут двое");
        });
    }

    [Test]
    public void Main_LoadProjectCore_SetsRestoreFlagBeforeLoadProject()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "LoadProjectCore");

        int flag = method.IndexOf("_isRestoringSession = true;", StringComparison.Ordinal);
        int loadProject = method.IndexOf("CustomWfDesigner.LoadProject(", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(flag, Is.GreaterThanOrEqualTo(0), "восстановление глушится флагом");
            Assert.That(loadProject, Is.GreaterThan(flag),
                "флаг обязан включаться ДО CustomWfDesigner.LoadProject: внутри тот чистит вкладки и вызывает SaveSessionState");
        });
    }

    /// <summary>Тело метода по имени (грубая, но устойчивая к форматированию выборка).</summary>
    private static string MethodBody(string source, string methodName)
    {
        Match match = Regex.Match(
            source,
            @"(override|internal|public|private|protected)[^\r\n]*\b" + Regex.Escape(methodName) + @"\s*\(");

        Assert.That(match.Success, Is.True, $"не найден метод {methodName}");
        return source.Substring(match.Index, Math.Min(1600, source.Length - match.Index));
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
