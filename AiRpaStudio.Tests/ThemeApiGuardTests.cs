using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи API темы: тема — это перечисление <see cref="Agent.Core.Models.StudioTheme"/>, и никакая
/// подсистема не возвращает <c>bool dark</c> и не «угадывает» тему по словарю ресурсов.
/// <para>
/// Зачем отдельный страж: рассинхрон темы (студия в одной палитре, чат/дизайнер в другой) появлялся
/// именно там, где рядом с палитрой жило второе представление темы. Точечные тесты такое не ловят —
/// они проверяют поведение, а не форму API. Здесь форма зафиксирована текстом исходников.
/// </para>
/// </summary>
public class ThemeApiGuardTests
{
    [Test]
    public void ThemeEnum_LivesInCore_AndIsSharedByStudioAndChat()
    {
        string root = FindRepositoryRoot();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(root, "Agent.Core", "Models", "StudioTheme.cs")), Is.True,
                "перечисление темы обязано быть одно на всё решение");
            Assert.That(Read(root, @"AiRpaStudio\AiRpaStudio.csproj"), Does.Contain(@"..\Agent.Core\Agent.Core.csproj"),
                "студия должна ссылаться на сборку с перечислением напрямую");
            Assert.That(Read(root, @"RPA.Chat\RPA.Chat.csproj"), Does.Contain(@"..\Agent.Core\Agent.Core.csproj"),
                "чат использует то же перечисление, а не своё");
        });
    }

    [Test]
    public void StudioCode_HasNoBooleanThemeApi()
    {
        string root = FindRepositoryRoot();
        var offenders = new List<string>();

        string[] forbidden =
        {
            @"ApplyTheme\(bool",
            @"ApplyMaterialDesignTheme\(bool",
            @"_currentDarkTheme",
            @"StudioPalette\.Detect\(",
            @"StudioPalette\.For\(\s*(true|false)\s*\)",
            @"StudioPalette\.For\(\s*dark\s*\)"
        };

        foreach (string file in SourceFiles(Path.Combine(root, "AiRpaStudio")))
        {
            string text = File.ReadAllText(file);

            foreach (string pattern in forbidden)
            {
                if (Regex.IsMatch(text, pattern))
                {
                    offenders.Add(Path.GetFileName(file) + ": " + pattern);
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "тема должна идти только перечислением StudioTheme (и палитрой из StudioPalette.Current): "
            + string.Join("; ", offenders));
    }

    [Test]
    public void AppSettings_SetsCurrentThemeBeforeRebuildingBrushes()
    {
        string code = Read(FindRepositoryRoot(), @"AiRpaStudio\Models\AppSettings.cs");

        int method = code.IndexOf("internal void ApplyTheme(StudioTheme theme)", StringComparison.Ordinal);
        Assert.That(method, Is.GreaterThanOrEqualTo(0), "AppSettings.ApplyTheme должен принимать StudioTheme");

        int applied = code.IndexOf("StudioPalette.ApplyCurrent(theme)", method, StringComparison.Ordinal);
        int material = code.IndexOf("ApplyMaterialDesignTheme(theme)", method, StringComparison.Ordinal);
        int brushes = code.IndexOf("StudioBrushes.ApplyToApplication()", method, StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.GreaterThan(method), "тема обязана запоминаться в ApplyTheme");
            Assert.That(applied, Is.LessThan(material),
                "StudioPalette.Current ставится ДО пересборки кистей: иначе они соберутся в палитре предыдущей темы");
            Assert.That(applied, Is.LessThan(brushes));
        });
    }

    [Test]
    public void ChatApi_SpeaksStudioTheme()
    {
        string root = FindRepositoryRoot();

        string viewModel = Read(root, @"RPA.Chat\ViewModels\ChatViewModel.cs");
        string panel = Read(root, @"RPA.Chat\Views\ChatPanel.xaml.cs");
        string settings = Read(root, @"RPA.Chat\Services\SettingsStore.cs");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel, Does.Contain("public StudioTheme Theme"),
                "ChatViewModel.Theme — источник истины, bool остаётся только проекцией тумблера");
            Assert.That(viewModel, Does.Contain("Action<StudioTheme> ThemeApplying"),
                "хост студии получает значение перечисления");
            Assert.That(panel, Does.Contain("ApplyTheme(StudioTheme theme)"));
            Assert.That(settings, Does.Contain("public string Theme"),
                "в JSON лежит имя перечисления, а не bool");
            Assert.That(settings, Does.Not.Contain("public bool UseDarkTheme"),
                "старое bool-поле осталось только как легаси для миграции");
        });
    }

    [Test]
    public void MainView_PassesEnumThemeToStudio()
    {
        string code = Read(FindRepositoryRoot(), @"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Services.AppSettings.ApplyTheme(chatPanel.ViewModel.Theme)"));
            Assert.That(code, Does.Not.Contain("ApplyTheme(chatPanel.ViewModel.IsDarkTheme)"),
                "студия не должна получать проекцию тумблера вместо темы");
        });
    }

    private static IEnumerable<string> SourceFiles(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(@"\obj\") || file.Contains(@"\bin\"))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string Read(string root, string relative)
    {
        string path = Path.Combine(root, relative);
        Assert.That(File.Exists(path), Is.True, $"не найден {relative}");
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
