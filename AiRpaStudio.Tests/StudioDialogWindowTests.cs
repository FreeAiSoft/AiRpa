using System.IO;
using System.Text.RegularExpressions;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 8 плана (docs\Vs2026_Theme_Plan.md, §8 п.8; W-02…W-05 + M-04).
/// <para>
/// Четыре диалога студии («Создать активность», «Создать проект», «Открыть VS-проект»,
/// «Из VS-шаблона») до этапа не имели ни фона, ни foreground: в тёмной теме это белые окна
/// со светлым системным заголовком. Требования этапа: фон и текст только из палитры
/// (<c>Studio.Surface</c>/<c>Studio.Foreground</c>), ноль зашитых цветов, отступы 12,
/// компактная кнопка обзора 28px, все <c>Grid.Row</c> существуют среди <c>RowDefinition</c>
/// (в <c>ActivityCreatorView</c> кнопка стояла в строке 3 при трёх объявленных строках).
/// </para>
/// <para>
/// Плюс M-04: тема MaterialDesign создаётся нейтральными Primary/Secondary (без DeepPurple/Lime),
/// а базой приложения объявлена тёмная тема — до этапа <c>App.xaml</c> стартовал в Light,
/// хотя настройки по умолчанию дают dark.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class StudioDialogWindowTests
{
    private static readonly string[] DialogPaths =
    {
        @"AiRpaStudio\Views\ActivityCreatorView.xaml",
        @"AiRpaStudio\Views\ProjectCreatorView.xaml",
        @"AiRpaStudio\Views\VsProjectOpenView.xaml",
        @"AiRpaStudio\Views\VsTemplateProjectCreatorView.xaml",
        @"AiRpaStudio\Views\ErrorView.xaml"
    };

    [Test]
    public void Dialogs_UsePaletteBackgroundAndForeground()
    {
        foreach (string relativePath in DialogPaths)
        {
            string xaml = ReadSource(relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(xaml, Does.Contain("Background=\"{DynamicResource Studio.Surface}\""),
                    $"{relativePath}: фон окна должен быть Studio.Surface, иначе в тёмной теме остаётся белое окно");
                Assert.That(xaml, Does.Contain("Foreground=\"{DynamicResource Studio.Foreground}\""),
                    $"{relativePath}: без Studio.Foreground системный текст остаётся чёрным на тёмном фоне");
            });
        }
    }

    [Test]
    public void Dialogs_HaveNoHardcodedColors()
    {
        foreach (string relativePath in DialogPaths)
        {
            MatchCollection colors = Regex.Matches(ReadSource(relativePath), "#[0-9A-Fa-f]{6,8}",
                RegexOptions.None, TimeSpan.FromSeconds(2));

            Assert.That(colors.Count, Is.Zero,
                $"{relativePath}: зашитые цвета запрещены, только ключи палитры — найдено: " +
                string.Join(", ", colors.Cast<Match>().Select(m => m.Value)));
        }
    }

    [Test]
    public void Dialogs_EveryGridRowIsDeclared()
    {
        foreach (string relativePath in DialogPaths)
        {
            string xaml = ReadSource(relativePath);

            int declaredRows = Regex.Matches(xaml, "<RowDefinition", RegexOptions.None, TimeSpan.FromSeconds(2)).Count;
            MatchCollection usedRows = Regex.Matches(xaml, "Grid\\.Row=\"(\\d+)\"", RegexOptions.None, TimeSpan.FromSeconds(2));

            foreach (Match used in usedRows.Cast<Match>())
            {
                int index = int.Parse(used.Groups[1].Value);
                Assert.That(index, Is.LessThan(declaredRows),
                    $"{relativePath}: Grid.Row=\"{index}\" при {declaredRows} объявленных RowDefinition — " +
                    "WPF молча клампит строку, из-за чего вёрстка «плывёт»");
            }
        }
    }

    [Test]
    public void Dialogs_BrowseButtonsAreCompact()
    {
        foreach (string relativePath in DialogPaths)
        {
            string xaml = ReadSource(relativePath);

            Assert.That(xaml, Does.Not.Contain("Width=\"40\""),
                $"{relativePath}: кнопка обзора «...» должна быть 28px (было 40)");

            // net48: у string нет перегрузки Contains(string, StringComparison) — используем IndexOf.
            if (xaml.IndexOf("CommandParameter=\"SetFolder\"", StringComparison.Ordinal) >= 0 ||
                xaml.IndexOf("CommandParameter=\"SetFile\"", StringComparison.Ordinal) >= 0)
            {
                Assert.That(xaml, Does.Contain("Width=\"28\""),
                    $"{relativePath}: у кнопки обзора должен быть Width=\"28\"");
            }
        }
    }

    [Test]
    public void Dialogs_UseTwelvePixelOuterMargin()
    {
        foreach (string relativePath in DialogPaths)
        {
            string xaml = ReadSource(relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(xaml, Does.Contain("Margin=\"12\""), $"{relativePath}: внешний отступ должен быть 12");
                Assert.That(xaml, Does.Not.Contain("Margin=\"5\""), $"{relativePath}: отступ 5 заменяется на 12");
            });
        }
    }

    [Test]
    public void ActivityCreator_ButtonIsInDeclaredRow()
    {
        string xaml = ReadSource(@"AiRpaStudio\Views\ActivityCreatorView.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("Grid.Row=\"2\""), "кнопка Create должна стоять в последней объявленной строке (2)");
            Assert.That(xaml, Does.Not.Contain("Grid.Row=\"3\""), "строки 3 в ActivityCreatorView нет");
        });
    }

    [Test]
    public void Dialogs_AreRegisteredForDwmCorners()
    {
        foreach (string relativePath in DialogPaths)
        {
            string codeBehind = ReadSource(relativePath + ".cs");

            Assert.That(codeBehind, Does.Contain("WindowCorner.Enable(this)"),
                $"{relativePath}.cs: окно должно скругляться (этап 5, M-03)");
        }
    }

    // --- M-04: нейтральная тема MaterialDesign и тёмная база по умолчанию. ---

    [Test]
    public void MaterialDesignTheme_IsNeutralAndComesFromPalette()
    {
        string source = ReadSource(@"AiRpaStudio\Models\AppSettings.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("DeepPurple"),
                "акцент DeepPurple пробивается в фокус, выделение и скроллбары стандартных контролов");
            Assert.That(source, Does.Not.Contain("Lime"),
                "вторичный акцент Lime запрещён правилами §1 плана");
            Assert.That(source, Does.Contain("StudioPalette"),
                "Primary/Secondary должны браться из палитры студии, а не из зашитых цветов");
        });
    }

    [Test]
    public void AppBaseTheme_IsDark()
    {
        string xaml = ReadSource(@"AiRpaStudio\App.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(xaml, Does.Contain("BaseTheme=\"Dark\""),
                "стартовая тема обязана совпадать с дефолтом настроек (dark), иначе первый кадр светлый");
            Assert.That(xaml, Does.Not.Contain("BaseTheme=\"Light\""));
            Assert.That(xaml, Does.Not.Contain("DeepPurple"));
            Assert.That(xaml, Does.Not.Contain("Lime"));
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
