using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

using NUnit.Framework;

using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Оформление контекстных меню дизайнера: собственные шаблоны на палитре (<c>WfContextMenuTheme</c>),
/// а не только локальные <c>Background</c>/<c>Foreground</c> поверх чужого шаблона.
/// <para>
/// Дефект, который держат эти тесты: меню в таблицах «Переменные»/«Аргументы» выглядело белым с белыми
/// буквами. Причина двухслойная: (1) меню живёт в popup и в проходы темы по дереву дизайнера не попадает;
/// (2) рисовали его чужие шаблоны (<c>MaterialDesignContextMenu</c>/<c>MaterialDesignMenuItem</c> из
/// <c>App.xaml</c>, в тестовом процессе — системный Aero2) — локальные кисти до отрисовки доходили
/// не полностью, а подсветка пункта строится от <c>Foreground</c> (светлая накладка на тёмной теме).
/// Поэтому проверки не верят кистям меню «на слово», а открывают настоящее меню в окне и смотрят на
/// материализованные части шаблона.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class WfContextMenuThemeTests
{
    // --- Фабрика меню: свои шаблон, панель пунктов и стиль контейнера. ---

    [Test]
    public void CreateEditingContextMenu_HasOwnTemplates()
    {
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);
        List<MenuItem> items = menu.Items.OfType<MenuItem>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(menu.Template, Is.Not.Null,
                "меню осталось на чужом шаблоне (MaterialDesign/Aero2): локальные кисти внешний вид не гарантируют");
            Assert.That(menu.ItemContainerStyleSelector, Is.Not.Null,
                "у пунктов нет своего стиля: рисует чужой шаблон пункта, а его подсветка строится от Foreground");
            Assert.That(menu.ItemContainerStyle, Is.Null,
                "ItemContainerStyle нельзя ставить на меню: он применяется и к Separator, и падает на TargetType");
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(
                items.All(item => item.Style != null
                    && item.Style.Setters.OfType<Setter>().Any(setter => setter.Property == Control.TemplateProperty)),
                Is.True,
                "пункт обязан получить стиль с собственным шаблоном: иначе тень/подсветка/углы приходят из чужого словаря");
            Assert.That(menu.ItemsPanel, Is.Not.Null, "ItemsPresenter без ItemsPanel не рисует пункты вовсе");
            Assert.That(items.All(item => WfThemeStyles.IsThemed(item.Style)), Is.True,
                "стиль пункта обязан быть помечен как стиль темизатора");
            Assert.That(WfThemeStyles.PaletteOf(items[0].Style), Is.SameAs(StudioPalette.Dark),
                "стиль пункта обязан помнить палитру: иначе смена темы не пересоберёт шаблон");
        });
    }

    [Test]
    public void CreateEditingContextMenu_RendersPaletteWithoutApplicationResources()
    {
        // В тестовом процессе нет словарей MaterialDesign — это ровно тот случай, когда меню рисует чужой
        // (системный) шаблон: так и появлялась светлая плашка с белыми буквами.
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        Window window = ShowWindow(designer);

        try
        {
            ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);
            Open(menu, designer, window);

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(MenuPlate(menu).Background), Is.EqualTo(StudioPalette.Dark.Surface),
                    "поверхность меню осталась светлой: шаблон чужой");
                Assert.That(TextBackdrops(menu), Is.All.EqualTo(StudioPalette.Dark.Surface),
                    "за буквами пунктов светлая плашка чужого шаблона");
                Assert.That(TextColors(menu), Is.SubsetOf(TextTokens(StudioPalette.Dark)),
                    "буквы пунктов не на палитре: на тёмной поверхности их не видно");
                Assert.That(
                    menu.Items.OfType<MenuItem>().All(item => item.Template != null
                        && item.Template.FindName(WfContextMenuTheme.MenuItemBorderPartName, item) is Border),
                    Is.True,
                    "контейнеры пунктов остались на чужом шаблоне: ItemContainerStyle не применился");
            });

            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CreateEditingContextMenu_HasNoLightPlates()
    {
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        Window window = ShowWindow(designer);

        try
        {
            ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);
            Open(menu, designer, window);

            double surface = StudioPalette.Luminance(StudioPalette.Dark.Surface);
            HashSet<Color> paletteColors = PaletteColors(StudioPalette.Dark);
            var light = new List<string>();

            foreach (Border border in VisualWalk(menu).OfType<Border>())
            {
                if (!(border.Background is SolidColorBrush solid) || solid.Color.A == 0)
                {
                    continue;
                }

                if (!paletteColors.Contains(solid.Color)
                    && StudioPalette.Luminance(solid.Color) > surface + WfThemeStyles.LightPlateThreshold)
                {
                    light.Add($"{border.Name}: {solid.Color}");
                }
            }

            Assert.That(light, Is.Empty, "светлые плашки в меню: " + string.Join(", ", light));
            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CreateEditingContextMenu_TextIsReadableInBothThemes()
    {
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        Window window = ShowWindow(designer);

        try
        {
            foreach (StudioThemeColors palette in new[] { StudioPalette.Dark, StudioPalette.Light })
            {
                ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(palette);

                // Недоступная команда (в таблице пространств имён ячейки только для чтения) обязана
                // читаться приглушённым текстом, а не пропадать.
                MenuItem disabled = menu.Items.OfType<MenuItem>().Last();
                disabled.IsEnabled = false;

                Open(menu, designer, window);

                Color plate = ColorOf(MenuPlate(menu).Background);
                Color disabledText = ColorOf(disabled.Foreground);

                Assert.Multiple(() =>
                {
                    Assert.That(plate, Is.EqualTo(palette.Surface));
                    Assert.That(TextColors(menu), Is.SubsetOf(TextTokens(palette)),
                        "текст меню не на палитре: чужой шаблон оставил свои буквы");
                    Assert.That(Contrast(palette.Foreground, plate), Is.GreaterThanOrEqualTo(4.5),
                        $"текст на поверхности меню не читается ({(palette.IsDark ? "dark" : "light")})");
                    Assert.That(disabledText, Is.EqualTo(palette.ForegroundMuted),
                        "недоступный пункт обязан быть ForegroundMuted, а не белым/системным");
                    Assert.That(Contrast(disabledText, plate), Is.GreaterThanOrEqualTo(3d),
                        "недоступный пункт не читается на поверхности меню");
                });

                Close(menu, window);
            }
        }
        finally
        {
            window.Close();
        }
    }


    [Test]
    public void CreateEditingContextMenu_RendersNestedSubmenuItems()
    {
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        Window window = ShowWindow(designer);

        try
        {
            ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);
            MenuItem parent = menu.Items.OfType<MenuItem>().First();
            _ = parent.Items.Add(new MenuItem { Header = "Вставить точку останова" });
            _ = parent.Items.Add(new MenuItem { Header = "Удалить точку останова" });

            Open(menu, designer, window);

            parent.IsSubmenuOpen = true;
            Pump(window);

            Popup popup = parent.Template?.FindName("PART_Popup", parent) as Popup;
            Assert.That(popup, Is.Not.Null, "PART_Popup обязан быть в шаблоне пункта меню");
            Assert.That(popup.IsOpen, Is.True, "подменю не открылось");

            List<string> texts = Walk(popup.Child)
                .OfType<TextBlock>()
                .Where(text => !string.IsNullOrEmpty(text.Text))
                .Select(text => text.Text)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(texts, Does.Contain("Вставить точку останова"),
                    "вложенные пункты не отрисовываются: в PART_Popup нет ItemsPresenter");
                Assert.That(texts, Does.Contain("Удалить точку останова"),
                    "вложенные пункты не отрисовываются: в PART_Popup нет ItemsPresenter");
            });

            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CreateEditingContextMenu_ShowsSubmenuChevron()
    {
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        Window window = ShowWindow(designer);

        try
        {
            ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);
            MenuItem parent = menu.Items.OfType<MenuItem>().First();
            _ = parent.Items.Add(new MenuItem { Header = "Вставить точку останова" });
            MenuItem leaf = menu.Items.OfType<MenuItem>().Skip(1).First();

            Open(menu, designer, window);

            var parentArrow = parent.Template?.FindName(WfContextMenuTheme.SubmenuArrowPartName, parent) as System.Windows.Shapes.Path;
            var leafArrow = leaf.Template?.FindName(WfContextMenuTheme.SubmenuArrowPartName, leaf) as System.Windows.Shapes.Path;

            Assert.Multiple(() =>
            {
                Assert.That(parentArrow, Is.Not.Null, "у шаблона пункта должен быть шеврон подменю");
                Assert.That(parentArrow.Visibility, Is.EqualTo(Visibility.Visible),
                    "пункт-подкатегория обязан показывать шеврон справа");
                Assert.That(parentArrow.Fill, Is.InstanceOf<SolidColorBrush>());
                Assert.That(ColorOf(parentArrow.Fill), Is.EqualTo(StudioPalette.Dark.Foreground),
                    "шеврон обязан быть в цвете текста пункта");

                Assert.That(leafArrow, Is.Not.Null, "у шаблона листового пункта тоже есть шеврон, но свёрнут");
                Assert.That(leafArrow.Visibility, Is.EqualTo(Visibility.Collapsed),
                    "у пункта без подменю шеврона быть не должно");
            });

            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }


    // --- Перекраска: меню создаётся один раз, тема меняется. ---

    [Test]
    public void RecolorEditingContextMenu_RebuildsTemplatesForNewPalette()
    {
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        Window window = ShowWindow(designer);

        try
        {
            ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);
            Open(menu, designer, window);
            Assert.That(ColorOf(MenuPlate(menu).Background), Is.EqualTo(StudioPalette.Dark.Surface),
                "предпосылка: меню отрисовано тёмной палитрой");
            Close(menu, window);

            ControlTemplate darkTemplate = menu.Template;
            Style darkStyle = menu.Items.OfType<MenuItem>().First().Style;

            WfDesignerTheme.RecolorEditingContextMenu(menu, StudioPalette.Light);
            Open(menu, designer, window);

            Assert.Multiple(() =>
            {
                Assert.That(menu.Template, Is.Not.SameAs(darkTemplate),
                    "шаблон меню не пересобран: в нём остались кисти прежней темы");
                Assert.That(menu.Items.OfType<MenuItem>().First().Style, Is.Not.SameAs(darkStyle),
                    "стиль пунктов не пересобран: подсветка осталась от прежней палитры");
                Assert.That(WfThemeStyles.PaletteOf(menu.Items.OfType<MenuItem>().First().Style), Is.SameAs(StudioPalette.Light));
                Assert.That(ColorOf(MenuPlate(menu).Background), Is.EqualTo(StudioPalette.Light.Surface),
                    "меню, созданное в тёмной теме, обязано перекрашиваться при смене темы");
                Assert.That(TextColors(menu), Is.SubsetOf(TextTokens(StudioPalette.Light)),
                    "текст меню остался от прежней палитры");
            });

            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }

    // --- Меню самого WF-дизайнера (не наша фабрика): popup вне дерева, поэтому нужен общий проход. ---

    [Test]
    public void Apply_ThemesForeignDesignerContextMenu()
    {
        StudioThemeColors palette = ExpectedPalette();
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        var field = new TextBox();
        designer.Children.Add(field);
        Window window = ShowWindow(designer);

        try
        {
            Pump(window);
            WfDesignerTheme.ApplyToApplication();

            ContextMenu menu = CreateForeignMenu();
            field.ContextMenu = menu;

            Open(menu, field, window);

            Assert.Multiple(() =>
            {
                Assert.That(WfContextMenuTheme.PaletteOf(menu), Is.Not.Null,
                    "общий проход по меню дизайнера не сработал: меню осталось на чужом стиле");
                Assert.That(menu.Template, Is.Not.Null);
                Assert.That(ColorOf(MenuPlate(menu).Background), Is.EqualTo(palette.Surface),
                    "белая плашка чужого шаблона осталась: меню в popup не попадает в проход по дереву дизайнера");
                Assert.That(TextColors(menu), Is.SubsetOf(TextTokens(palette)),
                    "белые буквы на белой плашке: текст меню не перекрашен");
            });

            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_ThemesDesignerMenuWithSeparator()
    {
        // Меню самого WF (например контекстное меню панели «Свойства») содержит разделители. Правый клик
        // поднимает ContextMenuOpening ДО показа меню — на этом проходе стиль уже наш, поэтому генератор
        // контейнеров не падает на Separator, а разделитель получает hairline палитры.
        StudioThemeColors palette = ExpectedPalette();
        var designer = new Grid { Name = WfDesignerTheme.DesignerViewElementName };
        var field = new TextBox();
        designer.Children.Add(field);
        Window window = ShowWindow(designer);

        try
        {
            Pump(window);
            WfDesignerTheme.ApplyToApplication();

            ContextMenu menu = CreateForeignMenu();
            Separator separator = new Separator();
            _ = menu.Items.Add(separator);
            field.ContextMenu = menu;

            // Правый клик в дизайнере: ContextMenuOpening приходит до показа меню (WPF поднимает его на
            // элементе, у которого есть ContextMenu) — этот проход подключает WfContextMenuTheme.Hook().
            WfContextMenuTheme.PrepareOpen(menu, field);

            Assert.Multiple(() =>
            {
                Assert.That(menu.ItemContainerStyle, Is.Null,
                    "чужой ItemContainerStyle снят до показа меню: иначе Separator роняет открытие");
                Assert.That(WfThemeStyles.IsThemed(separator.Style), Is.True,
                    "разделитель обязан получить стиль палитры");
            });

            Assert.DoesNotThrow(() => Open(menu, field, window));
            Assert.That(ColorOf(MenuPlate(menu).Background), Is.EqualTo(palette.Surface),
                "меню с разделителем обязано темизироваться так же, как меню без него");

            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Hook_LeavesMenusOutsideDesignerAlone()
    {
        // Общий проход ставится на класс ContextMenu, поэтому обязан ограничиваться поверхностью
        // дизайнера: меню AvalonDock, дерева проекта и чата живут на своих стилях.
        var host = new Grid { Background = Brushes.White };
        var field = new TextBox();
        host.Children.Add(field);
        Window window = ShowWindow(host);

        try
        {
            Pump(window);
            WfDesignerTheme.ApplyToApplication();

            ContextMenu menu = CreateForeignMenu();
            field.ContextMenu = menu;

            Open(menu, field, window);

            Assert.That(ColorOf(MenuPlate(menu).Background), Is.EqualTo(Colors.White),
                "меню вне поверхности дизайнера не должно перекрашиваться");

            Close(menu, window);
        }
        finally
        {
            window.Close();
        }
    }

    // --- Стражи по исходникам. ---

    [Test]
    public void Sources_ContextMenuThemeUsesPaletteOnly()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfContextMenuTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("new ControlTemplate(typeof(ContextMenu))"),
                "меню обязано получать собственный шаблон");
            Assert.That(source, Does.Contain("ItemContainerStyle"), "пункты обязаны получать собственный стиль");
            Assert.That(source, Does.Contain("Hook("), "общий проход по меню дизайнера обязан быть");
            Assert.That(source, Does.Contain("ContextMenu.OpenedEvent"),
                "проход по меню подписан на Opened: у popup-ребёнка Loaded обработчикам класса не доставляется");
            Assert.That(source, Does.Contain("ContextMenuService.ContextMenuOpeningEvent"),
                "меню правого клика темизируется до показа: иначе Separator с чужим стилем роняет открытие");
            Assert.That(Regex.IsMatch(source, @"#[0-9A-Fa-f]{6}", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "цвета берутся только из StudioPalette");
            Assert.That(Regex.IsMatch(source, @"\bColors\.", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "зашитые системные цвета запрещены");
            Assert.That(source, Does.Not.Contain("SystemColors"));
            Assert.That(source, Does.Not.Contain("MaterialDesign.Brush"),
                "кисти MaterialDesign в меню дизайнера запрещены: палитра одна");
            Assert.That(source, Does.Not.Contain("DropShadowEffect"),
                "тень меню даёт светлый прямоугольник поверх тёмной темы");
        });
    }

    [Test]
    public void Sources_FactoryAndRecolorGoThroughMenuTheme()
    {
        string theme = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(theme, Does.Contain("WfContextMenuTheme.Apply("),
                "фабрика и перекраска меню обязаны идти через одну точку — WfContextMenuTheme");
            Assert.That(CountOf(theme, "WfContextMenuTheme.Apply("), Is.EqualTo(1),
                "шаблоны ставятся в одной точке — в перекраске, через которую идёт и фабрика");
            Assert.That(theme, Does.Contain("WfContextMenuTheme.Hook()"),
                "общий проход подключается там же, где объявляются ключи дизайнера (старт и смена темы)");
        });
    }

    [Test]
    public void ProjectFile_CompilesContextMenuTheme()
    {
        string project = ReadSource(Path.Combine("AiRpaStudio", "AiRpaStudio.csproj"));

        Assert.That(project, Does.Contain("Helpers\\WfContextMenuTheme.cs"),
            "новый файл обязан быть в legacy-csproj: иначе он не компилируется");
    }

    [Test]
    public void Document_RecordsMenuTemplateInvariant()
    {
        string document = ReadSource(Path.Combine("AiRpaStudio", "docs", "Designer_Variable_ContextMenu_Principles.md"));

        Assert.Multiple(() =>
        {
            Assert.That(document, Does.Contain("WfContextMenuTheme"),
                "документ обязан называть единую точку оформления меню");
            Assert.That(document, Does.Contain("popup"), "причина (popup вне проходов темы) должна быть зафиксирована");
            Assert.That(document, Does.Contain("белые буквы"),
                "симптом дефекта должен быть в документе: иначе его снова будут «лечить» локальными кистями");
        });
    }

    // --- Инфраструктура: живое окно, открытие меню, части шаблона. ---

    private static Window ShowWindow(FrameworkElement content)
    {
        var window = new Window { Width = 360, Height = 240, ShowInTaskbar = false, Content = content };
        window.Show();
        return window;
    }

    private static void Open(ContextMenu menu, UIElement target, Window window)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Center;
        menu.IsOpen = true;
        Pump(window);
        Pump(window);
    }

    private static void Close(ContextMenu menu, Window window)
    {
        menu.IsOpen = false;
        Pump(window);
    }

    /// <summary>Светлое «чужое» меню: так выглядит меню на шаблоне MaterialDesign/Aero2.</summary>
    private static ContextMenu CreateForeignMenu()
    {
        var chrome = new FrameworkElementFactory(typeof(Border));
        chrome.SetValue(Border.BackgroundProperty, Brushes.White);
        chrome.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));

        var itemsPanel = new FrameworkElementFactory(typeof(StackPanel));
        itemsPanel.SetValue(StackPanel.IsItemsHostProperty, true);

        var itemChrome = new FrameworkElementFactory(typeof(Border));
        itemChrome.SetValue(Border.BackgroundProperty, Brushes.White);
        itemChrome.SetValue(Border.PaddingProperty, new Thickness(12, 4, 12, 4));
        itemChrome.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));

        var itemStyle = new Style(typeof(MenuItem));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        itemStyle.Setters.Add(new Setter(
            Control.TemplateProperty,
            new ControlTemplate(typeof(MenuItem)) { VisualTree = itemChrome }));

        var menu = new ContextMenu
        {
            Template = new ControlTemplate(typeof(ContextMenu)) { VisualTree = chrome },
            ItemsPanel = new ItemsPanelTemplate(itemsPanel),
            ItemContainerStyle = itemStyle,
            Background = Brushes.White,
            Foreground = Brushes.White
        };

        _ = menu.Items.Add(new MenuItem { Header = "Вырезать" });
        _ = menu.Items.Add(new MenuItem { Header = "Копировать" });
        return menu;
    }

    /// <summary>Фон поверхности меню: часть шаблона по имени, иначе — корневой Border шаблона.</summary>
    private static Border MenuPlate(ContextMenu menu)
    {
        Assert.That(menu, Is.Not.Null);

        if (menu.Template?.FindName(WfContextMenuTheme.MenuBorderPartName, menu) is Border named)
        {
            return named;
        }

        Border root = VisualChildren(menu).OfType<Border>().FirstOrDefault();
        Assert.That(root, Is.Not.Null, "шаблон меню не отрисован: у меню нет ни фона, ни рамки");
        return root;
    }

    private static List<Color> TextColors(DependencyObject root)
    {
        List<TextBlock> texts = Walk(root).OfType<TextBlock>().Where(text => !string.IsNullOrEmpty(text.Text)).ToList();
        Assert.That(texts, Is.Not.Empty, "текст пунктов меню не отрисован");
        return texts.Select(text => ColorOf(text.Foreground)).Distinct().ToList();
    }

    /// <summary>
    /// Первая непрозрачная кисть фона под каждым текстом меню — то, что реально видно за буквами.
    /// Так ловится светлая плашка чужого шаблона, даже если сам пункт объявил прозрачный фон.
    /// </summary>
    private static List<Color> TextBackdrops(DependencyObject root)
    {
        List<TextBlock> texts = Walk(root).OfType<TextBlock>().Where(text => !string.IsNullOrEmpty(text.Text)).ToList();
        Assert.That(texts, Is.Not.Empty, "текст пунктов меню не отрисован");

        return texts.Select(text => ColorOf(OpaqueBackdropOf(text))).ToList();
    }

    private static Brush OpaqueBackdropOf(DependencyObject element)
    {
        for (DependencyObject current = element; current != null; current = ParentOf(current))
        {
            Brush background = BackgroundOf(current);
            if (background is SolidColorBrush solid && solid.Color.A != 0)
            {
                return background;
            }
        }

        return Brushes.Transparent;
    }

    private static Brush BackgroundOf(DependencyObject element)
    {
        if (element is Control control)
        {
            return control.Background;
        }

        if (element is Panel panel)
        {
            return panel.Background;
        }

        return element is Border border ? border.Background : null;
    }

    private static DependencyObject ParentOf(DependencyObject element)
    {
        if (element == null)
        {
            return null;
        }

        DependencyObject parent = LogicalTreeHelper.GetParent(element);
        if (parent == null && element is Visual)
        {
            parent = VisualTreeHelper.GetParent(element);
        }

        return parent;
    }


    private static HashSet<Color> PaletteColors(StudioThemeColors palette)
    {
        return new HashSet<Color>(palette.Tokens.Values);
    }

    /// <summary>
    /// Токены текста меню: основной и приглушённый. Приглушённый — это норма: пункт с командой,
    /// которая сейчас недоступна (нет выделения/фокуса), WPF сам делает <c>IsEnabled = false</c>.
    /// </summary>
    private static HashSet<Color> TextTokens(StudioThemeColors palette)
    {
        return new HashSet<Color> { palette.Foreground, palette.ForegroundMuted };
    }

    /// <summary>Отношение контраста WCAG: 1 — неразличимо, 21 — чёрное на белом.</summary>
    private static double Contrast(Color first, Color second)
    {
        double a = RelativeLuminance(first);
        double b = RelativeLuminance(second);

        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static double Channel(byte value)
    {
        double channel = value / 255d;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static StudioThemeColors ExpectedPalette()
    {
        return StudioPalette.Active;
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>(), "ожидалась однотонная кисть");
        return ((SolidColorBrush)brush).Color;
    }

    /// <summary>Протягивает очередь диспетчера до отложенных проходов темизации.</summary>
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject root)
    {
        int count = root is Visual ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int index = 0; index < count; index++)
        {
            yield return VisualTreeHelper.GetChild(root, index);
        }
    }

    /// <summary>Только визуальное поддерево: у меню логические дети и шаблон пересекаются.</summary>
    private static IEnumerable<DependencyObject> VisualWalk(DependencyObject root)
    {
        foreach (DependencyObject child in VisualChildren(root))
        {
            yield return child;

            foreach (DependencyObject descendant in VisualWalk(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Логические дети (до применения шаблона) плюс визуальные (после).</summary>
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        HashSet<DependencyObject> seen = new HashSet<DependencyObject>();

        if (root != null)
        {
            seen.Add(root);
        }

        foreach (DependencyObject element in WalkCore(root, seen))
        {
            yield return element;
        }
    }

    private static IEnumerable<DependencyObject> WalkCore(DependencyObject root, HashSet<DependencyObject> seen)
    {
        if (root == null)
        {
            yield break;
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject logical && seen.Add(logical))
            {
                yield return logical;

                foreach (DependencyObject descendant in WalkCore(logical, seen))
                {
                    yield return descendant;
                }
            }
        }

        if (!(root is Visual) && !(root is System.Windows.Media.Media3D.Visual3D))
        {
            yield break;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject visual = VisualTreeHelper.GetChild(root, index);
            if (seen.Add(visual))
            {
                yield return visual;

                foreach (DependencyObject descendant in WalkCore(visual, seen))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static int CountOf(string source, string fragment)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
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
