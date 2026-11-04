using System;
using System.IO;
using RPA.Chat.Services;
using RPA.Chat.ViewModels;
using Agent.Core.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Персист темы: в файле настроек лежит имя перечисления <see cref="StudioTheme"/> (<c>"Dark"</c>/<c>"Light"</c>),
/// а старый формат <c>{"UseDarkTheme": bool}</c> переносится автоматически.
/// <para>
/// Причина: пока в JSON жил <c>bool</c>, у темы было два представления (bool в настройках и палитра в коде).
/// Переход на имя перечисления убирает второй источник истины, но обязан сохранить уже созданные
/// файлы пользователей — иначе после обновления тема молча сбросится на тёмную.
/// </para>
/// </summary>
public class ChatThemeSettingsTests
{
    private string _directory;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "AiRpaStudio.ThemeSettings." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        ChatPaths.OverrideDataDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        ChatPaths.OverrideDataDirectory(null);

        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Test]
    public void Defaults_AreDarkTheme()
    {
        SettingsData settings = SettingsStore.Load();

        Assert.That(settings.Theme, Is.EqualTo(StudioThemes.DarkName),
            "тёмная тема — дефолт студии (App.xaml BaseTheme=Dark)");
        Assert.That(settings.LegacyUseDarkTheme, Is.Null);
    }

    [Test]
    public void LegacyFalse_MigratesToLightTheme()
    {
        WriteSettings("{ \"UseDarkTheme\": false }");

        SettingsData settings = SettingsStore.Load();

        Assert.Multiple(() =>
        {
            Assert.That(settings.Theme, Is.EqualTo(StudioThemes.LightName), "старый bool переносится в перечисление");
            Assert.That(settings.LegacyUseDarkTheme, Is.Null, "легаси-поле обнуляется после переноса");
        });
    }

    [Test]
    public void LegacyTrue_MigratesToDarkTheme()
    {
        WriteSettings("{ \"UseDarkTheme\": true }");

        Assert.That(SettingsStore.Load().Theme, Is.EqualTo(StudioThemes.DarkName));
    }

    [Test]
    public void ExistingThemeWinsOverLegacyField()
    {
        WriteSettings("{ \"Theme\": \"Light\", \"UseDarkTheme\": true }");

        Assert.That(SettingsStore.Load().Theme, Is.EqualTo(StudioThemes.LightName),
            "если имя темы уже есть, легаси-поле игнорируется");
    }

    [Test]
    public void UnknownThemeName_FallsBackToDark()
    {
        WriteSettings("{ \"Theme\": \"nonsense\" }");

        Assert.That(SettingsStore.Load().Theme, Is.EqualTo(StudioThemes.DarkName));
    }

    [Test]
    public void Save_WritesThemeNameAndDropsLegacyField()
    {
        WriteSettings("{ \"UseDarkTheme\": false }");
        SettingsData settings = SettingsStore.Load();

        SettingsStore.Save(settings);

        string json = File.ReadAllText(ChatPaths.SettingsFile);
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"Theme\": \"Light\""), "в файл пишется имя перечисления");
            Assert.That(json, Does.Not.Contain("UseDarkTheme"), "bool-поле больше не сохраняется");
        });
    }

    [Test]
    public void ViewModel_ThemeIsSourceOfTruth()
    {
        ChatViewModel viewModel = new();
        var applied = new System.Collections.Generic.List<StudioTheme>();
        viewModel.ThemeApplying = theme => applied.Add(theme);

        Assert.That(viewModel.Theme, Is.EqualTo(StudioTheme.Dark));

        viewModel.Theme = StudioTheme.Light;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Theme, Is.EqualTo(StudioTheme.Light));
            Assert.That(viewModel.IsDarkTheme, Is.False, "тумблер — проекция перечисления");
            Assert.That(viewModel.Settings.Theme, Is.EqualTo(StudioThemes.LightName));
            Assert.That(applied, Is.EqualTo(new[] { StudioTheme.Light }), "хост получает значение перечисления");
        });

        // Обратный путь: переключатель меняет именно перечисление.
        viewModel.IsDarkTheme = true;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Theme, Is.EqualTo(StudioTheme.Dark));
            Assert.That(viewModel.Settings.Theme, Is.EqualTo(StudioThemes.DarkName));
            Assert.That(applied, Is.EqualTo(new[] { StudioTheme.Light, StudioTheme.Dark }));
        });
    }

    private static void WriteSettings(string json)
    {
        ChatPaths.EnsureDirectories();
        File.WriteAllText(ChatPaths.SettingsFile, json);
    }
}
