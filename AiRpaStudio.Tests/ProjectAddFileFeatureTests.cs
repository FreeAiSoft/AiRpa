using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи фичи «Добавить файл» в контекстное меню проекта:
/// пункт есть у нативного корня, у корня VS-проекта и у папки; обработчик копирует файл
/// в папку проекта и регистрирует его в .csproj (для проектов Visual Studio).
/// </summary>
[TestFixture]
public class ProjectAddFileFeatureTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Test]
    public void ProjectMenus_HaveAddFileItem()
    {
        XDocument xaml = LoadProjectTreeXaml();

        foreach ((string menuKey, string itemName) in new[]
                 {
                     ("ProjectContext", "AddFileMenuItem"),
                     ("VsProjectRootContext", "VsAddFileMenuItem"),
                     ("ProjectFolderContext", "AddFileToFolderMenuItem")
                 })
        {
            XElement item = FindMenuItem(xaml, menuKey, itemName);

            Assert.Multiple(() =>
            {
                Assert.That(item, Is.Not.Null, $"в меню {menuKey} нет пункта {itemName}");
                Assert.That((string)item.Attribute("Header"), Is.EqualTo("Добавить файл"),
                    $"{menuKey}/{itemName}: подпись пункта");
                Assert.That((string)item.Attribute("Click"), Is.EqualTo("AddFileMenuItem_Click"),
                    $"{menuKey}/{itemName}: пункт должен вести на общий обработчик");
            });
        }
    }

    [Test]
    public void Main_AddFileCommand_CopiesAndRegisters()
    {
        string code = ReadSource(@"AiRpaStudio\Models\Main.cs");
        int methodStart = code.IndexOf("public void CmdAddFileToProjectFolder", StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThan(0), "в Main.cs нет метода CmdAddFileToProjectFolder");

        int nextMethod = code.IndexOf("public void ", methodStart + 1, StringComparison.Ordinal);
        string method = nextMethod > 0
            ? code.Substring(methodStart, nextMethod - methodStart)
            : code.Substring(methodStart);

        Assert.Multiple(() =>
        {
            Assert.That(method, Does.Contain("IOHelper.GetFilePath"),
                "команда должна открывать выбор файла");
            Assert.That(method, Does.Contain("ProjectManager.ImportFile"),
                "копирование и регистрация должны идти через ProjectManager.ImportFile");
        });
    }

    [Test]
    public void ProjectManager_ImportFile_RegistersInVsProject()
    {
        string code = ReadSource(@"AiRpaStudio\Models\ProjectManager.cs");
        int methodStart = code.IndexOf("ImportFile", StringComparison.Ordinal);
        Assert.That(methodStart, Is.GreaterThan(0), "в ProjectManager.cs нет метода ImportFile");

        int nextMember = code.IndexOf("internal ", methodStart + 1, StringComparison.Ordinal);
        string method = nextMember > 0
            ? code.Substring(methodStart, nextMember - methodStart)
            : code.Substring(methodStart);

        Assert.That(method, Does.Contain("RegisterFileInVsProject"),
            "добавленный файл должен регистрироваться в .csproj проекта Visual Studio");
    }

    private static XDocument LoadProjectTreeXaml()
    {
        return XDocument.Parse(ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml"));
    }

    private static XElement FindMenuItem(XDocument xaml, string menuKey, string itemName)
    {
        XElement menu = xaml
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ContextMenu" && (string)e.Attribute(Xaml + "Key") == menuKey);

        Assert.That(menu, Is.Not.Null, $"в ProjectManagerControl.xaml нет меню {menuKey}");

        return menu
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "MenuItem" && (string)e.Attribute(Xaml + "Name") == itemName);
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
