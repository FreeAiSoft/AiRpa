using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 9 плана (docs\Vs2026_Theme_Plan.md, §8 п.9 «Финал»): автоматическая часть чеклиста §9.
/// Ручная часть (реальный запуск студии) описана в AiRpaStudio\docs\Vs2026_Theme_Acceptance.md.
/// <para>
/// Здесь собраны сквозные стражи по ВСЕМ темизируемым XAML сразу: отсутствие зашитых цветов и
/// named-цветов, отсутствие «третьей» палитры, единственный источник семантических цветов и
/// полнота повторного применения темы. Отдельные этапы (4, 6, 7, 8) стерегут свои файлы,
/// этот набор ловит то, что осталось вне их области (Roslyn-редактор, generic.xaml ядра
/// AvalonDock, иконки проекта, legacy-словари).
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class ThemeAcceptanceTests
{
    /// <summary>Файлы, которые обязаны быть полностью на палитре (цвета — только ключами).</summary>
    private static readonly string[] ThemedXaml =
    {
        @"AiRpaStudio\App.xaml",
        @"AiRpaStudio\Views\MainView.xaml",
        @"AiRpaStudio\Views\ActivityCreatorView.xaml",
        @"AiRpaStudio\Views\ProjectCreatorView.xaml",
        @"AiRpaStudio\Views\VsProjectOpenView.xaml",
        @"AiRpaStudio\Views\VsTemplateProjectCreatorView.xaml",
        @"AiRpaStudio\Controls\SearchableComboBox.xaml",
        @"AiRpaStudio\Controls\ProjectManagerControl.xaml",
        @"AiRpaStudio\Controls\ToolboxPanel.xaml",
        @"AiRpaStudio\CSharpExpressionEditor\RoslynExpressionEditor.xaml",
        @"AiRpaStudio\Resources\ButtonStyles.xaml",
        @"AiRpaStudio\Themes\StudioTheme.xaml",
        @"AiRpaStudio\Themes\StudioScrollBar.xaml",
        @"AvalonDock.Themes.Metro\Theme.xaml",
        @"AvalonDock\Themes\generic.xaml",
        @"LogControlLibrary\Controls\LogViewerControl.xaml",
        @"RPA.Chat\Views\ChatPanel.xaml",
        @"RPA.Chat\Controls\ChatMessagesControl.xaml",
        @"RPA.Chat\Controls\PinnedChatMessageControl.xaml"
    };

    /// <summary>
    /// Разрешённые зашитые цвета: кнопка закрытия — системная семантика (docs\Vs2026_Theme_Plan.md §1 п.2).
    /// Значения словарей темы чата не проверяются: RPA.Chat не может ссылаться на RPA.Helpers,
    /// согласованность с палитрой стережёт ChatThemeBridgeTests.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedHex = new()
    {
        [@"AiRpaStudio\Themes\StudioTheme.xaml"] = new[] { "#E81123", "#FFFFFF" }
    };

    private static readonly string[] NamedColorPatterns =
    {
        "White", "Black", "Gray", "LightGray", "DarkGray", "Orange", "OrangeRed",
        "Green", "Blue", "Red", "AliceBlue", "LightBlue", "Lime", "Goldenrod"
    };

    [Test]
    public void ThemedXaml_HaveNoHardcodedColors()
    {
        var offenders = new List<string>();

        foreach (string file in ThemedXaml)
        {
            string text = WithoutComments(Read(file));
            string[] allowed = AllowedHex.TryGetValue(file, out string[] values) ? values : Array.Empty<string>();

            foreach (Match match in Regex.Matches(text, "#[0-9A-Fa-f]{6,8}", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                if (!Array.Exists(allowed, value => string.Equals(value, match.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{file}: {match.Value}");
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "зашитые цвета обходят палитру (docs\\Vs2026_Theme_Plan.md §1 п.1). " +
            "Берите кисть из палитры: Studio.Surface/SurfaceAlt/Hover/Selected/Separator/Foreground/…, " +
            "для семантики — Studio.Semantic.*: " + string.Join("; ", offenders));
    }

    [Test]
    public void ThemedXaml_HaveNoNamedColorAttributes()
    {
        var offenders = new List<string>();
        string pattern = "(Foreground|Background|Fill|Stroke|BorderBrush|Color)=\"(" +
                         string.Join("|", NamedColorPatterns) + ")\"";

        foreach (string file in ThemedXaml)
        {
            string text = WithoutComments(Read(file));
            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                offenders.Add($"{file}: {match.Value}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "named-цвета не следуют теме (в тёмной теме дают светлые артефакты): " + string.Join("; ", offenders));
    }

    [Test]
    public void ThemedXaml_DoNotUseMaterialDesignPaletteKeys()
    {
        // После M-04 палитра MaterialDesign нейтральна, но она не знает про «один фон»:
        // её Background/Selected отличаются от Studio.Surface/Studio.Selected.
        var offenders = new List<string>();

        foreach (string file in ThemedXaml)
        {
            if (file.StartsWith("RPA.Chat", StringComparison.Ordinal) ||
                file.StartsWith("AvalonDock", StringComparison.Ordinal) ||
                file.StartsWith("LogControlLibrary", StringComparison.Ordinal))
            {
                continue;
            }

            string text = WithoutComments(Read(file));
            foreach (Match match in Regex.Matches(text, "MaterialDesign\\.Brush\\.[A-Za-z.]+", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                offenders.Add($"{file}: {match.Value}");
            }
        }

        Assert.That(offenders, Is.Empty,
            "студия берёт кисти из StudioPalette, а не из палитры MaterialDesign: " + string.Join("; ", offenders));
    }

    [Test]
    public void LegacyThirdPalettes_AreRemoved()
    {
        string root = FindRepositoryRoot();

        foreach (string legacy in new[] { "StudioTheme.Dark.xaml", "StudioTheme.Light.xaml" })
        {
            string path = Path.Combine(root, "AiRpaStudio", "Themes", legacy);
            Assert.That(File.Exists(path), Is.False,
                $"{legacy} — третья палитра с прежними фиолетовыми/синими значениями Studio.*. " +
                "Оставлены только две палитры в StudioPalette.");
        }

        string project = Read(@"AiRpaStudio\AiRpaStudio.csproj");
        Assert.That(project, Does.Not.Contain("StudioTheme.Dark.xaml"), "csproj всё ещё собирает legacy-палитру");
        Assert.That(project, Does.Not.Contain("StudioTheme.Light.xaml"), "csproj всё ещё собирает legacy-палитру");
    }

    [Test]
    public void App_MergesOnlyOneStudioThemeDictionary()
    {
        string app = WithoutComments(Read(@"AiRpaStudio\App.xaml"));

        MatchCollection merged = Regex.Matches(app, "Source=\"Themes/StudioTheme", RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.That(merged.Count, Is.EqualTo(1), "в App.xaml должен подключаться ровно один словарь StudioTheme.xaml");
    }

    [Test]
    public void App_HasNoDynamicResourceLookupsInResourceValues()
    {
        // Регрессия этапа 9: в App.xaml лежал DrawingImage с Brush="{DynamicResource Studio.ForegroundMuted}".
        // В словарь попадает ResourceReferenceExpression, а не Brush: Freezable (DrawingImage/GeometryDrawing)
        // из-за этого нельзя заморозить, а Style-значение валит XamlParseException (Themes\StudioTheme.xaml).
        string app = WithoutComments(Read(@"AiRpaStudio\App.xaml"));

        Assert.That(app, Does.Not.Contain("{DynamicResource"),
            "ресурсы в Application.Resources объявляет StudioBrushes.ApplyToApplication() реальными Brush палитры, " +
            "а не {DynamicResource ...}: отложенная ссылка не является кистью");
        Assert.That(app, Does.Not.Contain("<DrawingImage"),
            "App.xaml объявлял неиспользуемый ресурс Bulb (Freezable) — декоративные иконки живут в разметке панели");
    }


    [Test]
    public void SemanticAliases_AreDeclaredAndFilledWithFrozenBrushes()
    {
        Assert.That(StudioBrushes.SemanticKeys, Is.Not.Empty);
        Assert.That(StudioBrushes.SemanticKeys, Is.Unique);
        Assert.That(StudioBrushes.SemanticKeys, Has.All.StartsWith("Studio.Semantic."));

        var target = new ResourceDictionary();
        StudioBrushes.Apply(target, null);

        foreach (string key in StudioBrushes.SemanticKeys)
        {
            Assert.That(target.Contains(key), Is.True, $"не заполнен семантический алиас {key}");
            Assert.That(target[key], Is.InstanceOf<SolidColorBrush>(), $"{key} должен быть SolidColorBrush");
            Assert.That(((Brush)target[key]).IsFrozen, Is.True, $"{key} должен быть заморожен");
        }
    }

    [Test]
    public void SemanticAliases_EncodeStateAndDoNotDependOnTheme()
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        StudioBrushes.ApplyPalette(dark, StudioPalette.Dark);
        StudioBrushes.ApplyPalette(light, StudioPalette.Light);

        Color run = ColorOf(dark, StudioBrushes.SemanticRunKey);
        Color pause = ColorOf(dark, StudioBrushes.SemanticPauseKey);
        Color stop = ColorOf(dark, StudioBrushes.SemanticStopKey);

        // Семантика кодируется цветом, поэтому chroma > 6 — в отличие от нейтральной палитры.
        Assert.That(StudioPalette.Chroma(run), Is.GreaterThan(6));
        Assert.That(run.G, Is.GreaterThan(run.R), "«запуск» — зелёный");
        Assert.That(run.G, Is.GreaterThan(run.B), "«запуск» — зелёный");
        Assert.That(stop.R, Is.GreaterThan(stop.G), "«стоп» — красный");
        Assert.That(stop.R, Is.GreaterThan(stop.B), "«стоп» — красный");
        Assert.That(pause.R, Is.GreaterThan(pause.B), "«пауза» — тёплая");

        foreach (string key in StudioBrushes.SemanticKeys)
        {
            Assert.That(ColorOf(light, key), Is.EqualTo(ColorOf(dark, key)),
                $"{key} — семантика данных, она не должна меняться при смене темы");
        }
    }

    [Test]
    public void ToolbarPlaybackAndFileIcons_UseSemanticBrushes()
    {
        string main = WithoutComments(Read(@"AiRpaStudio\Views\MainView.xaml"));

        Assert.That(main, Does.Contain("Studio.Semantic.Run"), "иконка «Запустить» должна брать цвет из семантики");
        Assert.That(main, Does.Contain("Studio.Semantic.Pause"), "иконка «Пауза» должна брать цвет из семантики");
        Assert.That(main, Does.Contain("Studio.Semantic.Stop"), "иконка «Остановить» должна брать цвет из семантики");
        Assert.That(main, Does.Not.Contain("#4169E1"),
            "кнопки сохранения были синими (акцент) — они декоративные, им нужен Studio.ToolbarForeground");

        string project = WithoutComments(Read(@"AiRpaStudio\Controls\ProjectManagerControl.xaml"));

        Assert.That(project, Does.Contain("Studio.Semantic.FolderIcon"));
        Assert.That(project, Does.Contain("Studio.Semantic.XamlFileIcon"));
        Assert.That(project, Does.Contain("Studio.Semantic.JsonFileIcon"));
        Assert.That(project, Does.Not.Contain("OrangeRed"));
    }

    [Test]
    public void SearchableComboBox_UsesPaletteAndHairlineInsteadOfShadow()
    {
        string xaml = WithoutComments(Read(@"AiRpaStudio\Controls\SearchableComboBox.xaml"));

        Assert.That(xaml, Does.Contain("Studio.Surface"), "popup — часть общей поверхности окна");
        Assert.That(xaml, Does.Contain("Studio.ForegroundMuted"), "placeholder — приглушённый текст палитры");
        Assert.That(xaml, Does.Not.Contain("#999999"), "placeholder был зашитым светлым серым");
        Assert.That(xaml, Does.Not.Contain("DropShadowEffect"), "тень не нужна: границу даёт hairline-рамка");
        Assert.That(xaml, Does.Contain("CornerRadius=\"6\""), "popup — «меню/подсказка», радиус 6 (§1 п.3)");
    }

    [Test]
    public void RoslynExpressionEditor_UsesPaletteAndHairlineErrorBadge()
    {
        string xaml = WithoutComments(Read(@"AiRpaStudio\CSharpExpressionEditor\RoslynExpressionEditor.xaml"));

        Assert.That(xaml, Does.Contain("Studio.Separator"), "рамка редактора — hairline палитры");
        Assert.That(xaml, Does.Contain("Studio.CloseHoverBackground"),
            "бейдж ошибки — единственный не-нейтральный цвет, как на hairline-адаптере этапа 7");
        Assert.That(xaml, Does.Not.Contain("TextBoxErrorIcon"),
            "многоцветная иконка ошибки (#FFDADADA/#FF1AA1E2/#FFFFFFFF) заменена hairline-бейджем");
        Assert.That(xaml, Does.Not.Contain("ResizeGripIcon"), "неиспользуемый ресурс удалён");
    }

    [Test]
    public void OverlayPreviewInGenericXaml_UsesBridgeKeys()
    {
        string xaml = WithoutComments(Read(@"AvalonDock\Themes\generic.xaml"));

        Assert.That(xaml, Does.Not.Contain("AliceBlue"), "рамка перетаскивания была светло-голубой");
        Assert.That(xaml, Does.Not.Contain("Background=\"White\""));
        Assert.That(xaml, Does.Contain("AvalonDock_OverlayFill"), "generic.xaml — fallback, он тоже должен читать мост M-02");
        Assert.That(xaml, Does.Contain("AvalonDock_OverlayStroke"));
    }

    [Test]
    public void LogPopup_HasNoDropShadow()
    {
        string xaml = WithoutComments(Read(@"LogControlLibrary\Controls\LogViewerControl.xaml"));

        Assert.That(xaml, Does.Not.Contain("DropShadowEffect"), "popup настроек лога — hairline, без тени");
    }

    [Test]
    public void DialogsAndMainWindow_UseSingleSurfaceBackground()
    {
        string[] windows =
        {
            @"AiRpaStudio\Views\MainView.xaml",
            @"AiRpaStudio\Views\ActivityCreatorView.xaml",
            @"AiRpaStudio\Views\ProjectCreatorView.xaml",
            @"AiRpaStudio\Views\VsProjectOpenView.xaml",
            @"AiRpaStudio\Views\VsTemplateProjectCreatorView.xaml"
        };

        foreach (string window in windows)
        {
            string xaml = WithoutComments(Read(window));
            Assert.That(xaml, Does.Contain("Background=\"{DynamicResource Studio.Surface}\""),
                $"{window}: фон окна должен быть Studio.Surface (§1 п.1)");
        }
    }

    [Test]
    public void ApplyTheme_ReappliesEverySubsystem()
    {
        string code = Read(@"AiRpaStudio\Models\AppSettings.cs");
        int start = code.IndexOf("internal void ApplyTheme(bool dark)", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "не найден AppSettings.ApplyTheme");

        string body = code.Substring(start, Math.Min(1800, code.Length - start));

        foreach (string call in new[]
                 {
                     "ApplyMaterialDesignTheme(dark)",
                     "StudioBrushes.ApplyToApplication()",
                     "WfDesignerTheme.ApplyToApplication()",
                     "ReapplyDockManagerResources()"
                 })
        {
            Assert.That(body, Does.Contain(call), $"смена темы не вызывает {call}");
        }
    }

    [Test]
    public void MainView_ReappliesOpenDocumentsOnThemeChange()
    {
        string code = Read(@"AiRpaStudio\Views\MainView.xaml.cs");

        Assert.That(code, Does.Contain("ReapplyWorkflowControlThemes"), "должен существовать повторный прогон WF-хелперов");

        int theme = code.LastIndexOf("AppSettings.ApplyTheme(", StringComparison.Ordinal);
        Assert.That(theme, Is.GreaterThanOrEqualTo(0), "MainView не реагирует на смену темы");

        string after = code.Substring(theme, Math.Min(400, code.Length - theme));
        Assert.That(after, Does.Contain("ReapplyWorkflowControlThemes"),
            "после смены темы открытые документы и панели должны перекрашиваться (§9 п.5)");
    }

    [Test]
    public void ThemePlan_IsMarkedAsFinished()
    {
        string plan = Read(@"AiRpaStudio\docs\Vs2026_Theme_Plan.md");

        Assert.That(plan, Does.Contain("этапы 1-10"), "статус плана должен фиксировать завершение этапов 1-10");
        Assert.That(plan, Does.Contain("| 10."), "в таблице этапов нет строки 10 (рамка докпанелей)");
        Assert.That(plan, Does.Contain("| 9."), "в таблице «Сделано по этапам» нет строки этапа 9");
        Assert.That(plan, Does.Contain("Vs2026_Theme_Acceptance.md"), "план должен ссылаться на чеклист приёмки");
    }

    [Test]
    public void AcceptanceChecklist_CoversEveryManualItem()
    {
        string checklist = Read(@"AiRpaStudio\docs\Vs2026_Theme_Acceptance.md");

        for (int item = 1; item <= 8; item++)
        {
            Assert.That(checklist, Does.Contain($"### {item}."), $"в чеклисте нет пункта §9.{item}");
        }

        Assert.That(checklist, Does.Contain("vstest.console.exe"), "в чеклисте должна быть команда прогона тестов");
        Assert.That(checklist, Does.Contain("ThemeAcceptanceTests"), "чеклист должен ссылаться на автоматические стражи");
    }

    [Test]
    public void AgentsMd_DocumentsSemanticBrushesAndChecklist()
    {
        string agents = Read("AGENTS.md");

        Assert.That(agents, Does.Contain("Studio.Semantic."), "AGENTS.md должен описывать семантические кисти");
        Assert.That(agents, Does.Contain("Vs2026_Theme_Acceptance.md"), "AGENTS.md должен ссылаться на чеклист приёмки");
        Assert.That(agents, Does.Contain("StudioTheme.Dark.xaml"),
            "AGENTS.md должен запрещать legacy-палитры (третью тему)");
    }

    [Test]
    public void MaterialDesignPlan_IsMarkedAsSuperseded()
    {
        string plan = Read(@"AiRpaStudio\docs\Theming_MaterialDesign_Plan.md");

        Assert.That(plan, Does.Contain("Vs2026_Theme_Plan.md"), "старый план должен ссылаться на актуальный");
        Assert.That(plan, Does.Contain("заменён"), "старый план должен быть помечен как заменённый");
    }

    [Test]
    public void MetroThemeFallbacks_AreNeutral()
    {
        string code = Read(@"AvalonDock.Themes.Metro\MetroTheme.cs");

        Assert.That(code, Does.Not.Contain("#DC143C"),
            "Crimson был акцентом вкладок AvalonDock — палитра студии нейтральна");
        Assert.That(code, Does.Not.Contain("#FF8C00"), "DarkOrange убран из fallback-набора");
        Assert.That(code, Does.Not.Contain("BrushConverter"),
            "fallback-кисти создаются из нейтральных Color и замораживаются (Neutral)");
        Assert.That(code, Does.Contain("SolidColorBrush"));
    }

    [Test]
    public void LegacyAvalonDockBrushesFile_IsRemoved()
    {
        string path = Path.Combine(FindRepositoryRoot(), "AvalonDock.Themes.Metro", "Brushes.xaml");

        Assert.That(File.Exists(path), Is.False,
            "Brushes.xaml никем не подключается и содержит светлую палитру (#FFFFFF/#41B1E1) — " +
            "источник светлых вставок; нейтральные fallback-значения живут в MetroTheme.Neutral");
    }

    private static Color ColorOf(ResourceDictionary target, string key)
    {
        Assert.That(target.Contains(key), Is.True, $"нет ресурса {key}");
        return ((SolidColorBrush)target[key]).Color;
    }

    private static string Read(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"не найден {path}");
        return File.ReadAllText(path);
    }

    /// <summary>Комментарии не участвуют в проверке: в них остались исторические значения цветов.</summary>
    private static string WithoutComments(string xaml)
    {
        return Regex.Replace(xaml, "<!--.*?-->", string.Empty, RegexOptions.Singleline, TimeSpan.FromSeconds(2));
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
