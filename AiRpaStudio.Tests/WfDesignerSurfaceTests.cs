using System;
using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Поверхность дизайнера WF: полоса вокруг активностей обязана принадлежать активной теме при любом
/// порядке переключений (dark → light → dark), а не только при первом включении.
/// <para>
/// Регресс, который здесь зафиксирован: после light → dark вокруг активностей оставался БЕЛЫЙ фон, хотя
/// сами активности перекрашивались. Механизм — в режиме кэша словаря WF (<c>WfColorResourcesOverride</c>)
/// полосу красят несколько подпроходов сразу (<c>WfDataGridTheme</c> — хост таблиц,
/// <c>WfVisualTreeThemer</c> — корень документа, <c>WfActivityTheme</c> — плиты), и каждый узнаёт «своё»
/// значение по своему признаку. В сторону светлой темы они срабатывали, а в сторону тёмной белый не
/// узнавался: проверка «это цвет палитры» отбрасывала #FFFFFF, потому что в тёмной палитре #FFFFFF
/// объявлен как <c>CloseHoverForeground</c>. Теперь у поверхности дизайнера есть собственный проход,
/// признак которого не зависит от истории: непрозрачный фон, которого нет среди токенов площади и текста
/// активной палитры, — чужой.
/// </para>
/// <para>
/// Почему тесты ставят режим кэша: в прежнем режиме белый дефолт подменяет
/// <c>WfDesignerTheme.ReplaceWorkflowDefaultColors</c> по значению, и дефект не воспроизводится —
/// баг живёт именно в режиме кэша, в котором этот проход выключен.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class WfDesignerSurfaceTests
{
    [Test]
    public void Apply_AfterLightAndBack_RepaintsTheBandAroundActivities()
    {
        (Grid root, Grid band, Grid host) = BuildSurfaceTree();

        InCacheMode(() =>
        {
            WfDesignerTheme.Apply(root, StudioPalette.Light);

            // Светлая тема: полоса поверхности белая. Именно это значение «запекалось» в элементах,
            // которые красят подпроходы: при возврате в тёмную его никто не узнавал.
            var lightSurface = new SolidColorBrush(StudioPalette.Light.Surface);
            band.Background = lightSurface;
            host.Background = lightSurface;

            WfDesignerTheme.Apply(root, StudioPalette.Dark);

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(band.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "полоса designerViewGrid после light → dark обязана вернуться в тёмную палитру: " +
                    "иначе вокруг активностей остаётся белый фон");
                Assert.That(ColorOf(host.Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "хост поверхности (Grid вокруг активностей) — то же самое");
            });
        });
    }

    [Test]
    public void Apply_SurfacePassIsIdempotent()
    {
        (Grid root, Grid band, _) = BuildSurfaceTree();

        InCacheMode(() =>
        {
            band.Background = new SolidColorBrush(StudioPalette.Light.Surface);

            WfDesignerTheme.Apply(root, StudioPalette.Dark);
            Brush first = band.Background;
            WfDesignerTheme.Apply(root, StudioPalette.Dark);

            Assert.That(band.Background, Is.SameAs(first), "повторный проход не должен создавать новые кисти");
        });
    }

    [Test]
    public void Apply_KeepsPaletteStatesAndTransparentBands()
    {
        (Grid root, Grid band, Grid host) = BuildSurfaceTree();

        InCacheMode(() =>
        {
            // Состояния палитры (выделение) и прозрачные полосы шаблона WF — «свои» значения:
            // проход поверхности обязан их сохранить, иначе выделение потеряет цвет.
            band.Background = new SolidColorBrush(StudioPalette.Dark.Selected);
            host.Background = Brushes.Transparent;

            WfDesignerTheme.Apply(root, StudioPalette.Dark);

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(band.Background), Is.EqualTo(StudioPalette.Dark.Selected),
                    "токен палитры — не «чужой» фон, перекрашивать его в Surface нельзя");
                Assert.That(ColorOf(host.Background), Is.EqualTo(Colors.Transparent),
                    "Transparent — не плашка поверхности, его трогать нельзя");
            });
        });
    }

    [Test]
    public void Apply_DoesNotTouchDataBoundBackgrounds()
    {
        (Grid root, Grid band, _) = BuildSurfaceTree();
        var host = new Border();
        band.Children.Add(host);
        host.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(ActivityPlate.Colour)) { Source = new ActivityPlate() });

        InCacheMode(() => WfDesignerTheme.Apply(root, StudioPalette.Dark));

        Assert.That(System.Windows.Data.BindingOperations.IsDataBound(host, Border.BackgroundProperty), Is.True,
            "привязка фона (цвет из модели активности) не должна разрываться локальным значением");
    }

    [Test]
    public void StaleColour_WhiteOfLightTheme_IsRecognisedInDarkTheme()
    {
        // Ядро регресса: белый — это Light.Surface, но в тёмной палитре #FFFFFF объявлен ещё и как
        // CloseHoverForeground, из-за чего проверка «цвет входит в активную палитру» отбрасывала его.
        bool recognised = WfThemeStyles.TryMapStalePaletteColour(
            new SolidColorBrush(Colors.White),
            StudioPalette.Dark,
            out Color mapped);

        Assert.Multiple(() =>
        {
            Assert.That(recognised, Is.True, "белый фон светлой темы обязан узнаваться в тёмной");
            Assert.That(mapped, Is.EqualTo(StudioPalette.Dark.Surface));
        });
    }

    [Test]
    public void StaleColour_ActiveThemeColourIsKept()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WfThemeStyles.TryMapStalePaletteColour(
                new SolidColorBrush(StudioPalette.Dark.Surface), StudioPalette.Dark, out _), Is.False);
            Assert.That(WfThemeStyles.TryMapStalePaletteColour(
                new SolidColorBrush(StudioPalette.Light.Surface), StudioPalette.Light, out _), Is.False,
                "белый в светлой теме — свой цвет поверхности, перекрашивать нечего");
        });
    }

    [Test]
    public void SurfacePass_DoesNotEnterActivitiesOrAdornerLayer()
    {
        string code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), @"AiRpaStudio\Helpers\WfDesignerTheme.cs"));

        Assert.That(code, Does.Contain("element is WorkflowViewElement || element is System.Windows.Documents.AdornerLayer"),
            "проход поверхности не имеет права заходить внутрь активностей (там связанные кисти " +
            "WfColorResourcesOverride, локальное значение сломало бы смену темы без обхода дерева) " +
            "и в слой адорнеров (там адаптеры ошибок)");
    }

    /// <summary>
    /// Выполняет проверку в режиме кэша словаря WF и всегда снимает установку: признак режима живёт на
    /// процесс, и «протёкший» режим изменил бы поведение остальных тестов темы.
    /// </summary>
    private static void InCacheMode(Action action)
    {
        try
        {
            Assert.That(WfColorResourcesOverride.TryInstall(StudioPalette.Dark, new ResourceDictionary(), new Hashtable()),
                Is.True, "режим кэша словаря WF не установился — тест проверяет именно его");

            action();
        }
        finally
        {
            WfColorResourcesOverride.Reset();
        }
    }

    private static (Grid Root, Grid Band, Grid Host) BuildSurfaceTree()
    {
        var root = new Grid { Name = "root" };
        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName };
        var band = new Grid { Name = "designerViewGrid" };
        var host = new Grid { Name = "scrollViewerContent" };
        var presenter = new Border { Name = "designerPresenterBorder" };

        host.Children.Add(presenter);
        band.Children.Add(host);
        designerView.Content = band;
        root.Children.Add(designerView);

        return (root, band, host);
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>(), "ожидалась сплошная кисть");
        return ((SolidColorBrush)brush).Color;
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

    /// <summary>Источник привязки для проверки «привязку не разрываем».</summary>
    private sealed class ActivityPlate
    {
        public Brush Colour { get; } = new SolidColorBrush(Colors.Orange);
    }
}
