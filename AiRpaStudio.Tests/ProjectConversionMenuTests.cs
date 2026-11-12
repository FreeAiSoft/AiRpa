using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи пунктов контекстного меню конвертации формата проекта:
/// «Преобразовать в VS-проект» (native) и «Преобразовать во встроенный проект» (VS) вызывают
/// <see cref="RPA.ProjectModel.ProjectConversionService"/> и обновляют дерево/сохранённый путь.
/// </summary>
public class ProjectConversionMenuTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Test]
    public void ProjectTree_HasConversionMenuItems()
    {
        XElement nativeMenu = FindContextMenu("ProjectContext");
        XElement vsMenu = FindContextMenu("VsProjectRootContext");

        Assert.Multiple(() =>
        {
            Assert.That(Headers(nativeMenu), Does.Contain("Преобразовать в VS-проект"),
                "у нативного проекта должен быть пункт конвертации в VS");
            Assert.That(Headers(vsMenu), Does.Contain("Преобразовать во встроенный проект"),
                "у VS-проекта должен быть пункт конвертации во встроенный формат");
        });
    }

    [Test]
    public void ProjectTree_ConversionHandlers_ExistAndUseConversionService()
    {
        string code = ReadSource(@"AiRpaStudio\Controls\ProjectManagerControl.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("ConvertToVsProjectMenuItem_Click"));
            Assert.That(code, Does.Contain("ConvertToNativeProjectMenuItem_Click"));
            Assert.That(code, Does.Contain("ProjectConversionService.ConvertToVsProject(project)"));
            Assert.That(code, Does.Contain("ProjectConversionService.ConvertToNative(project)"));
            Assert.That(code, Does.Contain("ProjectConversionService"),
                "конвертация должна идти через единый ProjectConversionService");
        });
    }

    [Test]
    public void ProjectTree_ConversionMenuItemNames_AreUnique()
    {
        XDocument document = LoadProjectTreeXaml();

        var names = new[]
        {
            "ConvertToVsProjectMenuItem",
            "ConvertToNativeProjectMenuItem"
        };

        foreach (string name in names)
        {
            int count = document
                .Descendants()
                .Count(e => (string)e.Attribute(Xaml + "Name") == name);

            Assert.That(count, Is.EqualTo(1), $"имя пункта меню {name} обязано быть уникальным");
        }
    }

    private static string[] Headers(XElement menu)
    {
        return menu
            .Elements()
            .Where(e => e.Name.LocalName == "MenuItem")
            .Select(e => (string)e.Attribute("Header"))
            .ToArray();
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
