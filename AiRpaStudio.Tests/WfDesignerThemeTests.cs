using System.Activities.Presentation;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Этап 7 плана (docs\Vs2026_Theme_Plan.md, §8 п.7, F-04…F-06): поверхность дизайнера WF.
/// <para>
/// Требования этапа: поверхность дизайнера и shell bar на палитре (один фон, hairline вместо рамок),
/// ключи <c>DesignerViewBackground</c> / <c>DesignerViewForeground</c> объявлены в палитре
/// (F-05: на <c>DesignerViewBackground</c> ссылается <c>CSharpExpressionEditor\RoslynExpressionEditor.xaml</c>),
/// адаптеры ошибок и контекстные меню дизайнера — на палитре, красный только для ошибок (F-06).
/// </para>
/// <para>
/// Проверки идут по синтетическому дереву, повторяющему визуальную структуру <c>DesignerView</c>
/// (имена <c>xnDesignerView</c>, <c>shellBar</c>, <c>breadCrumbListBox</c>, <c>miniMap</c> взяты из
/// Reference Source, см. docs\Reference_Source_Storage.md), поэтому HWND и настоящий
/// <c>WorkflowDesigner</c> тестам не нужны.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class WfDesignerThemeTests
{
    // --- F-05: ключи палитры, на которые ссылаются шаблоны WF и RoslynExpressionEditor. ---

    [Test]
    public void Resources_DeclareDesignerBackgroundAndForeground()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(values.ContainsKey(WfDesignerTheme.DesignerViewBackgroundKey), Is.True,
                "ключ DesignerViewBackground обязателен: на него ссылается RoslynExpressionEditor.xaml");
            Assert.That(values.ContainsKey(WfDesignerTheme.DesignerViewForegroundKey), Is.True);
            Assert.That(ColorOf((Brush)values[WfDesignerTheme.DesignerViewBackgroundKey]), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf((Brush)values[WfDesignerTheme.DesignerViewForegroundKey]), Is.EqualTo(StudioPalette.Dark.Foreground));
        });
    }

    [Test]
    public void Resources_AreFrozenAndNeutral()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Light);
        HashSet<Color> paletteColors = new(StudioPalette.Light.Tokens.Values);

        Assert.That(values, Is.Not.Empty);

        foreach (KeyValuePair<string, object> pair in values)
        {
            Assert.That(pair.Value, Is.InstanceOf<SolidColorBrush>(), $"{pair.Key}: ожидалась сплошная кисть");
            SolidColorBrush brush = (SolidColorBrush)pair.Value;

            Assert.That(brush.IsFrozen, Is.True, $"{pair.Key}: кисть должна быть заморожена");
            Assert.That(paletteColors.Contains(brush.Color), Is.True,
                $"{pair.Key}: цвет {brush.Color} не из палитры — зашитые hex и акцент MaterialDesign запрещены");
        }
    }

    [Test]
    public void Resources_DeclarePropertyInspectorKeys()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorTextBrushKey), Is.True);
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorBackgroundBrushKey), Is.True);
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorSelectedBackgroundBrushKey), Is.True);
            Assert.That(values.ContainsKey(WorkflowDesignerColors.PropertyInspectorToolBarSeparatorBrushKey), Is.True);
            Assert.That(ColorOf((Brush)values[WorkflowDesignerColors.PropertyInspectorSelectedBackgroundBrushKey]),
                Is.EqualTo(StudioPalette.Dark.Selected), "выделение в инспекторе — яркость палитры, а не системный Highlight");
        });
    }

    [Test]
    public void Resources_DeclareDesignerContextMenuKeys()
    {
        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf((Brush)values["ContextMenuBackgroundBrush"]), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf((Brush)values["ContextMenuBorderBrush"]), Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
            Assert.That(ColorOf((Brush)values["ContextMenuSeparatorBrush"]), Is.EqualTo(StudioPalette.Dark.Separator));
            Assert.That(ColorOf((Brush)values["ContextMenuTextBrush"]), Is.EqualTo(StudioPalette.Dark.Foreground));
        });
    }

    // --- Применение словаря к ресурсам (одна точка записи, без накопления словарей). ---

    [Test]
    public void ApplyToApplication_NullArguments_DoNotThrow()
    {
        Assert.DoesNotThrow(() => WfDesignerTheme.ApplyToApplication(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfDesignerTheme.ApplyToApplication(new ResourceDictionary(), null));
    }

    [Test]
    public void ApplyToApplication_AddsSingleMergedDictionary()
    {
        var target = new ResourceDictionary();

        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Dark);
        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Dark);

        Assert.That(target.MergedDictionaries, Has.Count.EqualTo(1), "повторное применение не должно добавлять словарь");
        Assert.That(ColorOf(GetBrush(target, WfDesignerTheme.DesignerViewBackgroundKey)), Is.EqualTo(StudioPalette.Dark.Surface));
    }

    [Test]
    public void ApplyToApplication_UpdatesValuesInPlaceOnThemeChange()
    {
        var target = new ResourceDictionary();

        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Dark);
        WfDesignerTheme.ApplyToApplication(target, StudioPalette.Light);

        Assert.That(target.MergedDictionaries, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(GetBrush(target, WfDesignerTheme.DesignerViewBackgroundKey)), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(GetBrush(target, WorkflowDesignerColors.PropertyInspectorTextBrushKey)), Is.EqualTo(StudioPalette.Light.Foreground));
        });
    }

    // --- F-04: темизация поверхности дизайнера. ---

    [Test]
    public void Apply_NullRoot_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => WfDesignerTheme.Apply(null));
        Assert.DoesNotThrow(() => WfDesignerTheme.Apply(null, StudioPalette.Dark));
        Assert.DoesNotThrow(() => WfDesignerTheme.Apply(new Grid(), null));
    }

    [Test]
    public void Apply_SetsSurfaceOnRootAndDesignerView()
    {
        (Grid root, UserControl designerView, _, _, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(root.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(designerView.Background), Is.EqualTo(StudioPalette.Dark.Surface));
        });
    }

    [Test]
    public void Apply_MergesPaletteIntoDesignerViewResources()
    {
        (Grid root, UserControl designerView, _, _, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        WfDesignerTheme.Apply(root, StudioPalette.Light);

        Assert.That(designerView.Resources.MergedDictionaries, Has.Count.EqualTo(1),
            "словарь палитры должен переиспользоваться, а не накапливаться");
        Assert.That(ColorOf(GetBrush(designerView.Resources, WfDesignerTheme.DesignerViewBackgroundKey)),
            Is.EqualTo(StudioPalette.Light.Surface),
            "локальные ColorResources дизайнера перекрывают словарь приложения — значение должно обновиться");
    }

    [Test]
    public void Apply_ReplacesWorkflowDefaultSurfaceColors()
    {
        (Grid root, _, Border designerSurface, _, _, _, _) = BuildDesignerViewTree();

        // Дефолтные цвета WF приходят из StaticResource и не пересчитываются при смене темы,
        // поэтому темизатор заменяет их по значению (белая поверхность дизайнера, фон активности).
        designerSurface.Background = new SolidColorBrush(WorkflowDesignerColors.DesignerViewBackgroundColor);
        var activityBorder = new Border { Background = new SolidColorBrush(WorkflowDesignerColors.WorkflowViewElementBackgroundColor) };
        root.Children.Add(activityBorder);

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(designerSurface.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(activityBorder.Background), Is.EqualTo(StudioPalette.Dark.Surface));
        });
    }

    [Test]
    public void Apply_DoesNotTouchCustomBackgrounds()
    {
        (Grid root, _, _, _, _, _, _) = BuildDesignerViewTree();
        var custom = new Border { Background = Brushes.Magenta };
        root.Children.Add(custom);

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.That(ColorOf(custom.Background), Is.EqualTo(Colors.Magenta));
    }

    [Test]
    public void Apply_ThemesShellBar()
    {
        (Grid root, _, _, StatusBar shellBar, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(shellBar.Background), Is.EqualTo(StudioPalette.Dark.Surface), "shell bar на том же фоне, что и поверхность");
            Assert.That(ColorOf(shellBar.BorderBrush), Is.EqualTo(StudioPalette.Dark.Separator), "вместо акцентной полосы — hairline");
            Assert.That(ColorOf(shellBar.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(shellBar.FontFamily.Source, Is.EqualTo(WfThemeStyles.FontFamilyName));
        });
    }

    [Test]
    public void Apply_ThemesBreadCrumbAndMiniMap()
    {
        (Grid root, _, _, _, ListBox breadCrumb, UserControl miniMap, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(breadCrumb.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(breadCrumb.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(breadCrumb.BorderThickness, Is.EqualTo(new Thickness(0)));
            Assert.That(ColorOf(miniMap.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(miniMap.BorderBrush), Is.EqualTo(StudioPalette.Dark.Separator));
        });
    }

    [Test]
    public void Apply_ThemesText()
    {
        (Grid root, _, _, _, _, _, TextBlock caption) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        Assert.That(ColorOf(caption.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
    }

    [Test]
    public void Apply_SwitchesPalette()
    {
        (Grid root, UserControl designerView, _, _, _, _, _) = BuildDesignerViewTree();

        WfDesignerTheme.Apply(root, StudioPalette.Dark);
        Assert.That(ColorOf(designerView.Background), Is.EqualTo(StudioPalette.Dark.Surface));

        WfDesignerTheme.Apply(root, StudioPalette.Light);
        Assert.That(ColorOf(designerView.Background), Is.EqualTo(StudioPalette.Light.Surface));
    }

    // --- F-06: ошибки (красный только для ошибок) и контекстные меню дизайнера. ---

    [Test]
    public void Apply_SetsErrorAdornerTemplate()
    {
        (Grid root, _, _, _, _, _, _) = BuildDesignerViewTree();
        var textBox = new TextBox();
        root.Children.Add(textBox);

        WfDesignerTheme.Apply(root, StudioPalette.Dark);

        ControlTemplate template = Validation.GetErrorTemplate(textBox);

        Assert.Multiple(() =>
        {
            Assert.That(template, Is.Not.Null, "адаптер ошибки должен быть нашим, а не системным красным прямоугольником");
            Assert.That(template.VisualTree, Is.Not.Null);
            Assert.That(template.IsSealed, Is.True);
            Assert.That(ColorOf(WfDesignerTheme.ErrorBrush(StudioPalette.Dark)), Is.EqualTo(StudioPalette.Dark.CloseHoverBackground),
                "красный зарезервирован под ошибки (системная семантика, не акцент)");
        });
    }

    [Test]
    public void ErrorColor_IsRedAndNotUsedElsewhere()
    {
        Color error = WfDesignerTheme.ErrorBrush(StudioPalette.Dark).Color;

        Assert.That(error.R, Is.GreaterThan(error.G + 64), "токен ошибки должен быть красным");
        CollectionAssert.DoesNotContain(
            WfDesignerTheme.CreateResourceValues(StudioPalette.Dark).Values.OfType<SolidColorBrush>().Select(b => b.Color).ToList(),
            error,
            "красный не должен попадать в обычные кисти дизайнера");
    }

    [Test]
    public void CreateEditingContextMenu_UsesPalette()
    {
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(menu, Is.Not.Null);
            Assert.That(ColorOf(menu.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(menu.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(ColorOf(menu.BorderBrush), Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
            Assert.That(menu.FontFamily.Source, Is.EqualTo(WfThemeStyles.FontFamilyName));
            Assert.That(menu.HasDropShadow, Is.False, "тень даёт «светлый» прямоугольник поверх тёмной темы");
            Assert.That(menu.Items, Has.Count.EqualTo(3), "Вырезать/Копировать/Вставить");
        });

        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            Assert.That(ColorOf(item.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
        }
    }

    [Test]
    public void CreateEditingContextMenu_NullPalette_FallsBackToDark()
    {
        ContextMenu menu = null;

        Assert.DoesNotThrow(() => menu = WfDesignerTheme.CreateEditingContextMenu(null));
        Assert.That(ColorOf(menu.Background), Is.EqualTo(StudioPalette.Dark.Surface));
    }

    // --- Стражи по исходникам: точки подключения и отсутствие зашитых цветов. ---

    [Test]
    public void Sources_AreWiredIntoStudio()
    {
        string app = ReadSource(Path.Combine("AiRpaStudio", "App.xaml.cs"));
        Assert.That(app, Does.Contain("WfDesignerTheme.ApplyToApplication"),
            "ключи DesignerViewBackground/Foreground должны быть объявлены до создания первого дизайнера");

        string settings = ReadSource(Path.Combine("AiRpaStudio", "Models", "AppSettings.cs"));
        Assert.That(settings, Does.Contain("WfDesignerTheme.ApplyToApplication"), "смена темы должна обновлять ключи дизайнера");

        string docGroup = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "DocGroupHelper.cs"));
        Assert.That(docGroup, Does.Contain("WfDesignerTheme.Apply"), "поверхность дизайнера темизируется при открытии документа");

        string mainView = ReadSource(Path.Combine("AiRpaStudio", "Views", "MainView.xaml.cs"));
        Assert.That(mainView, Does.Contain("WfDesignerTheme.Apply"), "уже открытые дизайнеры перекрашиваются при смене темы");

        string service = ReadSource(Path.Combine("AiRpaStudio", "Core", "Services", "DesignerVariableContextMenuService.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("WfDesignerTheme.CreateEditingContextMenu"));
            Assert.That(service, Does.Not.Contain("Colors.Black"), "зашитый чёрный фон в контекстном меню запрещён");
            Assert.That(service, Does.Not.Contain("Colors.Green"));
            Assert.That(Regex.IsMatch(service, @"\bDebug\.WriteLine", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "отладочные WriteLine не должны оставаться в темизации");
        });
    }

    [Test]
    public void Sources_UseNoHardcodedColors()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.That(Regex.IsMatch(source, @"#[0-9A-Fa-f]{6}", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
            "цвета берутся только из StudioPalette");
        Assert.That(source, Does.Not.Contain("MaterialThemePalette"));
    }

    [Test]
    public void RoslynExpressionEditor_ReferencesDeclaredKeys()
    {
        string xaml = ReadSource(Path.Combine("AiRpaStudio", "CSharpExpressionEditor", "RoslynExpressionEditor.xaml"));
        Assert.That(xaml, Does.Contain(WfDesignerTheme.DesignerViewBackgroundKey));

        Dictionary<string, object> values = WfDesignerTheme.CreateResourceValues(StudioPalette.Dark);
        Assert.That(values.ContainsKey(WfDesignerTheme.DesignerViewBackgroundKey), Is.True,
            "ключ, на который ссылается RoslynExpressionEditor.xaml, обязан быть объявлен");
    }

    [Test]
    public void ProjectFile_CompilesDesignerTheme()
    {
        string csproj = ReadSource(Path.Combine("AiRpaStudio", "AiRpaStudio.csproj"));
        Assert.That(csproj, Does.Contain("Helpers\\WfDesignerTheme.cs"));
    }

    // --- Построители и помощники. ---

    /// <summary>
    /// Синтетическое дерево, повторяющее структуру <c>DesignerView</c>:
    /// корень (WorkflowDesigner.View) → <c>xnDesignerView</c> → поверхность дизайнера,
    /// shell bar, breadcrumb, MiniMap и подпись.
    /// </summary>
    private static (Grid Root, UserControl DesignerView, Border DesignerSurface, StatusBar ShellBar, ListBox BreadCrumb, UserControl MiniMap, TextBlock Caption) BuildDesignerViewTree()
    {
        var root = new Grid { Name = "root" };
        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName };
        var surface = new Border();
        var shellBar = new StatusBar { Name = WfDesignerTheme.ShellBarElementName };
        var breadCrumb = new ListBox { Name = WfDesignerTheme.BreadCrumbElementName, BorderThickness = new Thickness(1) };
        var miniMap = new UserControl { Name = WfDesignerTheme.MiniMapElementName };
        var caption = new TextBlock { Text = "Sequence" };

        var layout = new Grid();
        layout.Children.Add(surface);
        layout.Children.Add(shellBar);
        layout.Children.Add(breadCrumb);
        layout.Children.Add(miniMap);
        layout.Children.Add(caption);

        designerView.Content = layout;
        root.Children.Add(designerView);

        return (root, designerView, surface, shellBar, breadCrumb, miniMap, caption);
    }

    private static Brush GetBrush(ResourceDictionary resources, string key)
    {
        Assert.That(resources, Is.Not.Null);
        Assert.That(resources.Contains(key), Is.True, $"в ресурсах нет ключа {key}");
        Assert.That(resources[key], Is.InstanceOf<Brush>(), $"{key}: ожидалась кисть");
        return (Brush)resources[key];
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>());
        return ((SolidColorBrush)brush).Color;
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
