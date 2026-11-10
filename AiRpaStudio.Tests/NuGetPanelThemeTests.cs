using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи оформления панели «NuGet» (docs\NuGet_Panel_Redesign_Plan.md §2.2).
/// <para>
/// Панель повторяет менеджер пакетов Visual Studio 2026, но с прямыми углами: панели студии
/// плоские (<c>PropertyWindowStyleHelper</c>). Поэтому проверяется: нет скруглений, цвета берутся
/// только ключами палитры, шрифт — только <c>Studio.FontFamily</c>, а состав разметки совпадает со
/// скриншотом-эталоном (вкладки, поиск Ctrl+L, источник, детали, README/Package Details).
/// </para>
/// </summary>
[TestFixture]
public class NuGetPanelThemeTests
{
    private static readonly string[] PanelFiles =
    {
        @"RPA.NuGet\Views\NuGetPanel.xaml",
        @"RPA.NuGet\Views\NuGetPanel.Styles.xaml"
    };

    private static readonly string[] NamedColorPatterns =
    {
        "White", "Black", "Gray", "LightGray", "DarkGray", "Orange", "OrangeRed",
        "Green", "Blue", "Red", "AliceBlue", "LightBlue", "Lime", "Goldenrod"
    };

    /// <summary>Все кисти, которые разрешено читать разметке панели: алиасы и семантика студии.</summary>
    private static IEnumerable<string> AllowedResourceKeys()
    {
        return StudioBrushes.AliasKeys
            .Concat(StudioBrushes.SemanticKeys)
            .Concat(new[] { StudioFonts.FontFamilyKey });
    }

