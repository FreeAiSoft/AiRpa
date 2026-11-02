using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи по контекстному меню основного файла проекта Visual Studio (.csproj):
/// меню на месте, пункты привязаны к обработчикам, ничего не дублируется,
/// и путь к проекту больше не передаётся файлом .csproj в качестве папки.
/// </summary>
[TestFixture]
public class VsProjectContextMenuTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Test]
    public void ProjectTree_HasContextMenuForVsProjectRoot()
    {
        XElement menu = FindContextMenu("VsProjectRootContext");

        string[] headers = menu
            .Elements()
            .Where(e => e.Name.LocalName == "MenuItem")
            .Select(e => (string)e.Attribute("Header"))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(headers, Does.Contain("Добавить активность"));
            Assert.That(headers, Does.Contain("Добавить папку проекта"));
            Assert.That(headers, Does.Contain("Переименовать проект"));
            Assert.That(headers, Does.Contain("Добавить зависимость"));
        });
    }

    [Test]
    public void ProjectTree_VsProjectNode_UsesRootContextMenu()
    {
        XElement menu = FindContextMenu("VsProjectRootContext");
        XDocument document = LoadProjectTreeXaml();

        var triggers = document
            .Descendants()
            .Where(e => e.Name.LocalName == "DataTrigger")
            .Select(trigger => new
            {
                Value = (string)trigger.Attribute("Value"),
                ContextMenu = trigger
                    .Elements()
                    .Where(e => e.Name.LocalName == "Setter" && (string)e.Attribute("Property") == "ContextMenu")
                    .Select(e => (string)e.Attribute("Value"))
                    .FirstOrDefault()
            })
            .ToList();

        var vsProjectTrigger = triggers.Single(t => t.Value != null && t.Value.EndsWith("VsProject}", StringComparison.Ordinal));
        var solutionTrigger = triggers.Single(t => t.Value != null && t.Value.EndsWith("Solution}", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(vsProjectTrigger.ContextMenu, Does.Contain("VsProjectRootContext"),
                "у файла проекта .csproj должно быть меню с действиями проекта");
            Assert.That(solutionTrigger.ContextMenu, Does.Contain("VsProjectContext"));
            Assert.That(menu, Is.Not.Null);
        });
    }

    [Test]
    public void ProjectTree_MenuItemNames_AreUnique()
    {
        XDocument document = LoadProjectTreeXaml();

        List<string> duplicates = document
            .Descendants()
            .Select(e => (string)e.Attribute(Xaml + "Name"))
            .Where(name => !string.IsNullOrEmpty(name))
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.That(duplicates, Is.Empty, "в XAML повторяются имена элементов: " + string.Join(", ", duplicates));
    }

    [Test]
    public void ProjectTree_HandlersForVsProjectMenu_Exist()
    {
        string codeBehind = ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(codeBehind, Does.Contain("AddActivityToVsProjectMenuItem_Click"));
            Assert.That(codeBehind, Does.Contain("AddDependencyToVsProjectMenuItem_Click"));
            Assert.That(codeBehind, Does.Contain("RenameVsProject"));
            Assert.That(codeBehind, Does.Contain("CmdOpenNuGetPanel"));
        });
    }

    [Test]
    public void ProjectTree_FolderOperations_UseNodeFolderInsteadOfRawPath()
    {
        string codeBehind = ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(codeBehind, Does.Not.Contain("Path.Combine(parentNode.RuntimeFullPath, uniqueFolderName)"),
                "новая папка должна создаваться в папке проекта, а не рядом с файлом .csproj");
            Assert.That(codeBehind, Does.Contain("ProjectPathResolver.GetNodeFolder(parent)"));
        });
    }

    [Test]
    public void MainView_CanShowNuGetPanelForProject()
    {
        string code = ReadSource(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.That(code, Does.Contain("public void ShowNuGetPanelFor(string projectPath)"));
    }

    [Test]
    public void Main_ResolvesProjectFolder_ForEveryActivityCreationEntry()
    {
        string code = ReadSource(@"AiRpaStudio\Models\Main.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("public void CmdOpenNuGetPanel(string projectPath)"));
            Assert.That(code, Does.Contain("ProjectPathResolver.GetProjectFolder"));
            Assert.That(code, Does.Contain("RegisterFileInVsProject"));
            Assert.That(code, Does.Not.Contain("new ActivityCreator()"), "путь из настроек нужно нормализовать до папки");
        });
    }

    [Test]
    public void ProjectFile_IncludesNewSources()
    {
        string csproj = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.Multiple(() =>
        {
            Assert.That(csproj, Does.Contain(@"Helpers\ProjectPathResolver.cs"));
            Assert.That(csproj, Does.Contain(@"VsProject\VsProjectWriter.cs"));
        });
    }

    private static XElement FindContextMenu(string key)
    {
        XDocument document = LoadProjectTreeXaml();
        XElement menu = document
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ContextMenu" && (string)e.Attribute(Xaml + "Key") == key);

        Assert.That(menu, Is.Not.Null, $"в ProjectManagerControl.xaml нет меню {key}");
        return menu;
    }

    private static XDocument LoadProjectTreeXaml()
    {
        XDocument document = XDocument.Parse(ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml"));
        return document;
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
