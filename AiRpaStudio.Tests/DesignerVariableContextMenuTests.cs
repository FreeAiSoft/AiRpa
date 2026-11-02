using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Threading;

using NUnit.Framework;
using RPA.Core.Services;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Контекстное меню в таблицах переменных и аргументов дизайнера:
/// <see cref="DesignerVariableContextMenuService"/> и перекраска меню в <see cref="WfDesignerTheme"/>.
/// <para>
/// Принципы, инварианты и ручной чеклист — docs\Designer_Variable_ContextMenu_Principles.md.
/// Дефекты, которые держат эти тесты: запись стиля в <c>DataTemplate.Resources</c> роняла панель
/// («ResourceDictionary доступен только для чтения», 1.0.0.0); флаг «меню открывается» залипал и глушил
/// следующее завершение правки; меню создавалось с палитрой на момент первого показа и не следовало теме;
/// глобальные обработчики закрытия не снимались с окна; обработчики висели в шаблоне ячейки, поэтому
/// клик мимо поля взводил отмену.
/// </para>
/// <para>
/// HWND и настоящий <c>WorkflowDesigner</c> не нужны: панель переменных подменяется синтетическим
/// деревом <c>Grid → DataGrid#variableDataGrid</c>, как и в <c>WfDesignerThemeTests</c>.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class DesignerVariableContextMenuTests
{
    // --- Палитра поля ввода: локальные значения при входе в правку. ---

    [Test]
    public void ApplyCellPalette_UsesPalette()
    {
        var field = new TextBox();

        DesignerVariableContextMenuService.ApplyCellPalette(field, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(field.Background), Is.EqualTo(StudioPalette.Dark.Surface));
            Assert.That(ColorOf(field.Foreground), Is.EqualTo(StudioPalette.Dark.Foreground));
            Assert.That(ColorOf(field.BorderBrush), Is.EqualTo(StudioPalette.Dark.SeparatorStrong));
            Assert.That(ColorOf(field.CaretBrush), Is.EqualTo(StudioPalette.Dark.Foreground),
                "каретка не должна оставаться системной: на тёмной поверхности её не видно");
            Assert.That(field.FontFamily.Source, Does.Contain("Roboto"));
        });
    }

    [Test]
    public void ApplyCellPalette_FollowsLightPalette()
    {
        var field = new TextBox();

        DesignerVariableContextMenuService.ApplyCellPalette(field, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(field.Background), Is.EqualTo(StudioPalette.Light.Surface));
            Assert.That(ColorOf(field.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
        });
    }

    [Test]
    public void ApplyCellPalette_NullPalette_FallsBackToDark()
    {
        var field = new TextBox();

        Assert.DoesNotThrow(() => DesignerVariableContextMenuService.ApplyCellPalette(field, null));
        Assert.That(ColorOf(field.Background), Is.EqualTo(StudioPalette.Dark.Surface));
    }

    [Test]
    public void ApplyCellPalette_NullField_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => DesignerVariableContextMenuService.ApplyCellPalette(null, StudioPalette.Dark));
    }

    [Test]
    public void ApplyCellPalette_HasNoForeignColors()
    {
        var field = new TextBox();
        DesignerVariableContextMenuService.ApplyCellPalette(field, StudioPalette.Dark);
        HashSet<Color> paletteColors = new HashSet<Color>(StudioPalette.Dark.Tokens.Values);

        foreach (Brush brush in new[] { field.Background, field.Foreground, field.BorderBrush, field.CaretBrush })
        {
            Assert.That(brush, Is.InstanceOf<SolidColorBrush>());
            var solid = (SolidColorBrush)brush;

            Assert.That(paletteColors.Contains(solid.Color), Is.True,
                $"цвет {solid.Color} не из палитры — зашитые hex запрещены");
            Assert.That(solid.IsFrozen, Is.True, "кисти палитры должны быть заморожены");
        }
    }

    // --- Подключение к панели переменных/аргументов. ---

    [Test]
    public void OnVariableDesignerVisible_AttachesHandlersToGrid()
    {
        (Grid designer, DataGrid dataGrid) = CreateDesignerTree(3);
        var service = new TestableContextMenuService(designer);

        service.Attach(designer, DesignerVariableContextMenuService.VariableDataGridElementName);

        Assert.That(service.IsAttached(dataGrid), Is.True,
            "обработчики обязаны висеть на таблице: она выше ячейки в туннеле");
    }

    [Test]
    public void OnVariableDesignerVisible_AttachTwice_IsIdempotent()
    {
        // Двойное подключение дублировало CellEditEnding: второй обработчик снимал отмену первого.
        (Grid designer, DataGrid dataGrid) = CreateDesignerTree(2);
        var service = new TestableContextMenuService(designer);

        service.Attach(designer, DesignerVariableContextMenuService.VariableDataGridElementName);
        Assert.DoesNotThrow(() => service.Attach(designer, DesignerVariableContextMenuService.VariableDataGridElementName));

        Assert.That(service.IsAttached(dataGrid), Is.True);
    }

    [Test]
    public void OnVariableDesignerVisible_SealedTemplateResources_DoNotThrow()
    {
        // Дефект 1.0.0.0 (2026-11-02 03:21:35): стиль писался в DataTemplate.Resources, а шаблоны правки
        // WF-дизайнера запечатаны при первом применении — их словарь только для чтения, и запись в него
        // падала с InvalidOperationException «ResourceDictionary доступен только для чтения».
        DataTemplate sealedTemplate = CreateSealedCellTemplate();
        DataGrid dataGrid = CreateGridWith(sealedTemplate);
        var designer = new Grid { Name = DesignerVariableContextMenuService.VariableDesignerElementName };
        designer.Children.Add(dataGrid);
        var service = new TestableContextMenuService(designer);

        Assert.That(sealedTemplate.IsSealed, Is.True, "предпосылка: шаблон применён и запечатан");
        Assert.That(
            () =>
            {
                sealedTemplate.Resources ??= new ResourceDictionary();
                sealedTemplate.Resources[typeof(TextBox)] = new Style(typeof(TextBox));
            },
            Throws.InvalidOperationException,
            "предпосылка: запись в Resources запечатанного шаблона невозможна — именно этот путь падал в 1.0.0.0");

        Assert.DoesNotThrow(() => service.Attach(designer, DesignerVariableContextMenuService.VariableDataGridElementName),
            "инициализация панели поверх запечатанных шаблонов не имеет права падать");
        Assert.That(service.IsAttached(dataGrid), Is.True,
            "при этом обработчики обязаны подключиться: меню работает и без записи в шаблоны");
    }

    [Test]
    public void OnVariableDesignerVisible_DoesNotWriteTemplateResources()
    {
        (Grid designer, DataGrid dataGrid) = CreateDesignerTree(3);
        var service = new TestableContextMenuService(designer);

        service.Attach(designer, DesignerVariableContextMenuService.VariableDataGridElementName);

        foreach (DataGridColumn column in dataGrid.Columns)
        {
            ResourceDictionary resources = ((DataGridTemplateColumn)column).CellEditingTemplate.Resources;

            Assert.That(resources == null || resources.Count == 0, Is.True,
                "шаблоны правки могут быть запечатаны: сервис не пишет в их словари (см. дефект 1.0.0.0)");
        }
    }

    [Test]
    public void OnVariableDesignerVisible_WithoutColumns_DoesNotThrow()
    {
        var dataGrid = new DataGrid { Name = DesignerVariableContextMenuService.VariableDataGridElementName };
        var designer = new Grid { Name = DesignerVariableContextMenuService.VariableDesignerElementName };
        designer.Children.Add(dataGrid);
        var service = new TestableContextMenuService(designer);

        Assert.DoesNotThrow(() => service.Attach(designer, DesignerVariableContextMenuService.VariableDataGridElementName));
        Assert.That(service.IsAttached(dataGrid), Is.False, "таблица без столбцов ещё не собрана");
    }

    [Test]
    public void OnVariableDesignerVisible_TextColumnsOnly_Attaches()
    {
        // DataGridTextColumn — норма для столбцов «Имя»/«Тип»: поле правки создаёт сам DataGrid,
        // обработчики на таблице работают и без CellEditingTemplate.
        var dataGrid = new DataGrid { Name = DesignerVariableContextMenuService.ArgumentsDataGridElementName };
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "Имя" });
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "Тип" });
        var designer = new Grid { Name = DesignerVariableContextMenuService.ArgumentDesignerElementName };
        designer.Children.Add(dataGrid);
        var service = new TestableContextMenuService(designer);

        Assert.DoesNotThrow(() => service.Attach(designer, DesignerVariableContextMenuService.ArgumentsDataGridElementName));
        Assert.That(service.IsAttached(dataGrid), Is.True,
            "столбцы с автогенерацией поля ввода тоже обязаны получать меню");
    }

    [Test]
    public void OnVariableDesignerVisible_UnknownDataGridName_DoesNothing()
    {
        (Grid designer, DataGrid dataGrid) = CreateDesignerTree(2);
        var service = new TestableContextMenuService(designer);

        Assert.DoesNotThrow(() => service.Attach(designer, DesignerVariableContextMenuService.ArgumentsDataGridElementName));
        Assert.That(service.IsAttached(dataGrid), Is.False,
            "таблица с другим именем не должна подключаться");
    }

    [Test]
    public void OnVariableDesignerVisible_NullDesigner_DoesNotThrow()
    {
        var service = new TestableContextMenuService(null);

        Assert.DoesNotThrow(() => service.Attach(null, DesignerVariableContextMenuService.VariableDataGridElementName));
    }

    // --- Сквозной сценарий: правка ячейки → правый клик → меню открылось, правка не завершилась. ---

    [Test]
    public void RightClickInEditingCell_OpensMenuAndKeepsEdit()
    {
        (Window window, DataGrid dataGrid) = CreateLiveGrid();
        window.Show();

        try
        {
            window.UpdateLayout();
            var service = new TestableContextMenuService((UIElement)window.Content);
            service.Attach((UIElement)window.Content, DesignerVariableContextMenuService.VariableDataGridElementName);
            Assert.That(service.IsAttached(dataGrid), Is.True, "предпосылка: обработчики подключены к живой таблице");

            dataGrid.CurrentCell = new DataGridCellInfo(dataGrid.Items[0], dataGrid.Columns[0]);
            Assert.That(dataGrid.BeginEdit(), Is.True, "не удалось войти в режим правки ячейки");

            // палитра ставится отложенно (DispatcherPriority.Loaded): доводим очередь
            DrainDispatcher();
            TextBox field = FindDescendant<TextBox>(dataGrid);
            Assert.That(field, Is.Not.Null, "поле правки не найдено — сценарий невоспроизводим");
            Assert.That(ColorOf(field.Background), Is.EqualTo(ExpectedPalette().Surface),
                "палитра обязана примениться уже при входе в правку (PreparingCellForEdit)");

            // Правый клик: туннель WPF доставляет событие от корня к полю, обработчик сервиса живёт на
            // таблице и видит OriginalSource = поле. Здесь моделируется именно эта пара (источник + таблица).
            dataGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            {
                RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent,
                Source = field
            });

            DrainDispatcher();

            Assert.Multiple(() =>
            {
                Assert.That(service.IsMenuOpen, Is.True, "правый клик в поле правки обязан открыть меню");
                Assert.That(field.ContextMenu, Is.Not.Null, "меню создаётся на поле ввода");
                Assert.That(field.ContextMenu.PlacementTarget, Is.SameAs(field),
                    "меню открывается у поля, а не в углу экрана");
                Assert.That(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true), Is.False,
                    "правка не должна завершаться: иначе ячейка и меню закрываются");
            });

            field.ContextMenu.IsOpen = false;
            DrainDispatcher();
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void RightClickOutsideEditingField_DoesNotOpenMenuNorCancelEdit()
    {
        (Window window, DataGrid dataGrid) = CreateLiveGrid();
        window.Show();

        try
        {
            window.UpdateLayout();
            var service = new TestableContextMenuService((UIElement)window.Content);
            service.Attach((UIElement)window.Content, DesignerVariableContextMenuService.VariableDataGridElementName);

            dataGrid.CurrentCell = new DataGridCellInfo(dataGrid.Items[0], dataGrid.Columns[0]);
            Assert.That(dataGrid.BeginEdit(), Is.True);
            DrainDispatcher();

            // правый клик мимо поля ввода (пустая область таблицы): OriginalSource — не TextBox
            dataGrid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            {
                RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent,
                Source = dataGrid
            });

            DrainDispatcher();

            Assert.Multiple(() =>
            {
                Assert.That(service.IsMenuOpen, Is.False, "клик мимо поля правки меню не открывает");
                Assert.That(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true), Is.True,
                    "флаг отмены не взведён: завершение правки проходит обычным путём");
            });
        }
        finally
        {
            window.Close();
        }
    }




    // --- Service: подключение к дизайнеру и состояние. ---

    [Test]
    public void InitializeVariableDesignerTracking_NullView_DoesNotThrow()
    {
        var service = new TestableContextMenuService(null);

        Assert.DoesNotThrow(() => service.InitializeVariableDesignerTracking());
    }

    [Test]
    public void Constructor_ForDesigner_IsSafeWithNull()
    {
        Assert.DoesNotThrow(() => new DesignerVariableContextMenuService((UIElement)null));
    }

    [Test]
    public void IsMenuOpen_IsFalseInitially()
    {
        Assert.That(CreateService().IsMenuOpen, Is.False);
    }

    // --- Перекраска меню: тема меняется, меню создаётся один раз. ---

    [Test]
    public void RecolorEditingContextMenu_SwitchesPalette()
    {
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);

        WfDesignerTheme.RecolorEditingContextMenu(menu, StudioPalette.Light);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(menu.Background), Is.EqualTo(StudioPalette.Light.Surface),
                "меню, созданное в тёмной теме, обязано перекрашиваться при смене темы");
            Assert.That(ColorOf(menu.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(ColorOf(menu.BorderBrush), Is.EqualTo(StudioPalette.Light.SeparatorStrong));
            Assert.That(menu.HasDropShadow, Is.False, "тень даёт светлый прямоугольник поверх тёмной темы");
            Assert.That(menu.StaysOpen, Is.True, "закрытием управляет сервис, а не WF");
        });

        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            Assert.That(ColorOf(item.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(ColorOf(item.Background), Is.EqualTo(StudioPalette.Light.Surface));
        }
    }

    [Test]
    public void RecolorEditingContextMenu_KeepsStructureAndCommands()
    {
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);

        WfDesignerTheme.RecolorEditingContextMenu(menu, StudioPalette.Light);
        WfDesignerTheme.RecolorEditingContextMenu(menu, StudioPalette.Dark);

        Assert.That(menu.Items.OfType<MenuItem>().Select(item => item.Header).ToArray(),
            Is.EqualTo(new object[] { "Вырезать", "Копировать", "Вставить" }), "перекраска не пересоздаёт пункты");
        Assert.That(menu.Items.OfType<MenuItem>().Select(item => item.Command).ToArray(),
            Is.EqualTo(new[] { ApplicationCommands.Cut, ApplicationCommands.Copy, ApplicationCommands.Paste }));
    }

    [Test]
    public void RecolorEditingContextMenu_NullArguments_DoNotThrow()
    {
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);

        Assert.DoesNotThrow(() => WfDesignerTheme.RecolorEditingContextMenu(null, StudioPalette.Light));
        Assert.DoesNotThrow(() => WfDesignerTheme.RecolorEditingContextMenu(menu, null));
        Assert.That(ColorOf(menu.Background), Is.EqualTo(StudioPalette.Dark.Surface), "null-палитра — тёмная по умолчанию");
    }

    // --- Стражи по исходникам: точки подключения и запреты. ---

    [Test]
    public void Source_InitializesTrackingOnceAfterVariablesStyle()
    {
        string helper = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "LoadActivityToDesignerHelper.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(CountOf(helper, "InitializeVariableDesignerTracking()"), Is.EqualTo(1),
                "отслеживание подключается ровно один раз");
            Assert.That(helper.IndexOf("InitializeVariableDesignerTracking()", StringComparison.Ordinal),
                Is.GreaterThan(helper.IndexOf("SetVariablesStyle(workflowDesignerEx)", StringComparison.Ordinal)),
                "подключение идёт после стилизации переменных: иначе обработчики ставятся в чужую палитру");
        });
    }

    [Test]
    public void Source_HandlersLiveOnGrid()
    {
        string service = ServiceSource();

        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("dataGrid.PreviewMouseRightButtonDown +="),
                "взвод отмены и открытие меню живут на таблице: она выше ячейки в туннеле");
            Assert.That(service, Does.Contain("dataGrid.CellEditEnding +="));
            Assert.That(service, Does.Contain("dataGrid.PreparingCellForEdit +="),
                "палитра поля ввода ставится при входе в правку");
            Assert.That(service, Does.Contain("DesignerVariableContextMenuPolicy.FindEditingField"),
                "поле правки определяется политикой — без дублирующихся проверок");
            Assert.That(service, Does.Contain("_state.NotifyCellEditEnding()"),
                "отмена обязана идти через автомат (consume-once)");
            Assert.That(service, Does.Contain("e.Handled = true;"),
                "правый клик помечается обработанным: иначе ячейка завершает правку");
        });
    }

    [Test]
    public void Source_NoWritesIntoCellTemplates()
    {
        string service = ServiceSource();

        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Not.Contain("CellEditingTemplate"),
                "шаблоны правки WF запечатаны: обращаться к ним из сервиса нельзя (дефект 1.0.0.0)");
            Assert.That(service, Does.Not.Contain("GetCellEditingTemplates"));
            Assert.That(Regex.IsMatch(service, @"\.Resources\s*\[", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "запись в словари ресурсов запрещена: у запечатанного шаблона они только для чтения");
            Assert.That(service, Does.Not.Contain("??= new ResourceDictionary()"));
        });
    }

    [Test]
    public void Source_RemovesGlobalHandlersOnClosed()
    {
        string service = ServiceSource();
        int closed = service.IndexOf("contextMenu.Closed +=", StringComparison.Ordinal);
        int opened = service.IndexOf("contextMenu.Opened +=", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.GreaterThanOrEqualTo(0), "обработчик Opened не найден");
            Assert.That(closed, Is.GreaterThan(opened), "обработчик Closed не найден после Opened");

            Assert.That(CountOf(service, "Mouse.AddPreviewMouseDownHandler"), Is.EqualTo(1));
            Assert.That(CountOf(service, "Keyboard.AddPreviewKeyDownHandler"), Is.EqualTo(1),
                "глобальные обработчики подключаются один раз — на время показа меню");
            Assert.That(service.IndexOf("Mouse.AddPreviewMouseDownHandler", StringComparison.Ordinal), Is.GreaterThan(opened),
                "подключение должно быть в Opened, а не при создании меню");

            Assert.That(CountOf(service, "Mouse.RemovePreviewMouseDownHandler"), Is.EqualTo(1));
            Assert.That(CountOf(service, "Keyboard.RemovePreviewKeyDownHandler"), Is.EqualTo(1),
                "снятие обработчиков ровно одно: иначе утечка на каждый показ меню");
            Assert.That(service.IndexOf("Mouse.RemovePreviewMouseDownHandler", StringComparison.Ordinal), Is.GreaterThan(closed),
                "снятие обязано жить в Closed: закрытие пунктом/кликом вне/Escape иначе оставляло хуки на окне");
            Assert.That(service.IndexOf("Keyboard.RemovePreviewKeyDownHandler", StringComparison.Ordinal), Is.GreaterThan(closed));
        });
    }

    [Test]
    public void Source_InteractiveRootIsNullSafe()
    {
        string service = ServiceSource();

        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("TryGetInteractiveRoot"));
            Assert.That(service, Does.Not.Contain("Application.Current.MainWindow"),
                "прямое обращение к MainWindow падало с ArgumentNullException, когда окна нет");
            Assert.That(service, Does.Contain("Application.Current?.MainWindow"));
        });
    }

    [Test]
    public void Source_UsesSinglePaletteMenuFactory()
    {
        string service = ServiceSource();

        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("WfDesignerTheme.CreateEditingContextMenu"),
                "меню строится только фабрикой на палитре");
            Assert.That(service, Does.Contain("WfDesignerTheme.RecolorEditingContextMenu"),
                "цвета применяются на каждое открытие — после смены темы меню не должно оставаться старым");
            Assert.That(service.LastIndexOf("WfDesignerTheme.RecolorEditingContextMenu", StringComparison.Ordinal),
                Is.GreaterThan(service.IndexOf("contextMenu.Opened +=", StringComparison.Ordinal)),
                "перекраска вызывается в Opened: иначе меню остаётся в палитре, в которой создано");
            Assert.That(service, Does.Not.Contain("new ContextMenu"), "меню нельзя собирать вручную мимо фабрики");
            Assert.That(service, Does.Not.Contain("MessageBox"));
            Assert.That(Regex.IsMatch(service, @"\bDebug\.WriteLine", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "отладочные WriteLine не должны оставаться в сервисе");
            Assert.That(Regex.IsMatch(service, @"#[0-9A-Fa-f]{6}", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "цвета берутся только из StudioPalette");
            Assert.That(Regex.IsMatch(service, @"\bColors\.", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "зашитые системные цвета запрещены");
        });
    }

    [Test]
    public void Source_UsesElementNameConstants()
    {
        string service = ServiceSource();

        Assert.Multiple(() =>
        {
            Assert.That(CountOf(service, "\"VariableDesigner\""), Is.EqualTo(1));
            Assert.That(CountOf(service, "\"ArgumentDesigner\""), Is.EqualTo(1));
            Assert.That(CountOf(service, "\"variableDataGrid\""), Is.EqualTo(1));
            Assert.That(CountOf(service, "\"argumentsDataGrid\""), Is.EqualTo(1),
                "имена элементов WF объявляются один раз константой, без дублирующихся литералов");
        });
    }

    [Test]
    public void Source_ThemeRecolorIsUsedByFactory()
    {
        string theme = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(theme, Does.Contain("internal static void RecolorEditingContextMenu"));
            Assert.That(theme, Does.Contain("RecolorEditingContextMenu(menu, palette);"),
                "фабрика обязана красить пункты через Recolor: одна точка правды для палитры");
        });
    }

    [Test]
    public void ProjectFile_CompilesSourcesAndShipsDocument()
    {
        string project = ReadSource(Path.Combine("AiRpaStudio", "AiRpaStudio.csproj"));

        Assert.Multiple(() =>
        {
            Assert.That(project, Does.Contain("Core\\Services\\DesignerVariableContextMenuPolicy.cs"));
            Assert.That(project, Does.Contain("Core\\Services\\DesignerContextMenuState.cs"));
            Assert.That(project, Does.Contain("Core\\Services\\DesignerVariableContextMenuService.cs"));
            Assert.That(project, Does.Contain("Docs\\Designer_Variable_ContextMenu_Principles.md"));
        });
    }

    [Test]
    public void Document_RecordsPrinciplesInvariantsAndGuards()
    {
        string document = ReadSource(Path.Combine("AiRpaStudio", "docs", "Designer_Variable_ContextMenu_Principles.md"));

        Assert.Multiple(() =>
        {
            Assert.That(document, Does.Contain("## Инварианты"), "раздел инвариантов обязателен");
            Assert.That(document, Does.Contain("## Стражи"), "раздел стражей обязателен");
            Assert.That(document, Does.Contain("## Тесты"), "карта тестов обязательна");
            Assert.That(document, Does.Contain("PreviewMouseRightButtonDown"));
            Assert.That(document, Does.Contain("CellEditEnding"));
            Assert.That(document, Does.Contain("DesignerVariableContextMenuPolicy"));
            Assert.That(document, Does.Contain("DesignerContextMenuState"));
            Assert.That(document, Does.Contain("RecolorEditingContextMenu"));
            Assert.That(document, Does.Contain("FindEditingField"));
            Assert.That(document, Does.Contain("1.0.0.0"),
                "дефект с записью в запечатанный шаблон обязан быть зафиксирован в документе");
            Assert.That(document, Does.Contain("только для чтения"),
                "симптом из журнала должен быть в документе: иначе его снова будут «лечить» наугад");
        });
    }

    [Test]
    public void Agents_ReferencesDocument()
    {
        string agents = ReadSource("AGENTS.md");

        Assert.That(agents, Does.Contain("Designer_Variable_ContextMenu_Principles.md"),
            "принципы механизма обязаны быть в карте проекта");
        Assert.That(agents, Does.Contain("DesignerVariableContextMenuTests"));
    }

    // --- Все таблицы дизайнера: переменные, аргументы и пространства имён. ---

    [Test]
    public void RefreshTables_AttachesVariablesArgumentsAndNamespaceTables()
    {
        // Панель «Пространства имён» (ImportDesigner.xaml) раньше не обслуживалась вовсе: правый клик в её
        // ячейке завершал правку и закрывал панель, потому что сервис искал только две известные панели.
        (UIElement designer, DataGrid variables, DataGrid arguments, DataGrid namespaces) = CreateAllTablesTree();
        var service = new TestableContextMenuService(designer);

        int attached = service.RefreshTables(designer);

        Assert.Multiple(() =>
        {
            Assert.That(attached, Is.EqualTo(3), "обслуживаются все три таблицы дизайнера");
            Assert.That(service.IsAttached(variables), Is.True);
            Assert.That(service.IsAttached(arguments), Is.True);
            Assert.That(service.IsAttached(namespaces), Is.True, "таблица пространств имён больше не пропускается");
        });
    }

    [Test]
    public void RefreshTables_IsIdempotent()
    {
        (UIElement designer, DataGrid variables, _, _) = CreateAllTablesTree();
        var service = new TestableContextMenuService(designer);

        _ = service.RefreshTables(designer);
        int second = service.RefreshTables(designer);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(3), "повторный проход не должен подключать таблицы заново");
            Assert.That(service.IsAttached(variables), Is.True);
        });
    }

    [Test]
    public void RefreshTables_WithoutColumns_DoesNotThrowAndSkips()
    {
        var dataGrid = new DataGrid { Name = DesignerVariableContextMenuService.ImportedNamespacesDataGridElementName };
        var designer = new Grid();
        designer.Children.Add(dataGrid);
        var service = new TestableContextMenuService(designer);

        Assert.DoesNotThrow(() => service.RefreshTables(designer));
        Assert.That(service.IsAttached(dataGrid), Is.False, "таблица без столбцов ещё не собрана");
    }

    [Test]
    public void RefreshTables_NullRoot_DoesNotThrow()
    {
        var service = new TestableContextMenuService(new Grid());

        Assert.DoesNotThrow(() => service.RefreshTables(null));
    }

    [Test]
    public void Source_AttachesEveryDesignerTable()
    {
        string service = ServiceSource();

        Assert.Multiple(() =>
        {
            Assert.That(service, Does.Contain("RefreshTables"),
                "таблицы подключаются общим проходом, а не двумя зашитыми именами панелей");
            Assert.That(service, Does.Contain("WfDataGridTheme.FindTables"),
                "поиск таблиц идёт по типу: имена элементов объявляет шаблон WF");
            Assert.That(service, Does.Contain("Attach("), "подключение обработчиков живёт в одном месте");
        });
    }


    // --- Вспомогательное. ---

    private static TestableContextMenuService CreateService()
    {
        return new TestableContextMenuService(new Grid());
    }

    private static (Grid Designer, DataGrid DataGrid) CreateDesignerTree(int templateColumns)
    {
        var dataGrid = new DataGrid { Name = DesignerVariableContextMenuService.VariableDataGridElementName };
        for (int index = 0; index < templateColumns; index++)
        {
            dataGrid.Columns.Add(new DataGridTemplateColumn { CellEditingTemplate = CreateCellTemplate() });
        }

        var designer = new Grid { Name = DesignerVariableContextMenuService.VariableDesignerElementName };
        designer.Children.Add(dataGrid);
        return (designer, dataGrid);
    }

    /// <summary>
    /// Дерево со всеми тремя таблицами дизайнера: панели переменных и аргументов называются как у WF
    /// (<c>VariableDesigner</c>/<c>ArgumentDesigner</c>), а панель пространств имён — по имени элемента
    /// шаблона (<c>imports1</c>): именно её сервис раньше не находил.
    /// </summary>
    private static (UIElement Designer, DataGrid Variables, DataGrid Arguments, DataGrid Namespaces) CreateAllTablesTree()
    {
        var designer = new Grid();

        var variables = new DataGrid { Name = DesignerVariableContextMenuService.VariableDataGridElementName };
        variables.Columns.Add(new DataGridTextColumn { Header = "Имя" });

        var arguments = new DataGrid { Name = DesignerVariableContextMenuService.ArgumentsDataGridElementName };
        arguments.Columns.Add(new DataGridTextColumn { Header = "Имя" });

        var namespaces = new DataGrid { Name = DesignerVariableContextMenuService.ImportedNamespacesDataGridElementName };
        namespaces.Columns.Add(new DataGridTemplateColumn { CellTemplate = CreateCellTemplate(), IsReadOnly = true });

        var variablePanel = new Grid { Name = DesignerVariableContextMenuService.VariableDesignerElementName };
        variablePanel.Children.Add(variables);

        var argumentPanel = new Grid { Name = DesignerVariableContextMenuService.ArgumentDesignerElementName };
        argumentPanel.Children.Add(arguments);

        var namespacePanel = new Grid { Name = "imports1" };
        namespacePanel.Children.Add(namespaces);

        designer.Children.Add(variablePanel);
        designer.Children.Add(argumentPanel);
        designer.Children.Add(namespacePanel);

        return (designer, variables, arguments, namespaces);
    }


    /// <summary>Живая таблица в окне: нужна для сквозных сценариев с правкой ячейки.</summary>
    private static (Window Window, DataGrid DataGrid) CreateLiveGrid()
    {
        var dataGrid = new DataGrid
        {
            Name = DesignerVariableContextMenuService.VariableDataGridElementName,
            AutoGenerateColumns = false,
            ItemsSource = new[] { "cell" }
        };
        dataGrid.Columns.Add(new DataGridTemplateColumn
        {
            CellTemplate = CreateCellTemplate(),
            CellEditingTemplate = CreateCellTemplate()
        });

        var designer = new Grid { Name = DesignerVariableContextMenuService.VariableDesignerElementName };
        designer.Children.Add(dataGrid);

        return (new Window { Width = 420, Height = 220, ShowInTaskbar = false, Content = designer }, dataGrid);
    }

    private static DataTemplate CreateCellTemplate()
    {
        return new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(TextBox)) };
    }

    /// <summary>
    /// Шаблон правки, уже применённый к элементу: WPF запечатывает <see cref="DataTemplate"/> при первом
    /// применении, и его <see cref="FrameworkTemplate.Resources"/> становится только для чтения —
    /// ровно то состояние, в котором панель переменных падала в 1.0.0.0.
    /// </summary>
    private static DataTemplate CreateSealedCellTemplate()
    {
        DataTemplate template = CreateCellTemplate();
        var window = new Window
        {
            Width = 120,
            Height = 60,
            ShowInTaskbar = false,
            Content = new ContentPresenter { Content = "cell", ContentTemplate = template }
        };

        try
        {
            window.Show();
            window.UpdateLayout();
        }
        finally
        {
            window.Close();
        }

        return template;
    }

    private static DataGrid CreateGridWith(DataTemplate cellEditingTemplate)
    {
        var dataGrid = new DataGrid { Name = DesignerVariableContextMenuService.VariableDataGridElementName };
        dataGrid.Columns.Add(new DataGridTemplateColumn { CellEditingTemplate = cellEditingTemplate });
        return dataGrid;
    }

    private static T FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root == null)
        {
            return null;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            T nested = FindDescendant<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void DrainDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static StudioThemeColors ExpectedPalette()
    {
        return StudioPalette.Detect(Application.Current?.Resources);
    }

    private static Color ColorOf(object value)
    {
        Assert.That(value, Is.InstanceOf<Brush>(), "ожидалась кисть");
        Assert.That(value, Is.InstanceOf<SolidColorBrush>());
        return ((SolidColorBrush)value).Color;
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

    private static string ServiceSource()
    {
        return ReadSource(Path.Combine("AiRpaStudio", "Core", "Services", "DesignerVariableContextMenuService.cs"));
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

    /// <summary>Сервис для тестов: открывает <c>protected</c> точку подключения панели.</summary>
    private sealed class TestableContextMenuService : DesignerVariableContextMenuService
    {
        public TestableContextMenuService(UIElement view)
            : base(view)
        {
        }

        public void Attach(UIElement designer, string nameDataGrid)
        {
            OnVariableDesignerVisible(designer, nameDataGrid);
        }
    }

    // --- Проверка реальным вводом мыши (Explicit: управляет курсором, нужен интерактивный рабочий стол). ---

    [Test]
    [Explicit("Управляет курсором мыши: запускать вручную при проверке живой панели переменных.")]
    public void RealRightClickInEditingCell_ReachesGridHandler()
    {
        (Window window, DataGrid dataGrid) = CreateLiveGrid();
        window.Topmost = true;
        POINT savedCursor = default;
        window.Show();
        window.Activate();

        try
        {
            window.UpdateLayout();
            var service = new TestableContextMenuService((UIElement)window.Content);
            service.Attach((UIElement)window.Content, DesignerVariableContextMenuService.VariableDataGridElementName);

            dataGrid.CurrentCell = new DataGridCellInfo(dataGrid.Items[0], dataGrid.Columns[0]);
            Assert.That(dataGrid.BeginEdit(), Is.True);
            DrainDispatcher();

            TextBox field = FindDescendant<TextBox>(dataGrid);
            Assert.That(field, Is.Not.Null);

            GetCursorPos(out savedCursor);
            Point center = field.PointToScreen(new Point(field.ActualWidth / 2, field.ActualHeight / 2));
            Assert.That(SetCursorPos((int)center.X, (int)center.Y), Is.True);
            mouse_event(MouseEventRightDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventRightUp, 0, 0, 0, UIntPtr.Zero);

            PumpEvents();

            Assert.Multiple(() =>
            {
                Assert.That(service.IsMenuOpen, Is.True,
                    "реальный правый клик в поле правки обязан дойти до обработчика таблицы и открыть меню");
                Assert.That(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true), Is.False,
                    "правка не должна завершаться реальным правым кликом");
            });

            if (field.ContextMenu != null)
            {
                field.ContextMenu.IsOpen = false;
            }
        }
        finally
        {
            window.Close();
            SetCursorPos(savedCursor.X, savedCursor.Y);
        }
    }

    private const uint MouseEventRightDown = 0x0008;

    private const uint MouseEventRightUp = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private static void PumpEvents()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (s, e) =>
        {
            ((DispatcherTimer)s).Stop();
            frame.Continue = false;
        }, Dispatcher.CurrentDispatcher);

        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
    }
}
