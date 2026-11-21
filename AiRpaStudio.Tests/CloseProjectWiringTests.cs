using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи функции «Закрыть проект»: per-project сессия и буферы, очистка вкладок, пункт меню,
/// точки интеграции в <c>Main</c>/<c>CustomWfDesigner</c>.
/// </summary>
public class CloseProjectWiringTests
{
    [Test]
    public void Main_SaveSessionState_WritesPerProjectSession()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "internal void SaveSessionState()");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("ProjectSessionStore.Set"),
                "сессия текущего проекта обязана попадать в per-project store");
            Assert.That(method, Does.Contain("Properties.Settings.Default.DesignerFiles"),
                "настройка DesignerFiles остаётся fallback-ом последнего проекта");
        });
    }

    [Test]
    public void Main_RestoreSessionState_ReadsPerProjectSessionAndBuffers()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "internal void RestoreSessionState(string projectPath)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("ProjectSessionStore.TryGet"),
                "восстановление обязано читать сессию из per-project store");
            Assert.That(method, Does.Contain("LoadProjectBuffers(projectPath)"),
                "при восстановлении загружаются несохранённые буферы проекта");
            Assert.That(method, Does.Contain("ReopenOpenDocumentsWithBuffers()"),
                "уже открытая главная активность переоткрывается из буфера");
        });
    }

    [Test]
    public void Main_CloseProjectCore_ClearsTabsAndStoresBuffers()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "internal bool CloseProjectCore(CloseProjectChangesResult? choice)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("CaptureProjectBuffers(projectPath)"),
                "«Сохранить в буфер» сохраняет несохранённое в per-project store");
            Assert.That(method, Does.Contain("OpenCodeDocuments.Clear()"),
                "закрытие проекта обязано закрывать вкладки редактора кода/XAML");
            Assert.That(method, Does.Contain("CustomWfDesigner.ClearProject()"),
                "закрытие проекта обязано очищать вкладки дизайнера WF");
            Assert.That(method, Does.Contain("_isClosingProject = true"),
                "во время закрытия запись сессии глушится, чтобы очистка не затёрла сохранённое");
        });
    }

    [Test]
    public void Main_CaptureProjectBuffers_UsesPerProjectStore()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "private void CaptureProjectBuffers(string projectPath)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("ProjectUnsavedBuffersStore.Set"),
                "буферы пишутся в per-project store");
            Assert.That(method, Does.Contain("TryCaptureUnsavedBuffer"),
                "изменённые дизайнеры сериализуются без записи в общую коллекцию");
            Assert.That(method, Does.Contain("OpenCodeDocuments.Where(d => d.IsDirty)"),
                "изменённые редакторы кода/XAML попадают в буфер");
        });
    }

    [Test]
    public void CustomWfDesigner_HasClearProjectAndBufferCapture()
    {
        string source = ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("internal void ClearProject()"),
                "дизайнер обязан уметь полностью очистить состояние проекта");
            Assert.That(source, Does.Contain("internal bool TryCaptureUnsavedBuffer(WorkflowDesignerEx designer, out UnsavedDocumentBuffer buffer)"),
                "должен быть способ сериализовать дизайнер в буфер без общей коллекции");
            Assert.That(source, Does.Contain("ClearProject();"),
                "LoadProject переиспользует единую очистку проекта");
        });
    }


    [Test]
    public void CustomWfDesigner_ClearProject_ClosesDesignersIndividually()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Core\CustomWfDesigner.cs"), "internal void ClearProject()");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("RemoveDesignerByLoadedFile(loadedFile, stashUnsaved: false)"),
                "вкладки дизайнера закрываются штатным путём (DetachValidation + закрытие UI-вкладки)");
            Assert.That(method, Does.Contain(".Select(GetLoadedFile)"),
                "файлы для закрытия берутся из открытых дизайнеров");
        });
    }

    [Test]
    public void MainView_FileMenu_ContainsCloseProjectItem()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\MainView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("lang_main_close_project"), "пункт меню «Закрыть проект» не локализован");
            Assert.That(xaml, Does.Contain("CommandParameter=\"CmdCloseProject\""), "пункт меню не вызывает команду закрытия проекта");
        });
    }

    [Test]
    public void Settings_ContainPerProjectStores()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReadSource(@"AiRpaStudio\Properties\Settings.settings"), Does.Contain("ProjectSessions"));
            Assert.That(ReadSource(@"AiRpaStudio\Properties\Settings.settings"), Does.Contain("ProjectUnsavedBuffers"));
            Assert.That(ReadSource(@"AiRpaStudio\App.config"), Does.Contain("ProjectSessions"));
            Assert.That(ReadSource(@"AiRpaStudio\App.config"), Does.Contain("ProjectUnsavedBuffers"));
            Assert.That(ReadSource(@"AiRpaStudio\Properties\Settings.Designer.cs"), Does.Contain("public string ProjectSessions"));
            Assert.That(ReadSource(@"AiRpaStudio\Properties\Settings.Designer.cs"), Does.Contain("public string ProjectUnsavedBuffers"));
        });
    }

    [Test]
    public void Resources_ContainCloseProjectKeys()
    {
        string neutral = ReadSource(@"AiRpaStudio\Resources\Main.resx");
        string russian = ReadSource(@"AiRpaStudio\Resources\Main.ru-RU.resx");

        foreach (string key in new[]
                 {
                     "lang_main_close_project",
                     "lang_close_project_title",
                     "lang_close_project_message",
                     "lang_close_project_save",
                     "lang_close_project_buffer",
                     "lang_close_project_cancel"
                 })
        {
            Assert.Multiple(() =>
            {
                Assert.That(neutral, Does.Contain(key), $"нет ключа {key} в Main.resx");
                Assert.That(russian, Does.Contain(key), $"нет ключа {key} в Main.ru-RU.resx");
            });
        }
    }

    private static string MethodBody(string source, string methodSignature)
    {
        Match match = Regex.Match(source, Regex.Escape(methodSignature));
        Assert.That(match.Success, Is.True, "не найден метод " + methodSignature);
        return source.Substring(match.Index, Math.Min(2200, source.Length - match.Index));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, "не найден файл " + relativePath);
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
