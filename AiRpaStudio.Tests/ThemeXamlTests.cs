using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 4 плана (docs\Vs2026_Theme_Plan.md, §8 п.4): тесты-стражи по тексту
/// <c>AvalonDock.Themes.Metro\Theme.xaml</c> + проверка, что словарь темы реально загружается.
/// Тема ~2000 строк, поэтому регрессы ловятся регексами и полной загрузкой словаря, а не глазами.
/// </summary>
[Apartment(ApartmentState.STA)]
public class ThemeXamlTests
{
    private static string ReadTheme()
    {
        string path = Path.Combine(FindRepositoryRoot(), "AvalonDock.Themes.Metro", "Theme.xaml");
        Assert.That(File.Exists(path), Is.True, $"не найден {path}");
        return File.ReadAllText(path);
    }

    [Test]
    public void ThemeXaml_UsesNoLegacyAccentBrushes()
    {
        string text = ReadTheme();

        Match legacy = Regex.Match(text, "AvalonDock_ThemeMetro_BaseColor[1-7]", RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.That(legacy.Success, Is.False,
            $"шаблон ссылается на legacy-кисть {legacy.Value}: она объявлена акцентной (#DC143C/#FF8C00) — " +
            "используйте нейтральные ключи AvalonDock_Surface/_Foreground/_Separator/_Selection");
    }

    [Test]
    public void ThemeXaml_HasNoHardcodedColors()
    {
        string text = ReadTheme();

        MatchCollection colors = Regex.Matches(text, "#[0-9A-Fa-f]{6,8}", RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.That(colors.Count, Is.Zero,
            "в теме остались зашитые цвета: " + string.Join(", ", colors.Cast<Match>().Select(m => m.Value)));
    }

    [Test]
    public void ThemeXaml_HasNoLightArtifacts()
    {
        string text = ReadTheme();

        foreach (string offender in new[]
                 {
                     "AliceBlue",
                     "Background=\"White\"",
                     "SystemColors.Highlight",
                     "LinearGradientBrush",
                     "_Black.png",
                     "_Dark.png",
                     "Consolas"
                 })
        {
            Assert.That(text, Does.Not.Contain(offender), $"в теме остался артефакт светлой/акцентной темы: {offender}");
        }
    }

    [Test]
    public void ThemeXaml_ButtonStylesHaveHoverStates()
    {
        string text = ReadTheme();

        foreach (string styleKey in new[]
                 {
                     "AvalonDock_ThemeMetro_ToolButtonStyle",
                     "AvalonDock_ThemeMetro_ButtonStyle"
                 })
        {
            Match block = Regex.Match(text,
                $"<Style x:Key=\"{styleKey}\".*?</Style>",
                RegexOptions.Singleline,
                TimeSpan.FromSeconds(2));

            Assert.That(block.Success, Is.True, $"не найден стиль {styleKey}");
            Assert.That(block.Value, Does.Contain("IsMouseOver"), $"{styleKey} должен реагировать на hover");
            Assert.That(block.Value, Does.Contain("Value=\"{DynamicResource AvalonDock_SelectionHover}\""),
                $"{styleKey}: hover должен быть AvalonDock_SelectionHover");
            Assert.That(block.Value, Does.Contain("CornerRadius=\"4\""), $"{styleKey}: скругление кнопки — 4");
        }
    }

    [Test]
    public void ThemeXaml_PanesAndDocumentsUseHairlines()
    {
        string text = ReadTheme();

        Assert.That(text, Does.Contain("{DynamicResource AvalonDock_Separator}"), "hairline панелей/документов");

        Match resizer = Regex.Match(text,
            "<Style x:Key=\"\\{x:Type avalonDockControls:LayoutGridResizerControl\\}\".*?</Style>",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(2));

        Assert.That(resizer.Success, Is.True, "не найден стиль ресайзера панелей");
        Assert.That(resizer.Value, Does.Contain("Value=\"Transparent\""),
            "в покое ресайзер линии не рисует — зоны разделяет рамка панели (правило 6)");
        Assert.That(resizer.Value, Does.Contain("AvalonDock_SeparatorStrong"), "при hover ресайзер подсвечивается");
        Assert.That(Regex.IsMatch(resizer.Value, "Value=\"\\{DynamicResource AvalonDock_Separator\\}\""), Is.False,
            "постоянной линии в ресайзере быть не должно: между панелями осталась бы третья линия");
    }

    [Test]
    public void ThemeXaml_PaneFramesAreOnePixel()
    {
        string text = ReadTheme();

        // Панели главного окна (P-01…P-07): рамка 1px, у активной — SeparatorStrong.
        string anchorablePane = Segment(text,
            "<Style x:Key=\"AvalonDock_ThemeMetro_AnchorablePaneControlStyle\"",
            "<!--tabs-->");

        Assert.That(Regex.IsMatch(anchorablePane, "<Setter Property=\"BorderThickness\"\\s+Value=\"1\" />"), Is.True,
            "панель anchorable должна быть обведена рамкой 1px");
        Assert.That(anchorablePane, Does.Contain("{DynamicResource AvalonDock_Separator}"),
            "рамка неактивной панели — AvalonDock_Separator");
        Assert.That(anchorablePane, Does.Contain("x:Name=\"HeaderStrip\""),
            "полоса табов должна входить в рамку панели");
        Assert.That(Regex.Matches(anchorablePane,
                "TargetName=\"HeaderStrip\"\\s+Property=\"BorderThickness\"\\s+Value=\"0\"",
                RegexOptions.None,
                TimeSpan.FromSeconds(2)).Count, Is.EqualTo(2),
            "рамку полосы табов надо гасить дважды: внутри плавающего окна и у панели с одной вкладкой (иначе 2px на нижней кромке)");
        Assert.That(anchorablePane, Does.Contain("Model.SelectedContent.IsActive"),
            "рамка должна усиливаться у активной панели");
        Assert.That(anchorablePane, Does.Contain("{DynamicResource AvalonDock_SeparatorStrong}"),
            "активная панель — AvalonDock_SeparatorStrong");
        Assert.That(anchorablePane, Does.Contain("Model.IsHostedInFloatingWindow"),
            "внутри плавающего окна панель рамку не рисует (иначе двойная линия)");

        // Панель документов (P-08).
        string documentPane = Segment(text,
            "<Style x:Key=\"AvalonDock_ThemeMetro_DocumentPaneControlStyle\"",
            "<Setter Property=\"ItemContainerStyle\">");

        Assert.That(documentPane, Does.Contain("BorderThickness=\"1,1,1,0\""),
            "шапка документа должна быть в рамке с трёх сторон");
        Assert.That(Regex.IsMatch(documentPane, "<Border x:Name=\"ContentPanel\"[\\s\\S]*?BorderThickness=\"1\""), Is.True,
            "контент документа должен быть в рамке 1px");
        Assert.That(documentPane, Does.Contain("Model.IsHostedInFloatingWindow"),
            "внутри плавающего окна панель документов рамку не рисует");
        Assert.That(documentPane, Does.Contain("{DynamicResource AvalonDock_SeparatorStrong}"),
            "активная панель документов — AvalonDock_SeparatorStrong");

        // Панель автоскрытия (W-10): рамку рисует вложенный LayoutAnchorableControl.
        string autoHide = Segment(text,
            "<Style x:Key=\"AvalonDock_ThemeMetro_AutoHideAnchorableStyle\"",
            "AvalonDock_ThemeMetro_NavigatorWindowListBoxItemFocusVisual");

        Assert.That(Regex.IsMatch(autoHide, "<Setter Property=\"BorderThickness\"\\s+Value=\"1\" />"), Is.True,
            "стиль автоскрытия должен рисовать рамку 1px");
        Assert.That(autoHide, Does.Contain("AnchorableStyle"),
            "рамку автоскрытию даёт AnchorableStyle у LayoutAutoHideWindowControl");

        // Плавающие окна (W-06, W-07): активность передаётся рамкой окна, а не заливкой заголовка.
        foreach (string styleKey in new[]
                 {
                     "LayoutDocumentFloatingWindowControl",
                     "LayoutAnchorableFloatingWindowControl"
                 })
        {
            string floating = Segment(text,
                $"TargetType=\"{{x:Type avalonDockControls:{styleKey}}}\">",
                "\n    <Style ");

            MatchCollection strong = Regex.Matches(floating,
                "Property=\"([A-Za-z.]+)\"\\s+Value=\"\\{DynamicResource AvalonDock_SeparatorStrong\\}\"",
                RegexOptions.None,
                TimeSpan.FromSeconds(2));

            Assert.That(strong.Count, Is.GreaterThan(0),
                $"{styleKey}: активное плавающее окно должно подсвечиваться рамкой");

            foreach (Match match in strong.Cast<Match>())
            {
                Assert.That(match.Groups[1].Value, Is.EqualTo("BorderBrush"),
                    $"{styleKey}: SeparatorStrong допустим только как BorderBrush — активность не передаётся заливкой");
            }
        }
    }

    [Test]
    public void ThemeXaml_WindowsAreRounded()
    {
        string text = ReadTheme();

        Assert.That(text, Does.Not.Contain("CornerRadius=\"0\""), "у окон остался прямой угол");
        Assert.That(Regex.Matches(text, "CornerRadius=\"8\"", RegexOptions.None, TimeSpan.FromSeconds(2)).Count,
            Is.GreaterThanOrEqualTo(2), "плавающий документ и плавающая панель должны быть скруглены на 8");
    }

    [Test]
    public void ThemeXaml_EveryDynamicResourceKeyIsWrittenByStudio()
    {
        string text = ReadTheme();

        MatchCollection keys = Regex.Matches(text, "\\{DynamicResource (AvalonDock_[A-Za-z0-9_]+)\\}",
            RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.That(keys.Count, Is.GreaterThan(0));

        List<string> unknown = keys
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(key => !key.StartsWith("AvalonDock_ThemeMetro_", StringComparison.Ordinal))
            .Distinct()
            .Where(key => !DockingThemeBrushes.AllKeys.Contains(key))
            .ToList();

        Assert.That(unknown, Is.Empty,
            "тема ссылается на кисти, которые никто не записывает (в тёмной теме они останутся светлыми): " +
            string.Join(", ", unknown));
    }

    [Test]
    public void ThemeXaml_UsesOnlyDeclaredGlyphs()
    {
        string text = ReadTheme();

        MatchCollection used = Regex.Matches(text, "\\{StaticResource (AvalonDock_Glyph[A-Za-z0-9_]*)\\}",
            RegexOptions.None, TimeSpan.FromSeconds(2));
        MatchCollection declared = Regex.Matches(text, "x:Key=\"(AvalonDock_Glyph[A-Za-z0-9_]*)\"",
            RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.That(used.Count, Is.GreaterThan(0), "кнопки панелей должны рисоваться векторными глифами");

        HashSet<string> declaredKeys = declared.Cast<Match>().Select(m => m.Groups[1].Value).ToHashSet();
        List<string> missing = used
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Where(key => !declaredKeys.Contains(key))
            .ToList();

        Assert.That(missing, Is.Empty, "глиф не объявлен: " + string.Join(", ", missing));
    }

    [Test]
    public void ThemeXaml_LoadsWithoutErrors()
    {
        // Полный разбор XAML ловит синтаксис, неизвестные типы и битые ссылки на объявленные ресурсы.
        var dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(ReadTheme());

        Assert.That(dictionary.Keys.Count, Is.GreaterThan(0), "словарь темы пуст");

        foreach (string key in new[] { "AvalonDock_GlyphClose", "AvalonDock_GlyphMenu", "AvalonDock_GlyphPin" })
        {
            Assert.That(dictionary.Contains(key), Is.True, $"в теме нет ресурса {key}");
        }
    }

    // --- Пункт «тема не применена к переключателю вкладок в докпанели документов» (P-08). ---

    [Test]
    public void ThemeXaml_DocumentTabTemplateDrawsStates()
    {
        string text = ReadTheme();

        string style = Segment(text,
            "<Style x:Key=\"AvalonDock_ThemeMetro_DocumentPaneControlStyle\"",
            "<Setter Property=\"ItemTemplate\">");

        Assert.Multiple(() =>
        {
            // В исходном шаблоне вкладки был только Grid: BorderBrush менялся у самого TabItem,
            // который рамку не рисует, — hover и выделение не были видны вообще.
            Assert.That(style, Does.Contain("<Border x:Name=\"TabBorder\""),
                "шаблон вкладки документа должен рисовать Border (иначе состояния не видны)");
            Assert.That(style, Does.Contain("BorderThickness=\"1,1,1,0\""),
                "у вкладки документа должна быть hairline-рамка");
            Assert.That(style, Does.Contain("{DynamicResource AvalonDock_Base}"),
                "фон неактивной вкладки — AvalonDock_Base");
            Assert.That(style, Does.Contain("{DynamicResource AvalonDock_Hover}"),
                "hover вкладки должен брать AvalonDock_Hover");
            Assert.That(style, Does.Contain("{DynamicResource AvalonDock_Active}"),
                "выбранная вкладка — AvalonDock_Active");
            Assert.That(style, Does.Contain("{DynamicResource AvalonDock_Separator}"),
                "рамка вкладки — hairline AvalonDock_Separator");
            Assert.That(style, Does.Not.Contain("TabBackground"),
                "мёртвый Grid TabBackground вернулся: BorderBrush у него не рисуется");
        });

        // Все сеттеры рамки внутри шаблона обязаны быть адресованы Border шаблона (TargetName),
        // иначе они меняют свойство, которое ничем не отрисовывается.
        string template = Segment(style,
            "<ControlTemplate TargetType=\"{x:Type TabItem}\">",
            "</ControlTemplate>");

        List<string> untargeted = Regex.Matches(template,
                "<Setter\\b[^>]*/>",
                RegexOptions.None,
                TimeSpan.FromSeconds(2))
            .Cast<Match>()
            .Select(match => match.Value.Replace("\n", " ").Replace("\r", " "))
            .Where(value => value.Contains("Property=\"BorderBrush\"") && !value.Contains("TargetName="))
            .ToList();

        Assert.That(untargeted, Is.Empty,
            "BorderBrush задаётся у TabItem, а не у Border шаблона — рамка не появится: " +
            string.Join(" | ", untargeted));
    }

    [Test]
    public void ThemeXaml_DocumentTabCloseButtonUsesCloseHoverColors()
    {
        string text = ReadTheme();

        Match closeStyle = Regex.Match(text,
            "<Style x:Key=\"AvalonDock_ThemeMetro_DocumentCloseButtonStyle\".*?</Style>",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(2));

        Assert.That(closeStyle.Success, Is.True, "нет стиля кнопки закрытия вкладки документа");
        Assert.That(closeStyle.Value, Does.Contain("IsMouseOver"), "крестик вкладки должен реагировать на hover");
        Assert.That(closeStyle.Value, Does.Contain("{DynamicResource AvalonDock_CloseHoverBackground}"),
            "hover крестика — AvalonDock_CloseHoverBackground (семантика закрытия из палитры)");
        Assert.That(closeStyle.Value, Does.Contain("AvalonDock_ThemeMetro_ButtonStyle"),
            "стиль кнопки закрытия наследует общий стиль кнопок темы");

        string tabItem = Segment(text,
            "<!--#region LayoutDocumentTabItem  -->",
            "<!--#endregion LayoutDocumentTabItem  -->");

        Assert.That(tabItem, Does.Contain("AvalonDock_ThemeMetro_DocumentCloseButtonStyle"),
            "кнопка закрытия вкладки документа должна использовать стиль с hover");
        Assert.That(tabItem, Does.Contain("{DynamicResource AvalonDock_CloseHoverForeground}"),
            "глиф крестика при hover — AvalonDock_CloseHoverForeground");
    }

    [Test]
    public void DocumentTabStyle_RendersPaletteBrushes()
    {
        ResourceDictionary dictionary = LoadThemeDictionary();

        Style tabStyle = DocumentTabContainerStyle(dictionary);
        Color baseColor = Color.FromRgb(0x10, 0x10, 0x10);
        Color activeColor = Color.FromRgb(0x20, 0x20, 0x20);
        Color hoverColor = Color.FromRgb(0x30, 0x30, 0x30);
        Color separatorColor = Color.FromRgb(0x40, 0x40, 0x40);

        Border selected = BuildTabBorder(tabStyle, baseColor, hoverColor, activeColor, separatorColor, isSelected: true);
        Border regular = BuildTabBorder(tabStyle, baseColor, hoverColor, activeColor, separatorColor, isSelected: false);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(regular.Background), Is.EqualTo(baseColor),
                "неактивная вкладка документа должна брать фон AvalonDock_Base");
            Assert.That(ColorOf(selected.Background), Is.EqualTo(activeColor),
                "выбранная вкладка документа светлее неактивной (AvalonDock_Active)");
            Assert.That(ColorOf(selected.BorderBrush), Is.EqualTo(separatorColor),
                "у выбранной вкладки — hairline AvalonDock_Separator");
        });
    }

    /// <summary>Загружает словарь темы (тот же разбор, что и при старте студии).</summary>
    private static ResourceDictionary LoadThemeDictionary()
    {
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(ReadTheme());
    }

    /// <summary>Достаёт стиль контейнера вкладки из стиля панели документов.</summary>
    private static Style DocumentTabContainerStyle(ResourceDictionary dictionary)
    {
        var paneStyle = dictionary["AvalonDock_ThemeMetro_DocumentPaneControlStyle"] as Style;
        Assert.That(paneStyle, Is.Not.Null, "в теме нет стиля панели документов");

        Style tabStyle = paneStyle.Setters
            .OfType<Setter>()
            .Where(setter => setter.Property == ItemsControl.ItemContainerStyleProperty)
            .Select(setter => setter.Value)
            .OfType<Style>()
            .FirstOrDefault();

        Assert.That(tabStyle, Is.Not.Null, "у панели документов нет ItemContainerStyle — вкладки не темизируются");
        return tabStyle;
    }

    /// <summary>
    /// Материализует вкладку документа и возвращает Border шаблона: так проверяется, что состояния
    /// действительно рисуются кистями палитры, а не просто объявлены в XAML.
    /// </summary>
    private static Border BuildTabBorder(
        Style tabStyle,
        Color baseColor,
        Color hoverColor,
        Color activeColor,
        Color separatorColor,
        bool isSelected)
    {
        var tab = new TabItem
        {
            Style = tabStyle,
            Header = "document",
            IsSelected = isSelected
        };

        // Кисти, которые в студии пишет AppSettings.ReapplyDockManagerResources.
        tab.Resources["AvalonDock_Base"] = FrozenBrush(baseColor);
        tab.Resources["AvalonDock_Hover"] = FrozenBrush(hoverColor);
        tab.Resources["AvalonDock_Active"] = FrozenBrush(activeColor);
        tab.Resources["AvalonDock_Separator"] = FrozenBrush(separatorColor);

        tab.ApplyTemplate();
        tab.Measure(new Size(200, 30));
        tab.Arrange(new Rect(0, 0, 200, 30));

        Border border = XamlHelper.FindVisualChildren<Border>(tab)
            .FirstOrDefault(candidate => candidate.Name == "TabBorder");

        Assert.That(border, Is.Not.Null, "в шаблоне вкладки документа нет Border с именем TabBorder");
        return border;
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        SolidColorBrush brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.Not.Null, "кисть не задана — состояние вкладки не применилось");
        return ((SolidColorBrush)brush).Color;
    }

    /// <summary>
    /// Вырезает фрагмент темы между двумя маркерами: тема — 2000+ строк, сквозные регексы по всему файлу
    /// ловят чужие стили и дают ложные падения.
    /// </summary>
    private static string Segment(string text, string from, string to)
    {
        int start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"в теме не найден маркер: {from}");

        int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), $"в теме не найден маркер: {to} после {from}");

        return text.Substring(start, end - start);
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