    [Test]
    public void PanelXaml_HasNoHardcodedColors()
    {
        var offenders = new List<string>();

        foreach (string file in PanelFiles)
        {
            string text = WithoutComments(Read(file));
            foreach (Match match in Regex.Matches(text, "#[0-9A-Fa-f]{6,8}", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                offenders.Add($"{file}: {match.Value}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "панель NuGet обязана брать цвета из палитры студии (Studio.Surface/Foreground/Separator/…): "
            + string.Join("; ", offenders));
    }

    [Test]
    public void PanelXaml_HasNoNamedColors()
    {
        var offenders = new List<string>();
        string pattern = "(Foreground|Background|Fill|Stroke|BorderBrush|Color)=\"("
                         + string.Join("|", NamedColorPatterns) + ")\"";

        foreach (string file in PanelFiles)
        {
            string text = WithoutComments(Read(file));
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                offenders.Add($"{file}: {match.Value}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "named-цвета не следуют теме и дают светлые артефакты в тёмной теме: " + string.Join("; ", offenders));
    }

    [Test]
    public void PanelXaml_UsesSquareCornersOnly()
    {
        // Скруглений в панели нет вообще (как у панели «Свойства»): любой ненулевой радиус — ошибка.
        var offenders = new List<string>();

        foreach (string file in PanelFiles)
        {
            string text = WithoutComments(Read(file));
            foreach (Match match in Regex.Matches(text, "CornerRadius=\"([^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                string value = match.Groups[1].Value.Trim();
                if (value != "0")
                {
                    offenders.Add($"{file}: CornerRadius=\"{value}\"");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "дизайн панели NuGet — без скруглений, допустим только CornerRadius=\"0\": " + string.Join("; ", offenders));
    }

    [Test]
    public void PanelXaml_UsesOnlyStudioResources()
    {
        List<string> allowed = AllowedResourceKeys().ToList();
        var offenders = new List<string>();

        foreach (string file in PanelFiles)
        {
            string text = WithoutComments(Read(file));

            foreach (Match match in Regex.Matches(text, "\\{DynamicResource\\s+([A-Za-z0-9_.]+)\\s*\\}", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                string key = match.Groups[1].Value;
                if (!allowed.Contains(key))
                {
                    offenders.Add($"{file}: {key}");
                }
            }

            Assert.That(text, Does.Not.Contain("MaterialDesign.Brush."),
                $"{file}: палитра MaterialDesign не источник цветов студии");
            Assert.That(text, Does.Not.Contain("Segoe UI"), $"{file}: шрифт только из StudioFonts");
            Assert.That(text, Does.Not.Contain("Consolas"), $"{file}: шрифт только из StudioFonts");
        }

        Assert.That(offenders, Is.Empty,
            "панель ссылается на неизвестные ключи ресурсов (опечатка даёт пустую кисть): "
            + string.Join("; ", offenders));
    }

    [Test]
    public void PanelXaml_HasVisualStudioLayout()
    {
        string xaml = WithoutComments(Read(@"RPA.NuGet\Views\NuGetPanel.xaml"));

        Assert.Multiple(() =>
        {
            // Вкладки как в VS 2026.
            Assert.That(xaml, Does.Contain("Content=\"Browse\""), "нет вкладки Browse");
            Assert.That(xaml, Does.Contain("Content=\"Installed\""), "нет вкладки Installed");
            Assert.That(xaml, Does.Contain("Content=\"Updates\""), "нет вкладки Updates");

            // Тулбар: поиск с плейсхолдером, prerelease, источник пакетов.
            Assert.That(xaml, Does.Contain("Search (Ctrl+L)"), "нет плейсхолдера поиска с подсказкой Ctrl+L");
            Assert.That(xaml, Does.Contain("Include prerelease"), "нет переключателя Include prerelease");
            Assert.That(xaml, Does.Contain("Package source:"), "нет подписи выбора источника пакетов");

            // Панель деталей.
            Assert.That(xaml, Does.Contain("Package source mapping is off."), "нет плашки сопоставления источников");
            Assert.That(xaml, Does.Contain("Header=\"Options\""), "нет блока Options");
            Assert.That(xaml, Does.Contain("Content=\"README\""), "нет вкладки README");
            Assert.That(xaml, Does.Contain("Content=\"Package Details\""), "нет вкладки Package Details");
            Assert.That(xaml, Does.Contain("Project URL"), "нет строки Project URL");
            Assert.That(xaml, Does.Not.Contain("Report Abuse"), "ссылка Report Abuse на nuget.org удалена");
            Assert.That(xaml, Does.Contain("Tags"), "нет строки Tags");

            // Разделитель «список | детали» толщиной 1px (hairline, без второго фона).
            Assert.That(xaml, Does.Contain("GridSplitter"), "нет разделителя между списком и деталями");

            // Комбобоксы: минимальная ширина — в разметке, потолок 150px — в стиле (общий для обоих).
            Assert.That(xaml, Does.Contain("MinWidth=\"50\""), "у источника пакетов нет минимальной ширины");
            Assert.That(xaml, Does.Contain("MinWidth=\"80\""), "у выбора версии нет минимальной ширины");
            // Потолок ширины 150px общий и задан стилем: он применяется и к источнику, и к версии.
            Assert.That(Read(@"RPA.NuGet\Views\NuGetPanel.Styles.xaml"), Does.Contain("MaxWidth\" Value=\"150\""),
                "в NuGet.ComboBoxStyle нет потолка ширины 150px");

            // Кнопки установки/удаления прямо на плашке пакета (по наведению мыши).
            Assert.That(xaml, Does.Contain("InstallItemCommand"), "нет кнопки установки на плашке пакета");
            Assert.That(xaml, Does.Contain("UninstallItemCommand"), "нет кнопки удаления на плашке пакета");
            Assert.That(xaml, Does.Contain("AncestorType=ListBoxItem"), "кнопки плашки не привязаны к наведению мыши");

            // Кнопки плашки скрыты умолчанием стиля, а не локальным значением: локальное значение
            // перебивает DataTemplate-триггер, и кнопки не появились бы по наведению.
            Assert.That(xaml, Does.Not.Contain("Visibility=\"Collapsed\" />"),
                "у кнопок плашки не должно быть локального Visibility — только стиль и триггер");

            // Поиск без Enter: обработчика клавиш у поля нет, вместо него — кнопка повтора.
            Assert.That(xaml, Does.Not.Contain("KeyDown=\"SearchTextBox_KeyDown\""),
                "Enter больше не запускает поиск — он идёт по мере ввода");
            Assert.That(xaml, Does.Contain("Refresh search results"), "нет иконки повтора поиска");

            // Оверлей «Package sources»: заголовок, таблица, кнопки Add/Edit/Remove.
            Assert.That(xaml, Does.Contain("x:Name=\"SourcesOverlay\""), "нет оверлея источников пакетов");
            Assert.That(Regex.IsMatch(xaml, "x:Name=\"SourcesOverlay\"[\\s\\S]{0,120}?Grid\\.RowSpan=\"3\""),
                "оверлей обязан накрывать строки 0–2 (Grid.RowSpan=\"3\"), а не строку состояния — её полоса резала подпись");
            Assert.That(xaml, Does.Contain("Package sources"), "нет заголовка оверлея");
            Assert.That(xaml, Does.Contain("Text=\"Add\""), "нет кнопки Add в оверлее");
            Assert.That(xaml, Does.Contain("Text=\"Remove\""), "нет кнопки Remove в оверлее");
            Assert.That(xaml, Does.Contain("Content=\"OK\""), "нет кнопки OK в редакторе источника");

            // README отображается разметкой, а не сырым текстом.
            Assert.That(xaml, Does.Contain("FlowDocumentScrollViewer"), "README обязан рендериться как документ");
            Assert.That(xaml, Does.Contain("MarkdownToDocument"), "нет конвертера Markdown → документ");

            // Пустая правая колонка не показывается: заглушки «Select a package…» быть не должно.
            Assert.That(xaml, Does.Not.Contain("Select a package to see its details"),
                "правая колонка обязана быть пустой, пока пакет не выбран");
        });
    }

    [Test]
    public void PanelCodeBehind_HasShortcutsAndStableApi()
    {
        string code = Read(@"RPA.NuGet\Views\NuGetPanel.xaml.cs");

        Assert.Multiple(() =>
        {
            // Публичный API панели используется MainView и тестами контекстного меню проекта.
            Assert.That(code, Does.Contain("public bool SetProject(string projectPath)"));
            Assert.That(code, Does.Contain("public bool SetProjectFolder(string folder)"));

            // Ctrl+L — фокус в поиск (как в VS), Esc — очистка.
            Assert.That(code, Does.Contain("Key.L"));
            Assert.That(code, Does.Contain("ModifierKeys.Control"));
            Assert.That(code, Does.Contain("Key.Escape"));

            // Узкий режим: ниже порога детали показываются вместо списка.
            Assert.That(code, Does.Contain("CompactWidthThreshold"));
        });
    }

    [Test]
    public void StylesXaml_ResolvesStaticResourcesInDeclarationOrder()
    {
        // Регрессия: оверлей «Package sources» падал при первом же рендере строки источника.
        // NuGet.OverlayCheckBoxStyle объявлялся ВЫШЕ своей базы NuGet.CheckBoxStyle, а BasedOn —
        // это StaticResource, который WPF разрешает по порядку разбора словаря:
        // «Не удается найти ресурс с именем "NuGet.CheckBoxStyle"». Ссылка жила внутри
        // DataTemplate, поэтому исключение вылетало не при загрузке панели, а при её отрисовке.
        string text = WithoutComments(Read(@"RPA.NuGet\Views\NuGetPanel.Styles.xaml"));

        var declaredAt = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match key in Regex.Matches(text, "x:Key=\"(?<key>[^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            string name = key.Groups["key"].Value;
            if (!declaredAt.ContainsKey(name))
            {
                declaredAt[name] = key.Index;
            }
        }

        var offenders = new List<string>();
        foreach (Match reference in Regex.Matches(text, "\\{StaticResource\\s+(?<key>[^}\\s]+)\\s*\\}", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            string name = reference.Groups["key"].Value;

            // Ключ объявлен не в этом словаре (палитра студии, тема) — порядок здесь ни при чём.
            if (!declaredAt.TryGetValue(name, out int declarationIndex))
            {
                continue;
            }

            if (declarationIndex > reference.Index)
            {
                offenders.Add($"{name} (ссылка: строка {LineOf(text, reference.Index)}, "
                    + $"объявление: строка {LineOf(text, declarationIndex)})");
            }
        }

        Assert.That(offenders, Is.Empty,
            "StaticResource обязан ссылаться на ранее объявленный ключ того же словаря, иначе в рантайме "
            + "«Не удается найти ресурс с именем …»: " + string.Join("; ", offenders));
    }

    /// <summary>Номер строки (с единицы) для позиции в тексте — для читаемых сообщений стража.</summary>
    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }


    private static string WithoutComments(string text)
    {
        return Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    }

    private static string Read(string relativePath)
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
