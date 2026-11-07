using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using LogControlLibrary.Converters;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 8 плана (docs\Vs2026_Theme_Plan.md, §8 п.8; F-07 и F-08).
/// <para>
/// F-07: <c>LogControlLibrary\Controls\LogViewerControl.xaml</c> был собран из светлых
/// цветов (<c>#FAFAFA</c>, <c>#F0F0F0</c>, <c>#CCCCCC</c>, <c>#E8E8E8</c>), а уровни логирования
/// приходили из конвертеров с «светлыми» кистями (<c>#F8F8F8</c> у hover Trace) — в тёмной теме
/// панель «Log» была светлым островом. Фоны/рамки/выделение переводятся на ключи палитры
/// (<c>Studio.*</c> заполняет <c>StudioBrushes</c>), а семантические цвета уровней становятся
/// средними тонами, читаемыми в обеих темах.
/// </para>
/// <para>
/// F-08: <c>RPA.Chat</c> держал собственный акцент (<c>Dark.Accent #264F78</c>,
/// <c>Dark.Primary #64B5F6</c>/<c>#1976D2</c>, синий <c>Dark.ControlPressed</c>) плюс зашитые hex
/// прямо в контролах. Все цвета чата переезжают в словари темы (единственное место), значения —
/// нейтральные токены палитры, а в студии словарь дополнительно перекрывается мостом
/// <c>ChatStudioBridge</c> из <c>Studio.*</c>.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class LogAndChatThemeTests
{
    private const string LogViewerPath = @"LogControlLibrary\Controls\LogViewerControl.xaml";
    private const string ChatDarkPath = @"RPA.Chat\Themes\ChatTheme.Dark.xaml";
    private const string ChatLightPath = @"RPA.Chat\Themes\ChatTheme.Light.xaml";

    // --- F-07: панель «Log». ---

    [Test]
    public void LogViewer_HasNoHardcodedColors()
    {
        string xaml = ReadSource(LogViewerPath);

        MatchCollection colors = Regex.Matches(xaml, "#[0-9A-Fa-f]{6,8}", RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.That(colors.Count, Is.Zero,
            "в панели «Log» остались зашитые цвета: " + string.Join(", ", colors.Cast<Match>().Select(m => m.Value)));
    }

    [Test]
    public void LogViewer_UsesPaletteKeys()
    {
        string xaml = ReadSource(LogViewerPath);

        foreach (string key in new[]
                 {
                     "Studio.Surface",
                     "Studio.Separator",
                     "Studio.Hover",
                     "Studio.Selected",
                     "Studio.Foreground",
                     "Studio.ForegroundMuted"
                 })
        {
            Assert.That(xaml, Does.Contain("{DynamicResource " + key + "}"),
                $"панель «Log» должна использовать ключ палитры {key}");
        }
    }

    [Test]
    public void LogViewer_HasNoNamedLightColors()
    {
        string xaml = ReadSource(LogViewerPath);

        foreach (string offender in new[] { "Foreground=\"Green\"", "Foreground=\"Blue\"", "Background=\"White\"", "BorderBrush=\"Gray\"" })
        {
            Assert.That(xaml, Does.Not.Contain(offender), $"в панели «Log» остался цвет {offender}");
        }
    }

    [Test]
    public void LogViewer_LevelColorsAreFrozenAndThemeNeutral()
    {
        BrushesOf(new LogLevelToColorConverter(), "цвет уровня лога").ToList().ForEach(brush =>
            Assert.That(IsNeutralOrSemantic(brush), Is.True,
                $"цвет уровня {ColorOf(brush)} не читается в обеих темах: нужен средний тон, а не чистый белый/чёрный"));

        BrushesOf(new LogLevelToHoverConverter(), "hover строки лога").ToList().ForEach(brush =>
            Assert.That(ColorOf(brush).A < 255 || IsNeutralOrSemantic(brush), Is.True,
                $"hover строки лога {ColorOf(brush)} должен быть полупрозрачной накладкой, иначе он светлее тёмного фона"));
    }

    [Test]
    public void LogViewer_RowBackgroundsAreOverlays()
    {
        // Фон строки задаётся наложением на фон панели: непрозрачный светлый цвет «съедал» бы тёмную тему.
        foreach (Brush brush in BrushesOf(new LogLevelToBackgroundConverter(), "фон строки лога"))
        {
            Assert.That(ColorOf(brush).A < 255, Is.True,
                $"фон строки лога {ColorOf(brush)} должен быть полупрозрачным");
        }
    }

    [Test]
    public void LogViewer_TraceRowsHaveNoBackgroundAtAll()
    {
        // Trace — «шум»: строки этого уровня не получают фона ни в покое, ни под курсором.
        Color background = ColorOf(new LogLevelToBackgroundConverter().TraceBrush);
        Color hover = ColorOf(new LogLevelToHoverConverter().TraceBrush);

        Assert.That(background.A, Is.Zero, "фон строки Trace обязан быть полностью прозрачным");
        Assert.That(hover.A, Is.Zero, "подсветка строки Trace обязана быть полностью прозрачной");
    }

    [Test]
    public void LogViewer_FilterIconsUseSemanticLevelKeys()
    {
        // Цвета уровней живут в палитре (StudioBrushes.SemanticKeys), а не hex-ом в разметке лога.
        string xaml = ReadSource(LogViewerPath);

        foreach (string key in LogFilterIconKeys())
        {
            Assert.That(xaml, Does.Contain("{DynamicResource " + key + "}"),
                $"иконка фильтра должна брать цвет из семантики уровня ({key})");
        }
    }

    [Test]
    public void LogFilterIconBrushes_AreClassicSemanticsAndOpaque()
    {
        var target = new ResourceDictionary();
        StudioBrushes.Apply(target);

        foreach (string key in LogFilterIconKeys())
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнен ключ иконки фильтра {key}");

            Color color = ColorOf(target[key]);
            Assert.That((int)color.A, Is.EqualTo(255), $"{key} = {color}: цвет иконки обязан быть непрозрачным (alpha = 255)");
        }

        // Классическая кодировка уровней: синий / зелёный / оранжевый / красный / тёмно-красный.
        Assert.That(ChannelOf(LogFilter("Debug"), 'B'), Is.GreaterThan(ChannelOf(LogFilter("Debug"), 'R')), "Debug — синий");
        Assert.That(ChannelOf(LogFilter("Info"), 'G'), Is.GreaterThan(ChannelOf(LogFilter("Info"), 'R')), "Info — зелёный");
        Assert.That(ChannelOf(LogFilter("Warning"), 'R'), Is.GreaterThan(ChannelOf(LogFilter("Warning"), 'B')), "Warning — оранжевый");
        Assert.That(ChannelOf(LogFilter("Warning"), 'G'), Is.GreaterThan(ChannelOf(LogFilter("Error"), 'G')), "Warning теплее Error");
        Assert.That(ChannelOf(LogFilter("Error"), 'R'), Is.GreaterThan(ChannelOf(LogFilter("Error"), 'G')), "Error — красный");
        Assert.That(ChannelOf(LogFilter("Fatal"), 'R'), Is.GreaterThan(ChannelOf(LogFilter("Fatal"), 'G')), "Fatal — красный");
        Assert.That(ChannelOf(LogFilter("Error"), 'R'), Is.GreaterThan(ChannelOf(LogFilter("Fatal"), 'R')), "Fatal темнее Error");
    }

    [Test]
    public void LogFilterIconBrushes_DoNotDependOnTheme()
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        StudioBrushes.ApplyPalette(dark, StudioPalette.Dark);
        StudioBrushes.ApplyPalette(light, StudioPalette.Light);

        foreach (string key in LogFilterIconKeys())
        {
            Assert.That(ColorOf(light[key]), Is.EqualTo(ColorOf(dark[key])),
                $"{key} — кодировка уровня, она не должна меняться при смене темы");
        }
    }

    [Test]
    public void LogViewer_EachFilterIconIsBoundToItsOwnLevelKey()
    {
        // Ключ, повешенный не на ту иконку, — та же регрессия, что и hex: проверяем каждый глиф поимённо.
        string xaml = ReadSource(LogViewerPath);

        var expected = new Dictionary<string, string>
        {
            { "Bug", StudioBrushes.SemanticLogDebugKey },
            { "Information", StudioBrushes.SemanticLogInfoKey },
            { "Warning", StudioBrushes.SemanticLogWarningKey },
            { "Error", StudioBrushes.SemanticLogErrorKey },
            { "Skull", StudioBrushes.SemanticLogFatalKey }
        };

        foreach (KeyValuePair<string, string> icon in expected)
        {
            Match element = Regex.Match(xaml,
                "<materialIcons:MaterialIcon Kind=\"" + icon.Key + "\"[^>]*/>",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(2));

            Assert.That(element.Success, Is.True, $"в панели «Log» нет иконки фильтра {icon.Key}");
            Assert.That(element.Value, Does.Contain("Foreground=\"{DynamicResource " + icon.Value + "}\""),
                $"иконка {icon.Key} должна брать цвет из {icon.Value}, а не наследовать цвет кнопки");

            // Ключ, которого нет в палитре, DynamicResource молча не найдёт — цвет пропадёт.
            Assert.That(StudioBrushes.SemanticKeys, Does.Contain(icon.Value),
                $"{icon.Value} обязан входить в SemanticKeys — его пишет StudioBrushes.Apply");
        }
    }

    [Test]
    public void LogViewer_TraceIconAndLettersUseStudioForeground()
    {
        // Trace — «шум»: своего цвета-сигнала у него нет, поэтому иконка и буквы красятся основным
        // цветом текста панели Studio.Foreground (белый в тёмной теме, чёрный в светлой).
        string xaml = ReadSource(LogViewerPath);

        Match traceIcon = Regex.Match(xaml,
            "<materialIcons:MaterialIcon Kind=\"TimelineTextOutline\"[^>]*/>",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(2));

        Assert.That(traceIcon.Success, Is.True, "в панели «Log» нет иконки фильтра Trace");
        Assert.That(traceIcon.Value, Does.Contain("Foreground=\"{DynamicResource " + StudioBrushes.ForegroundKey + "}\""),
            "иконка Trace обязана брать основной цвет текста панели (Studio.Foreground), а не серый");
        Assert.That(traceIcon.Value, Does.Not.Contain("Studio.Semantic.Log."),
            "у Trace нет собственного цвета-сигнала — он не входит в семантику уровней");

        // Палитра: тёмная тема даёт белый, светлая — чёрный.
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        StudioBrushes.ApplyPalette(dark, StudioPalette.Dark);
        StudioBrushes.ApplyPalette(light, StudioPalette.Light);

        Color darkForeground = ColorOf(dark[StudioBrushes.ForegroundKey]);
        Color lightForeground = ColorOf(light[StudioBrushes.ForegroundKey]);

        Assert.That(StudioPalette.Luminance(darkForeground), Is.GreaterThan(0.85),
            $"Trace в тёмной теме обязан быть белым, а не серым ({darkForeground})");
        Assert.That(StudioPalette.Luminance(lightForeground), Is.LessThan(0.15),
            $"Trace в светлой теме обязан быть чёрным, а не серым ({lightForeground})");

        // Буквы уровня в строке лога берут тот же ключ: строка ключа не должна разъехаться с палитрой.
        Assert.That(LogLevelToColorConverter.ForegroundKey, Is.EqualTo(StudioBrushes.ForegroundKey),
            "конвертер цвета уровня обязан читать тот же ключ палитры, что пишет StudioBrushes");

        // Вне палитры остаётся запасной средний тон: ни белого, ни чёрного, иначе он не читается в одной из тем.
        Brush fallback = new LogLevelToColorConverter().TraceBrush;
        Assert.That(fallback.IsFrozen, Is.True, "запасная кисть Trace обязана быть замороженной");
        Assert.That(IsNeutralOrSemantic(fallback), Is.True,
            $"запасной тон Trace {ColorOf(fallback)} обязан оставаться средним — он читается в обеих темах");
    }

    // --- F-08: панель «Chat». ---

    [Test]
    public void ChatXaml_HasNoHardcodedColors()
    {
        List<string> offenders = new();

        foreach (string file in ChatXamlFiles())
        {
            MatchCollection colors = Regex.Matches(File.ReadAllText(file), "#[0-9A-Fa-f]{6,8}",
                RegexOptions.None, TimeSpan.FromSeconds(2));
            offenders.AddRange(colors.Cast<Match>().Select(m => Path.GetFileName(file) + ": " + m.Value));
        }

        Assert.That(offenders, Is.Empty,
            "в контролах чата остались зашитые цвета (все они должны жить в ChatTheme.Dark/Light.xaml): " +
            string.Join(", ", offenders));
    }

    [Test]
    public void ChatXaml_HasNoLocalBrushOverrides()
    {
        // Локальный SolidColorBrush в контроле перебивает словарь темы — переключение Light/Dark перестаёт работать.
        foreach (string file in ChatXamlFiles())
        {
            string xaml = File.ReadAllText(file);

            Assert.That(xaml, Does.Not.Contain("<SolidColorBrush"),
                $"{Path.GetFileName(file)}: кисти объявляются только в ChatTheme.Dark.xaml / ChatTheme.Light.xaml");
        }
    }

    [Test]
    public void ChatThemeDictionaries_DeclareTheSameKeys()
    {
        ResourceDictionary dark = ParseDictionary(ReadSource(ChatDarkPath));
        ResourceDictionary light = ParseDictionary(ReadSource(ChatLightPath));

        List<string> darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k).ToList();
        List<string> lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k).ToList();

        Assert.That(darkKeys, Is.EqualTo(lightKeys),
            "наборы ключей тёмного и светлого словаря чата обязаны совпадать, иначе часть контролов остаётся без кисти");
    }

    [Test]
    public void ChatThemeDictionaries_HaveNoAccentColors()
    {
        HashSet<string> accentColors = new(StringComparer.OrdinalIgnoreCase)
        {
            "#264F78", // синий «accent» тёмной темы
            "#64B5F6", // голубой «primary» тёмной темы
            "#1976D2", // синий «primary/accent» светлой темы
            "#BBDEFB", // синий фон нажатия светлой темы
            "#98DDFB",
            "#6EA6F5"
        };

        foreach (string path in new[] { ChatDarkPath, ChatLightPath })
        {
            foreach (string color in ColorsOf(ReadSource(path)))
            {
                Assert.That(accentColors.Contains(color), Is.False,
                    $"{Path.GetFileName(path)}: акцентный цвет {color} запрещён правилами §1 плана");
            }
        }
    }

    [Test]
    public void ChatThemeDictionaries_KeepSurfacesNeutral()
    {
        foreach (string path in new[] { ChatDarkPath, ChatLightPath })
        {
            ResourceDictionary dictionary = ParseDictionary(ReadSource(path));

            foreach (string key in new[] { "Dark.Window", "Dark.Panel", "Dark.Control", "Dark.ControlHover", "Dark.ControlPressed", "Dark.Accent" })
            {
                Color color = ColorOf(dictionary[key]);
                int chroma = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));

                Assert.That(chroma, Is.LessThanOrEqualTo(6),
                    $"{Path.GetFileName(path)}: {key} = {color} — фон и состояния передаются только яркостью (chroma ≤ 6)");
            }
        }
    }

    [Test]
    public void ChatViewModel_ModeAccentsAreNeutral()
    {
        string source = ReadSource(@"RPA.Chat\ViewModels\ChatViewModel.cs");

        foreach (string accent in new[] { "#64B5F6", "#1976D2", "#4CAF50", "#F44336" })
        {
            Assert.That(source, Does.Not.Contain(accent),
                $"акцент режима {accent} в ChatViewModel заменяется нейтральным тоном (акцент запрещён §1)");
        }
    }

    // --- helpers ---

    private static IEnumerable<string> LogFilterIconKeys()
    {
        return new[]
        {
            StudioBrushes.SemanticLogDebugKey,
            StudioBrushes.SemanticLogInfoKey,
            StudioBrushes.SemanticLogWarningKey,
            StudioBrushes.SemanticLogErrorKey,
            StudioBrushes.SemanticLogFatalKey
        };
    }

    private static Color LogFilter(string level)
    {
        var target = new ResourceDictionary();
        StudioBrushes.Apply(target);

        string key = LogFilterIconKeys().Single(k => k.EndsWith("." + level, StringComparison.Ordinal));
        return ColorOf(target[key]);
    }

    private static int ChromaOf(Color color)
    {
        return Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
    }

    private static byte ChannelOf(Color color, char channel)
    {
        switch (channel)
        {
            case 'R':
                return color.R;
            case 'G':
                return color.G;
            default:
                return color.B;
        }
    }

    private static IEnumerable<Brush> BrushesOf(object converter, string what)
    {
        IEnumerable<System.Reflection.PropertyInfo> brushes = converter.GetType()
            .GetProperties()
            .Where(property => property.PropertyType == typeof(Brush));

        foreach (System.Reflection.PropertyInfo property in brushes)
        {
            Brush brush = (Brush)property.GetValue(converter);

            Assert.That(brush, Is.Not.Null, $"{what}: {property.Name} не задан");
            Assert.That(brush.IsFrozen, Is.True, $"{what}: {property.Name} должен быть заморожен");
            yield return brush;
        }
    }

    private static bool IsNeutralOrSemantic(Brush brush)
    {
        Color color = ColorOf(brush);

        // Ни чистого белого, ни чистого чёрного: оба нечитаемы в одной из двух тем.
        bool extremes = (color.R > 245 && color.G > 245 && color.B > 245) || (color.R < 24 && color.G < 24 && color.B < 24);
        return !extremes;
    }

    private static IEnumerable<string> ChatXamlFiles()
    {
        string root = Path.Combine(FindRepositoryRoot(), "RPA.Chat");

        return Directory
            .GetFiles(root, "*.xaml", SearchOption.AllDirectories)
            // net48: у string нет перегрузки Contains(string, StringComparison) — используем IndexOf.
            .Where(file => file.IndexOf(Path.Combine("RPA.Chat", "Themes"), StringComparison.OrdinalIgnoreCase) < 0)
            .OrderBy(file => file);
    }

    private static IEnumerable<string> ColorsOf(string xaml)
    {
        return Regex.Matches(xaml, "#[0-9A-Fa-f]{6,8}", RegexOptions.None, TimeSpan.FromSeconds(2))
            .Cast<Match>()
            .Select(match => match.Value.ToUpperInvariant());
    }

    private static ResourceDictionary ParseDictionary(string xaml)
    {
        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    private static Color ColorOf(object value)
    {
        Assert.That(value, Is.InstanceOf<SolidColorBrush>(), "ожидалась сплошная кисть");
        return ((SolidColorBrush)value).Color;
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
