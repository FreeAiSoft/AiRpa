using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи изоляции AgentToolkit от неявных стилей MaterialDesign студии: ChatHostView
/// подключает словарь-«защиту» с неявными стилями для контролов, которых нет в
/// AgentTheme.xaml тулкита (ToggleButton/CheckBox/Slider/ListBox), и эти стили используют
/// только {DynamicResource Theme.*}.
/// </summary>
public class ChatToolkitThemeIsolationTests
{
    private const string IsolationPath = @"RPA.Chat\Themes\AgentToolkitIsolation.xaml";

    [Test]
    public void ChatHostView_MergesToolkitIsolationDictionary()
    {
        string xaml = Read(@"RPA.Chat\Views\ChatHostView.xaml");

        Assert.That(xaml, Does.Contain("AgentToolkitIsolation.xaml"),
            "ChatHostView должен подключать словарь-изоляцию AgentToolkitIsolation.xaml");
    }

    [Test]
    public void IsolationDictionary_OverridesMaterialDesignControls()
    {
        string xaml = Read(IsolationPath);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("<Style TargetType=\"ToggleButton\""),
                "пустой неявный ToggleButton снимает Width=34/switch MaterialDesign");
            Assert.That(xaml, Does.Contain("<Style TargetType=\"CheckBox\""),
                "нужен неявный CheckBox (в тулките его нет — MaterialDesign протекает)");
            Assert.That(xaml, Does.Contain("<Style TargetType=\"Slider\""),
                "нужен неявный Slider (в тулките его нет — MaterialDesign протекает)");
            Assert.That(xaml, Does.Contain("<Style TargetType=\"ListBox\""),
                "нужен неявный ListBox (в тулките его нет — MaterialDesign протекает)");
        });
    }

    [Test]
    public void IsolationDictionary_UsesOnlyThemeDynamicResources()
    {
        string xaml = Read(IsolationPath);

        Assert.Multiple(() =>
        {
            Assert.That(Regex.IsMatch(xaml, "#[0-9A-Fa-f]{6}"), Is.False,
                "в словаре-изоляции не должно быть зашитых hex-цветов");

            string[] banned = { "White", "Black", "Gray", "Grey", "Red", "Green", "Blue", "Orange", "Yellow", "Purple", "Brown", "Pink" };
            foreach (string color in banned)
            {
                Assert.That(xaml.IndexOf(color, StringComparison.OrdinalIgnoreCase), Is.LessThan(0),
                    $"зашитый named-цвет {color} запрещён — только {{DynamicResource Theme.*}}");
            }
        });
    }

    private static string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Не найден корень репозитория (AiRpa.sln).");
    }
}
