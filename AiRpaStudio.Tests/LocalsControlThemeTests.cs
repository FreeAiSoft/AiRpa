using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Smoke-стражи контрола панели «Локальные переменные» (Locals): цвета только из палитры студии,
/// шрифт только Roboto из StudioFonts, разметка содержит дерево переменных и область деталей.
/// </summary>
public class LocalsControlThemeTests
{
    private static readonly string ControlXamlPath = Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "Controls", "LocalsControl.xaml");
    private static readonly string ControlCodePath = Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "Controls", "LocalsControl.xaml.cs");

    private static readonly string[] NamedColorPatterns =
    {
        "White", "Black", "Gray", "LightGray", "DarkGray", "Orange", "OrangeRed",
        "Green", "Blue", "Red", "AliceBlue", "LightBlue", "Lime", "Goldenrod"
    };

    private static IEnumerable<string> AllowedResourceKeys()
    {
        return StudioBrushes.AliasKeys
            .Concat(StudioBrushes.SemanticKeys)
            .Concat(new[] { StudioFonts.FontFamilyKey });
    }

    [Test]
    public void Xaml_HasNoHardcodedColors()
    {
        string text = Read(ControlXamlPath);
        var offenders = new List<string>();

        foreach (Match match in Regex.Matches(text, "#[0-9A-Fa-f]{6,8}", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            offenders.Add(match.Value);
        }

        Assert.That(offenders, Is.Empty,
            "панель Locals обязана брать цвета из палитры студии: " + string.Join("; ", offenders));
    }

    [Test]
    public void Xaml_HasNoNamedColors()
    {
        string text = Read(ControlXamlPath);
        string pattern = "(Foreground|Background|Fill|Stroke|BorderBrush|Color)=\"("
                         + string.Join("|", NamedColorPatterns) + ")\"";
        var offenders = new List<string>();

        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            offenders.Add(match.Value);
        }

        Assert.That(offenders, Is.Empty,
            "named-цвета не следуют теме и дают светлые артефакты в тёмной теме: " + string.Join("; ", offenders));
    }

    [Test]
    public void Xaml_UsesOnlyStudioResources()
    {
        List<string> allowed = AllowedResourceKeys().ToList();
        string text = Read(ControlXamlPath);
        var offenders = new List<string>();

        foreach (Match match in Regex.Matches(text, "\\{DynamicResource\\s+([A-Za-z0-9_.]+)\\s*\\}", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            string key = match.Groups[1].Value;
            if (!allowed.Contains(key))
            {
                offenders.Add(key);
            }
        }

        Assert.That(text, Does.Not.Contain("MaterialDesign.Brush."), "палитра MaterialDesign не источник цветов студии");
        Assert.That(offenders, Is.Empty, "неизвестные DynamicResource-ключи: " + string.Join("; ", offenders));
    }

    [Test]
    public void Xaml_UsesStudioFontOnly()
    {
        string text = Read(ControlXamlPath);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain($"{{DynamicResource {StudioFonts.FontFamilyKey}}}"),
                "шрифт панели Locals — Roboto из StudioFonts");
            Assert.That(text, Does.Not.Contain("Consolas"), "шрифт панели Locals — Roboto из StudioFonts");
            Assert.That(text, Does.Not.Contain("Segoe UI"), "шрифт панели Locals — Roboto из StudioFonts");
        });
    }

    [Test]
    public void Xaml_HasLocalsLayout()
    {
        string text = Read(ControlXamlPath);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("TreeView"), "нет дерева переменных");
            Assert.That(text, Does.Contain("ItemsSource=\"{Binding Roots}\""), "дерево не привязано к Roots");
            Assert.That(text, Does.Contain("DataGrid"), "нет таблицы для DataTable/DataView");
            Assert.That(text, Does.Contain("Content=\"{Binding SelectedNode}\""), "область деталей не привязана к SelectedNode");
            Assert.That(text, Does.Contain("ContentTemplateSelector"), "детали не выбирают шаблон по Kind");
            Assert.That(text, Does.Contain("ItemsSource=\"{Binding Children}\""), "узлы не раскрывают Children");
            Assert.That(text, Does.Contain("Grid.IsSharedSizeScope=\"True\""), "колонки Name/Value/Type должны выравниваться");
        });
    }

    [Test]
    public void CodeBehind_PushesSelectionToModel()
    {
        string code = Read(ControlCodePath);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("SelectedItemChanged"), "дерево не отслеживает выбор");
            Assert.That(code, Does.Contain("SelectedNode"), "выбор не передаётся в модель");
        });
    }

    private static string Read(string path)
    {
        Assert.That(File.Exists(path), Is.True, $"не найден {path}");
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
