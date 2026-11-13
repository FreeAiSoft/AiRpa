using System.IO;

namespace AiRpaStudio.Tests;

/// <summary>
/// Ф1 шаг 4 — пункт меню «Настройки → Стартовый скрипт…» и тематизированный диалог
/// (<c>Views\StartScriptView.xaml</c>), сохраняющий <c>DefaultRunScript</c> и вызывающий
/// <c>Save()</c>. Диалог обязан следовать правилам темы студии (StudioTitleBar, палитра).
/// </summary>
public class StartScriptDialogTests
{
    private const string DialogXaml = @"AiRpaStudio\Views\StartScriptView.xaml";
    private const string DialogCode = @"AiRpaStudio\Views\StartScriptView.xaml.cs";
    private const string MainViewXaml = @"AiRpaStudio\Views\MainView.xaml";
    private const string MainViewCode = @"AiRpaStudio\Views\MainView.xaml.cs";

    [Test]
    public void SettingsMenu_HasStartScriptItem()
    {
        string xaml = ReadSource(MainViewXaml);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("lang_main_start_script"),
                "пункт «Стартовый скрипт…» берёт подпись из ресурсов, как остальные пункты меню «Настройки»");
            Assert.That(xaml, Does.Contain("StartScriptMenuItem_Click"),
                "пункт меню привязан к обработчику клика");
        });
    }

    [Test]
    public void MenuHandler_OpensStartScriptDialog()
    {
        string code = ReadSource(MainViewCode);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("StartScriptView"),
                "обработчик открывает тематизированный диалог StartScriptView");
            Assert.That(code, Does.Contain("ShowDialog()"),
                "диалог показывается модально");
        });
    }

    [Test]
    public void Dialog_SavesDefaultRunScriptAndCallsSave()
    {
        string code = ReadSource(DialogCode);

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("DefaultRunScript"),
                "диалог читает/пишет настройку DefaultRunScript");
            Assert.That(code, Does.Contain("Settings.Default.Save()"),
                "изменение настройки сохраняется через Properties.Settings.Save()");
        });
    }

    [Test]
    public void Dialog_UsesPaletteOnly()
    {
        string xaml = ReadSource(DialogXaml);

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("<controls:StudioTitleBar"),
                "заголовок рисует студия (StudioTitleBar), а не системный caption");
            Assert.That(xaml, Does.Not.Match("#[0-9A-Fa-f]{6,8}"),
                "зашитые цвета запрещены: только ключи палитры Studio.*");
        });
    }

    [Test]
    public void Localization_DeclaresStartScriptKeys()
    {
        string en = ReadSource(@"AiRpaStudio\Resources\Main.resx");
        string ru = ReadSource(@"AiRpaStudio\Resources\Main.ru-RU.resx");

        string[] keys =
        {
            "lang_main_start_script",
            "lang_start_script_title",
            "lang_start_script_label",
            "lang_start_script_save",
            "lang_cancel"
        };

        Assert.Multiple(() =>
        {
            foreach (string key in keys)
            {
                Assert.That(en, Does.Contain($"name=\"{key}\""), $"Main.resx: нет ключа {key}");
                Assert.That(ru, Does.Contain($"name=\"{key}\""), $"Main.ru-RU.resx: нет ключа {key}");
            }
        });
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
