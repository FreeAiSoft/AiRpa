using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RPA.Helpers;
using RPA.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи по пункту «Открыть в редакторе кода» в дереве проекта:
/// <list type="bullet">
/// <item>пункт есть у файла проекта Visual Studio (.csproj) и у решения (.sln);</item>
/// <item>у нативного <c>project.json</c> он тоже виден (раньше меню этого файла оставляло
/// только «Открыть содержащую папку»);</item>
/// <item>правило видимости живёт в одном месте (<see cref="ProjectTreeMenuRules"/>), а не в XAML
/// и не в code-behind;</item>
/// <item>двойной клик по .sln/.csproj по-прежнему открывает Visual Studio — в редактор только пункт меню.</item>
/// </list>
/// </summary>
[TestFixture]
public class ProjectTreeCodeEditorMenuTests
{
    private const string CodeEditorHeader = "Открыть в редакторе кода";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Test]
    public void VsProjectRootMenu_HasOpenInCodeEditorItem()
    {
        XElement item = FindMenuItem("VsProjectRootContext", "VsRootOpenInCodeEditorMenuItem");

        Assert.Multiple(() =>
        {
            Assert.That((string)item.Attribute("Header"), Is.EqualTo(CodeEditorHeader));
            Assert.That((string)item.Attribute("Click"), Is.EqualTo("OpenInCodeEditorMenuItem_Click"),
                "пункт должен открывать файл встроенным редактором, а не в Visual Studio");
        });
    }

    [Test]
    public void SolutionMenu_HasOpenInCodeEditorItem()
    {
        XElement item = FindMenuItem("VsProjectContext", "SolutionOpenInCodeEditorMenuItem");

        Assert.Multiple(() =>
        {
            Assert.That((string)item.Attribute("Header"), Is.EqualTo(CodeEditorHeader));
            Assert.That((string)item.Attribute("Click"), Is.EqualTo("OpenInCodeEditorMenuItem_Click"));
        });
    }

    [Test]
    public void MenuRules_ProjectJson_ShowsCodeEditorItem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenInCodeEditorMenuItem, "project.json"),
                Is.True, "project.json должен открываться в редакторе кода из дерева проекта");
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenFolderContainingFileMenuItem, "project.json"),
                Is.True, "«Открыть содержащую папку» у project.json остаётся");
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenFileMenuItem, "project.json"),
                Is.False, "остальные пункты файлового меню у project.json по-прежнему скрыты");
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.DeleteFileMenuItem, "project.json"),
                Is.False);
        });
    }

    [Test]
    public void MenuRules_OtherFiles_KeepCurrentBehaviour()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenFileMenuItem, "Activity1.xaml"),
                Is.True, "у обычного файла видны все пункты меню");
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenInCodeEditorMenuItem, "Activity1.xaml"),
                Is.True);
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenInVsMenuItem, "Activity1.xaml"),
                Is.False, "«Открыть в VS» остаётся только у .sln/.csproj");
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.BuildWithMsBuildMenuItem, "Activity1.xaml"),
                Is.False);
        });
    }

    [Test]
    public void MenuRules_VsProjectFiles_ShowVsItems()
    {
        foreach (string fileName in new[] { "ActivityLibrary5.csproj", "AiRpa.sln" })
        {
            Assert.Multiple(() =>
            {
                Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenInVsMenuItem, fileName), Is.True);
                Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.BuildWithMsBuildMenuItem, fileName), Is.True);
                Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenInCodeEditorMenuItem, fileName), Is.True,
                    "файл проекта VS тоже открывается встроенным редактором");
            });
        }
    }

    [Test]
    public void MenuRules_MatchesByFileName_NotFullPath()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenInCodeEditorMenuItem, @"C:\work\PROJECT.JSON"),
                Is.True, "сравнение по имени файла и без учёта регистра");
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenFileMenuItem, @"C:\work\PROJECT.JSON"),
                Is.False);
            Assert.That(ProjectTreeMenuRules.IsVisibleForFile(ProjectTreeMenuRules.OpenInVsMenuItem, @"C:\work\A.SLN"),
                Is.True);
        });
    }

    [Test]
    public void MenuRules_UnknownMenuItem_StaysVisible()
    {
        Assert.That(ProjectTreeMenuRules.IsVisibleForFile("AddActivity", "project.json"),
            Is.False, "у project.json посторонних пунктов быть не должно");

        Assert.That(ProjectTreeMenuRules.IsVisibleForFile("AddActivity", "MyProject"),
            Is.True, "правило не должно прятать пункты, о которых ничего не знает");
    }

    [Test]
    public void ContextMenu_Opened_DelegatesToMenuRules()
    {
        string codeBehind = ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(codeBehind, Does.Contain("ProjectTreeMenuRules.IsVisibleForFile"),
                "видимость пунктов решает одно правило, а не список имён в code-behind");
            Assert.That(codeBehind, Does.Not.Contain("menuItem.Name == \"OpenFolderContainingFileMenuItem\""),
                "прямое сравнение имени пункта в code-behind обходило бы правило");
        });
    }

    [Test]
    public void CmdTryLoadFile_StillRoutesVsProjectToVisualStudio()
    {
        string code = ReadSource(@"AiRpaStudio\Models\Main.cs");

        int tryLoadStart = code.IndexOf("public void CmdTryLoadFile(string filePath)", StringComparison.Ordinal);
        Assert.That(tryLoadStart, Is.GreaterThan(0), "в Main.cs нет метода CmdTryLoadFile");
        int vsBranch = code.IndexOf("CmdVsOpenInVisualStudio(filePath)", tryLoadStart, StringComparison.Ordinal);
        int codeEditorBranch = code.IndexOf("CmdOpenInCodeEditor(filePath)", tryLoadStart, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(vsBranch, Is.GreaterThan(0), "двойной клик по .sln/.csproj должен открывать Visual Studio");
            Assert.That(codeEditorBranch, Is.GreaterThan(0));
            Assert.That(vsBranch, Is.LessThan(codeEditorBranch),
                "ветка .sln/.csproj должна проверяться раньше ветки редактора кода");
        });
    }

    [Test]
    public void ProjectFile_IncludesMenuRulesSource()
    {
        string csproj = ReadSource(@"AiRpaStudio\AiRpaStudio.csproj");

        Assert.That(csproj, Does.Contain(@"Helpers\ProjectTreeMenuRules.cs"));
    }

    [Test]
    public void CodeDocument_LoadsVsProjectFilesWithoutHighlighting()
    {
        string directory = Path.Combine(Path.GetTempPath(), "rpa-code-doc-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        try
        {
            foreach (string fileName in new[] { "ActivityLibrary5.csproj", "AiRpa.sln" })
            {
                string path = Path.Combine(directory, fileName);
                File.WriteAllText(path, "<Project />");

                CodeDocument document = CodeDocument.Load(path);

                Assert.Multiple(() =>
                {
                    Assert.That(document.Title, Is.EqualTo(fileName));
                    Assert.That(document.Text, Does.Contain("Project"));
                    Assert.That(document.Language, Is.Null,
                        "подсветки для .csproj/.sln ещё нет — редактор открывает их как текст (отдельная задача)");
                });
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static XElement FindMenuItem(string menuKey, string itemName)
    {
        XElement menu = LoadProjectTreeXaml()
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ContextMenu" && (string)e.Attribute(Xaml + "Key") == menuKey);

        Assert.That(menu, Is.Not.Null, $"в ProjectManagerControl.xaml нет меню {menuKey}");

        XElement item = menu
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "MenuItem" && (string)e.Attribute(Xaml + "Name") == itemName);

        Assert.That(item, Is.Not.Null, $"в меню {menuKey} нет пункта {itemName}");
        return item;
    }

    private static XDocument LoadProjectTreeXaml()
    {
        return XDocument.Parse(ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml"));
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
