using System;
using System.IO;
using System.Text.RegularExpressions;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 12 плана (docs\Vs2026_Theme_Plan.md, п.4): дерево проекта.
/// <para>
/// Дефект: при клике по активности в дереве элемент становился выбранным, и название файла
/// рисовалось системной кистью — чёрный текст на тёмном фоне. Тесты-стражи фиксируют явные кисти
/// палитры на тексте названия, триггеры состояния у контейнера и отсутствие Consolas.
/// Кисти заданы через <c>{DynamicResource}</c>, поэтому переключение темы перекрашивает дерево само.
/// </para>
/// </summary>
public class ProjectManagerThemeTests
{
    [Test]
    public void Tree_UsesStudioBrushesAndFont()
    {
        string xaml = ReadControlXaml();

        Match tree = Regex.Match(xaml,
            "<TreeView x:Name=\"projectTree\"[\\s\\S]{0,600}?>",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.That(tree.Success, Is.True, "не найдено дерево projectTree");

        Assert.Multiple(() =>
        {
            Assert.That(tree.Value, Does.Contain($"Foreground=\"{{DynamicResource {StudioBrushes.ForegroundKey}}}\""));
            Assert.That(tree.Value, Does.Contain($"Background=\"{{DynamicResource {StudioBrushes.SurfaceKey}}}\""));
            Assert.That(tree.Value, Does.Contain($"BorderBrush=\"{{DynamicResource {StudioBrushes.SeparatorKey}}}\""));
            Assert.That(tree.Value, Does.Contain($"FontFamily=\"{{DynamicResource {StudioFonts.FontFamilyKey}}}\""),
                "шрифт дерева — Roboto из StudioFonts");
        });
    }

    [Test]
    public void NameTextBlock_HasExplicitForeground()
    {
        string xaml = ReadControlXaml();

        Match textBlock = Regex.Match(xaml,
            "<TextBlock Text=\"\\{Binding Name\\}\"[\\s\\S]{0,900}?/>",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.That(textBlock.Success, Is.True, "не найден TextBlock с названием файла");
        Assert.That(textBlock.Value, Does.Contain($"Foreground=\"{{DynamicResource {StudioBrushes.ForegroundKey}}}\""),
            "название файла должно иметь явную кисть палитры, иначе выделение красит его в чёрный");
        Assert.That(textBlock.Value, Does.Contain($"FontFamily=\"{{DynamicResource {StudioFonts.FontFamilyKey}}}\""),
            "шрифт названия — Roboto");
    }

    [Test]
    public void ItemContainerStyle_HasStateTriggers()
    {
        string xaml = ReadControlXaml();

        Match style = Regex.Match(xaml,
            "<Style TargetType=\"\\{x:Type TreeViewItem\\}\">[\\s\\S]*?</Style>",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));

        Assert.That(style.Success, Is.True, "не найден стиль элемента дерева");

        Assert.Multiple(() =>
        {
            Assert.That(style.Value, Does.Contain("Property=\"IsSelected\""), "у выбранного элемента нет триггера");
            Assert.That(style.Value, Does.Contain("Property=\"IsMouseOver\""), "у hover нет триггера");
            Assert.That(style.Value, Does.Contain($"{{DynamicResource {StudioBrushes.SelectedKey}}}"),
                "фон выбранного элемента — Studio.Selected");
            Assert.That(style.Value, Does.Contain($"{{DynamicResource {StudioBrushes.HoverKey}}}"),
                "фон под курсором — Studio.Hover");
            Assert.That(style.Value, Does.Contain($"{{DynamicResource {StudioBrushes.ForegroundKey}}}"),
                "текст элемента — Studio.Foreground");
            Assert.That(style.Value, Does.Contain("IsExpanded"), "привязка раскрытия узла должна сохраниться");
            Assert.That(style.Value, Does.Contain("MouseDoubleClick"), "обработчик двойного клика должен сохраниться");
        });
    }

    [Test]
    public void EditAndSearchTextBoxes_AreThemed()
    {
        string xaml = ReadControlXaml();

        MatchCollection textBoxes = Regex.Matches(xaml, "<TextBox[\\s\\S]{0,900}?(/>|>)", RegexOptions.None, TimeSpan.FromSeconds(2));
        Assert.That(textBoxes.Count, Is.GreaterThanOrEqualTo(2), "ожидались поле поиска и поле переименования");

        foreach (Match textBox in textBoxes)
        {
            Assert.That(textBox.Value, Does.Contain($"Foreground=\"{{DynamicResource {StudioBrushes.ForegroundKey}}}\""),
                "текст поля — палитра студии");
            Assert.That(textBox.Value, Does.Contain($"Background=\"{{DynamicResource {StudioBrushes.SurfaceKey}}}\""),
                "фон поля — палитра студии");
        }
    }

    [Test]
    public void TreeXaml_HasNoConsolas()
    {
        Assert.That(ReadControlXaml(), Does.Not.Contain("Consolas"), "шрифт дерева — Roboto из StudioFonts");
    }

    [Test]
    public void ToggleButtonsInHeader_UseThemeBrushes()
    {
        // Кнопки «развернуть/свернуть/открыть папку» — часть шапки панели: рамка и фон из палитры.
        string xaml = ReadControlXaml();

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain($"{{DynamicResource {StudioBrushes.ToolbarBorderKey}}}"));
            Assert.That(xaml, Does.Contain($"{{DynamicResource {StudioBrushes.ToolbarHoverBackgroundKey}}}"));
        });
    }

    private static string ReadControlXaml()
    {
        string path = Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "Controls", "ProjectManagerControl.xaml");
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
