using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи пункта меню «Файл → Недавние проекты»: подменю с тултипом полного пути,
/// команда открытия с подтверждением несохранённых изменений, запись списка при загрузке,
/// локализация и наличие настройки.
/// </summary>
public class RecentProjectsMenuTests
{
    [Test]
    public void MainView_FileMenu_ContainsRecentProjectsSubmenuWithTooltip()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\MainView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("lang_main_recent_projects"), "пункт меню не локализован");
            Assert.That(xaml, Does.Contain("ItemsSource=\"{Binding RecentProjects}\""), "подменю не привязано к списку недавних проектов");
            Assert.That(xaml, Does.Contain("<Setter Property=\"ToolTip\" Value=\"{Binding Path}\" />"), "тултип должен показывать полный путь");
            Assert.That(xaml, Does.Contain("Event=\"Click\" Handler=\"RecentProjectMenuItem_Click\""), "клик по пункту не обрабатывается");
        });
    }

    [Test]
    public void Main_OpenRecentProject_ConfirmsAndLoads()
    {
        string method = MethodBody(ReadSource(@"AiRpaStudio\Models\Main.cs"), "public void CmdOpenRecentProject(string path)");

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("CloseProject()"), "открытие недавнего проекта обязано закрыть текущий проект с проверкой несохранённых изменений");
            Assert.That(method, Does.Contain("LoadProject(path)"), "команда должна грузить проект по выбранному пути");
        });
    }

    [Test]
    public void Main_LoadProjectCore_RecordsRecentProject()
    {
        string source = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.That(source, Does.Contain("RecentProjectsStore.Add(Properties.Settings.Default.RecentProjects, path)"),
            "при загрузке проекта путь обязан попадать в список недавних");
    }

    [Test]
    public void Resources_ContainRecentProjectsKey()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReadSource(@"AiRpaStudio\Resources\Main.resx"), Does.Contain("lang_main_recent_projects"));
            Assert.That(ReadSource(@"AiRpaStudio\Resources\Main.ru-RU.resx"), Does.Contain("lang_main_recent_projects"));
        });
    }

    [Test]
    public void Settings_ContainRecentProjects()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReadSource(@"AiRpaStudio\Properties\Settings.settings"), Does.Contain("RecentProjects"));
            Assert.That(ReadSource(@"AiRpaStudio\App.config"), Does.Contain("RecentProjects"));
            Assert.That(ReadSource(@"AiRpaStudio\Properties\Settings.Designer.cs"), Does.Contain("public string RecentProjects"));
        });
    }

    private static string MethodBody(string source, string methodSignature)
    {
        Match match = Regex.Match(source, Regex.Escape(methodSignature));
        Assert.That(match.Success, Is.True, "не найден метод " + methodSignature);
        return source.Substring(match.Index, Math.Min(1600, source.Length - match.Index));
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
