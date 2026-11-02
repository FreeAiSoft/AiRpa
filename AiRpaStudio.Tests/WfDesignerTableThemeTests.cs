using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using NUnit.Framework;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Панели «Переменные», «Аргументы», «Пространства имён» дизайнера WF: вкладки shell bar, таблицы
/// (<see cref="DataGrid"/>) и их контекстное меню — F-04 плана docs\Vs2026_Theme_Plan.md.
/// <para>
/// Регресс, который держат эти тесты: к таблицам тема не применялась вообще. WF объявляет их стилем
/// <c>defaultDataGridStyle</c> с <c>{StaticResource}</c> на дефолтные цвета WF
/// (<c>GridViewRowUnderlineColor</c>, <c>GridViewRowSelectedColor</c>, <c>GridViewRowHoverColor</c>),
/// а <c>StaticResource</c> не пересчитывается, поэтому в тёмной теме шапка, сетка и выделение оставались
/// светлыми; вкладки shell bar рисовал шаблон системной темы Aero2 (его триггеры сильнее стиля WF).
/// </para>
/// <para>
/// Проверки идут на синтетическом дереве, повторяющем структуру <c>DesignerView</c>
/// (<c>xnDesignerView</c> → <c>bottomPanel</c> → панели → таблицы), и на живой таблице в окне: только там
/// материализуются контейнеры строк, шапки и ячеек. Требования и карта тестов —
/// docs\Vs2026_Theme_Plan.md (F-04), docs\Designer_Variable_ContextMenu_Principles.md.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class WfDesignerTableThemeTests
{
    // --- Таблицы: кисти, шрифт, разделители. ---

    [Test]
    public void Apply_ThemesAllDesignerTables()
    {
        DesignerTree tree = BuildDesignerTree();
        StudioThemeColors palette = StudioPalette.Dark;

        WfDesignerTheme.Apply(tree.Root, palette);

        foreach (DataGrid grid in new[] { tree.Variables, tree.Arguments, tree.Namespaces })
        {
            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(grid.Background), Is.EqualTo(palette.Surface), $"{grid.Name}: фон таблицы");
                Assert.That(ColorOf(grid.RowBackground), Is.EqualTo(palette.Surface), $"{grid.Name}: фон строки");
                Assert.That(ColorOf(grid.AlternatingRowBackground), Is.EqualTo(palette.Surface),
                    $"{grid.Name}: чередование строк выключено — один фон");
                Assert.That(ColorOf(grid.HorizontalGridLinesBrush), Is.EqualTo(palette.Separator), $"{grid.Name}: линии по горизонтали");
                Assert.That(ColorOf(grid.VerticalGridLinesBrush), Is.EqualTo(palette.Separator), $"{grid.Name}: линии по вертикали");
                Assert.That(ColorOf(grid.BorderBrush), Is.EqualTo(palette.Separator), $"{grid.Name}: hairline таблицы");
                Assert.That(grid.BorderThickness.Top, Is.EqualTo(1d), $"{grid.Name}: hairline только сверху");
                Assert.That(ColorOf(grid.Foreground), Is.EqualTo(palette.Foreground), $"{grid.Name}: текст");
                Assert.That(grid.FontFamily, Is.EqualTo(StudioFonts.UiFont), $"{grid.Name}: шрифт Roboto");
            });
        }
    }

    [Test]
    public void Apply_TableHeaderUsesSurfaceAltAndHairline()
    {
        DesignerTree tree = BuildDesignerTree();
        StudioThemeColors palette = StudioPalette.Dark;

        WfDesignerTheme.Apply(tree.Root, palette);

        Style header = tree.Variables.ColumnHeaderStyle;
        Assert.Multiple(() =>
        {
            Assert.That(WfThemeStyles.IsThemed(header), Is.True, "шапка обязана получить стиль палитры");
            Assert.That(SetterColourOf(header, Control.BackgroundProperty), Is.EqualTo(palette.SurfaceAlt));
            Assert.That(SetterColourOf(header, Control.ForegroundProperty), Is.EqualTo(palette.Foreground));
            Assert.That(SetterColourOf(header, Control.BorderBrushProperty), Is.EqualTo(palette.Separator));
            Assert.That(SetterColourOf(header, DataGridColumnHeader.SeparatorBrushProperty), Is.EqualTo(palette.Separator),
                "разделитель шапки WF берёт из GridViewRowUnderlineColor (StaticResource) — его надо перекрыть");
            Assert.That(TriggerColourOf(header, UIElement.IsMouseOverProperty, Control.BackgroundProperty),
                Is.EqualTo(palette.Hover), "наведение на шапку — Hover");
        });
    }

    [Test]
    public void Apply_TableRowStatesUsePalette()
    {
        DesignerTree tree = BuildDesignerTree();
        StudioThemeColors palette = StudioPalette.Dark;

        WfDesignerTheme.Apply(tree.Root, palette);

        Style row = tree.Variables.RowStyle;
        Assert.Multiple(() =>
        {
            Assert.That(WfThemeStyles.IsThemed(row), Is.True);
            Assert.That(SetterColourOf(row, Control.BackgroundProperty), Is.EqualTo(palette.Surface));
            Assert.That(TriggerColourOf(row, UIElement.IsMouseOverProperty, Control.BackgroundProperty),
                Is.EqualTo(palette.Hover), "наведение на строку — Hover");
            Assert.That(TriggerColourOf(row, DataGridRow.IsSelectedProperty, Control.BackgroundProperty),
                Is.EqualTo(palette.Selected), "выделение строки — Selected, а не системное синее");
        });
    }

    [Test]
    public void Apply_TableCellFocusUsesSeparatorStrongHairline()
    {
        DesignerTree tree = BuildDesignerTree();
        StudioThemeColors palette = StudioPalette.Dark;

        WfDesignerTheme.Apply(tree.Root, palette);

        Style cell = tree.Variables.CellStyle;
        Assert.Multiple(() =>
        {
            Assert.That(WfThemeStyles.IsThemed(cell), Is.True);
            Assert.That(SetterColourOf(cell, Control.BackgroundProperty), Is.EqualTo(Colors.Transparent),
                "фон ячейки отдаётся строке: иначе выделение ячейки перекрывает выделение строки");
            Assert.That(TriggerColourOf(cell, UIElement.IsKeyboardFocusWithinProperty, Control.BorderBrushProperty),
                Is.EqualTo(palette.SeparatorStrong), "фокус ячейки — hairline SeparatorStrong");
        });
    }

    [Test]
    public void Apply_KeepsWorkflowTableStylesAsBasedOn()
    {
        DesignerTree tree = BuildDesignerTree();
        Style header = tree.Variables.ColumnHeaderStyle;
        Style cell = tree.Variables.CellStyle;
        Style row = tree.Variables.RowStyle;

        WfDesignerTheme.Apply(tree.Root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Variables.ColumnHeaderStyle.BasedOn, Is.SameAs(header),
                "шаблон и поведение шапки WF живут в его стиле: он обязан остаться в цепочке BasedOn");
            Assert.That(tree.Variables.CellStyle.BasedOn, Is.SameAs(cell));
            Assert.That(tree.Variables.RowStyle.BasedOn, Is.SameAs(row));
        });
    }

    [Test]
    public void Apply_TableStylesFollowThemeSwitch()
    {
        DesignerTree tree = BuildDesignerTree();
        Style headerBase = tree.Variables.ColumnHeaderStyle;

        WfDesignerTheme.Apply(tree.Root, StudioPalette.Dark);
        WfDesignerTheme.Apply(tree.Root, StudioPalette.Light);

        Style header = tree.Variables.ColumnHeaderStyle;

        Assert.Multiple(() =>
        {
            Assert.That(WfThemeStyles.PaletteOf(header), Is.SameAs(StudioPalette.Light),
                "стиль обязан помнить палитру: иначе после смены темы останутся кисти прежней");
            Assert.That(SetterColourOf(header, Control.BackgroundProperty), Is.EqualTo(StudioPalette.Light.SurfaceAlt));
            Assert.That(header.BasedOn, Is.SameAs(headerBase), "обёртка пересоздаётся от исходного стиля WF, а не от прежней темы");
            Assert.That(ColorOf(tree.Variables.Background), Is.EqualTo(StudioPalette.Light.Surface));
        });
    }

    [Test]
    public void Apply_TableThemeIsIdempotent()
    {
        DesignerTree tree = BuildDesignerTree();

        WfDesignerTheme.Apply(tree.Root, StudioPalette.Dark);
        Style header = tree.Variables.ColumnHeaderStyle;

        WfDesignerTheme.Apply(tree.Root, StudioPalette.Dark);

        Assert.Multiple(() =>
        {
            Assert.That(tree.Variables.ColumnHeaderStyle, Is.SameAs(header),
                "повторное применение той же палитры не должно наращивать цепочку стилей");
            Assert.That(WfDataGridTheme.HookCount(tree.Variables), Is.EqualTo(1), "подписка на таблицу ставится один раз");
        });
    }

    [Test]
    public void Apply_TablePanelBackdropBehindWhiteHostIsSurface()
    {
        DesignerTree tree = BuildDesignerTree();
        StudioThemeColors palette = StudioPalette.Dark;

        // Хост шаблона WF белый: вокруг таблицы просвечивал белый прямоугольник.
        tree.BottomPanel.Background = Brushes.White;
        tree.VariablesPanel.Background = null;

        WfDesignerTheme.Apply(tree.Root, palette);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(tree.VariablesPanel.Background), Is.EqualTo(palette.Surface),
                "панель переменных — на поверхности палитры");
            Assert.That(ColorOf(tree.BottomPanel.Background), Is.EqualTo(palette.Surface),
                "предок панели не должен оставаться белым");
        });
    }

    [Test]
    public void Apply_NullArguments_DoNotThrow()
    {
        Assert.DoesNotThrow(() =>
        {
            WfDataGridTheme.Apply(null, StudioPalette.Dark);
            WfDataGridTheme.Apply(new Grid(), null);
            WfDataGridTheme.Theme(null, StudioPalette.Dark);
        });
    }

    // --- Живые контейнеры: строки, шапки, отсутствие светлых плашек. ---

    [Test]
    public void Apply_LateCreatedRowsAreThemed()
    {
        (Window window, Grid root, DataGrid grid, ObservableCollection<string> items) = CreateLiveTable();
        StudioThemeColors palette = StudioPalette.Dark;

        try
        {
            window.Show();
            WfDesignerTheme.Apply(root, palette);

            // Строки создаются после применения темы — проход обязан их догнать.
            items.Add("first");
            items.Add("second");
            Pump(window);

            List<DataGridRow> rows = Walk(grid).OfType<DataGridRow>().ToList();

            Assert.That(rows, Is.Not.Empty, "предпосылка: строки материализованы");
            foreach (DataGridRow row in rows)
            {
                Assert.That(ColorOf(row.Background), Is.EqualTo(palette.Surface),
                    "поздно созданная строка обязана быть на поверхности палитры, а не на системной плашке");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_LiveTableHasNoLightPlates()
    {
        (Window window, Grid root, DataGrid grid, ObservableCollection<string> items) = CreateLiveTable();
        StudioThemeColors palette = StudioPalette.Dark;

        try
        {
            items.Add("first");
            items.Add("second");
            window.Show();
            window.UpdateLayout();

            WfDesignerTheme.Apply(root, palette);
            Pump(window);

            List<string> light = LightBrushesIn(grid, palette);
            Assert.That(light, Is.Empty,
                "в тёмной теме внутри таблицы не должно остаться светлых плашек Aero2: " + string.Join(", ", light));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_LiveTableHeaderAndCellsUsePalette()
    {
        (Window window, Grid root, DataGrid grid, ObservableCollection<string> items) = CreateLiveTable();
        StudioThemeColors palette = StudioPalette.Dark;

        try
        {
            items.Add("first");
            window.Show();
            WfDesignerTheme.Apply(root, palette);
            Pump(window);

            DataGridColumnHeader header = Walk(grid).OfType<DataGridColumnHeader>().FirstOrDefault();
            DataGridCell cell = Walk(grid).OfType<DataGridCell>().FirstOrDefault();

            Assert.Multiple(() =>
            {
                Assert.That(header, Is.Not.Null, "предпосылка: шапка материализована");
                Assert.That(ColorOf(header.Background), Is.EqualTo(palette.SurfaceAlt), "шапка — SurfaceAlt");
                Assert.That(cell, Is.Not.Null, "предпосылка: ячейка материализована");
                Assert.That(ColorOf(cell.Background), Is.EqualTo(Colors.Transparent), "фон ячейки отдаётся строке");
                Assert.That(ColorOf(cell.Foreground), Is.EqualTo(palette.Foreground), "текст ячейки — палитра");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // --- Комбобоксы таблиц: столбцы Scope и Modifiers. ---
    // Регресс: в тёмной теме свёрнутый комбобокс стоял на белой плашке шаблона Aero2, а его список
    // (popup вне дерева таблицы) оставался с системными чёрными буквами на тёмном фоне.

    [Test]
    public void Apply_LateCreatedCellComboBoxIsThemed()
    {
        (Window window, Grid root, DataGrid grid, ObservableCollection<string> items) = CreateLiveComboTable();
        StudioThemeColors palette = StudioPalette.Dark;

        try
        {
            window.Show();
            WfDataGridTheme.Apply(root, palette);

            // Строка и её редактор создаются после применения темы — проход обязан их догнать.
            items.Add("first");
            Pump(window);

            ComboBox comboBox = Walk(grid).OfType<ComboBox>().FirstOrDefault();
            Assert.That(comboBox, Is.Not.Null, "предпосылка: комбобокс ячейки материализован");

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(comboBox.Background), Is.EqualTo(palette.Surface),
                    "под текстом комбобокса осталась белая плашка шаблона Aero2 — буквы на ней не читаются");
                Assert.That(ColorOf(comboBox.Foreground), Is.EqualTo(palette.Foreground), "текст — палитра");
                Assert.That(ColorOf(comboBox.BorderBrush), Is.EqualTo(palette.Separator), "рамка — hairline палитры");
                Assert.That(comboBox.FontFamily, Is.EqualTo(StudioFonts.UiFont), "шрифт комбобокса — Roboto");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Apply_CellComboBoxItemStyleUsesPalette()
    {
        (Window window, Grid root, DataGrid grid, ObservableCollection<string> items) = CreateLiveComboTable();
        StudioThemeColors palette = StudioPalette.Dark;

        try
        {
            window.Show();
            WfDataGridTheme.Apply(root, palette);
            items.Add("first");
            Pump(window);

            ComboBox comboBox = Walk(grid).OfType<ComboBox>().FirstOrDefault();
            Assert.That(comboBox, Is.Not.Null, "предпосылка: комбобокс ячейки материализован");

            Style style = comboBox.ItemContainerStyle;
            Assert.That(style, Is.Not.Null,
                "элементы списка остались на системном стиле Aero2: буквы в списке Modifiers не видны");

            Color itemForeground = SetterColourOf(style, Control.ForegroundProperty);
            Assert.Multiple(() =>
            {
                Assert.That(WfThemeStyles.IsThemed(style), Is.True, "стиль элементов списка обязан быть стилем палитры");
                Assert.That(itemForeground, Is.EqualTo(palette.Foreground),
                    "текст элемента списка обязан быть цветом палитры, а не системным чёрным");
                Assert.That(Math.Abs(Luminance(itemForeground) - Luminance(palette.Surface)),
                    Is.GreaterThanOrEqualTo(0.3), "контраст текста списка к поверхности ниже порога");
                Assert.That(TriggerColourOf(style, UIElement.IsMouseOverProperty, Control.BackgroundProperty),
                    Is.EqualTo(palette.Hover), "наведение в списке — Hover палитры, а не светлая плашка Aero2");
                Assert.That(TriggerColourOf(style, ListBoxItem.IsSelectedProperty, Control.BackgroundProperty),
                    Is.EqualTo(palette.Selected), "выделение в списке — Selected палитры, а не системное синее");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // --- Вкладки shell bar: «Переменные / Аргументы / Пространства имён», PanMode. ---

    [Test]
    public void Apply_ThemesShellBarItems()
    {
        (StatusBar shellBar, ToggleButton[] tabs, Button button, TextBlock[] captions) = BuildShellBar();
        StudioThemeColors palette = StudioPalette.Dark;

        WfDesignerTheme.ThemeShellBar(shellBar, palette);

        Assert.Multiple(() =>
        {
            foreach (ToggleButton tab in tabs)
            {
                bool active = tab.IsChecked == true;

                Assert.That(ColorOf(tab.Background), Is.EqualTo(active ? palette.Selected : Colors.Transparent),
                    $"{tab.Name}: активная вкладка — Selected, неактивная — без плашки");
                Assert.That(ColorOf(tab.Foreground), Is.EqualTo(active ? palette.Foreground : palette.ForegroundMuted),
                    $"{tab.Name}: неактивная подпись — ForegroundMuted");
                Assert.That(tab.BorderThickness, Is.EqualTo(new Thickness(0)), $"{tab.Name}: рамка системной темы погашена");
            }

            Assert.That(ColorOf(button.Foreground), Is.EqualTo(palette.ForegroundMuted), "кнопка без состояния — приглушённая подпись");
            Assert.That(ColorOf(button.Background), Is.EqualTo(Colors.Transparent));

            foreach (TextBlock caption in captions)
            {
                Assert.That(ColorOf(caption.Foreground), Is.Not.EqualTo(default(Color)),
                    "подпись вкладки обязана получить цвет из палитры");
            }
        });
    }

    [Test]
    public void Apply_ShellBarItemsFollowThemeSwitch()
    {
        (StatusBar shellBar, ToggleButton[] tabs, _, _) = BuildShellBar();

        WfDesignerTheme.ThemeShellBar(shellBar, StudioPalette.Dark);
        WfDesignerTheme.ThemeShellBar(shellBar, StudioPalette.Light);

        ToggleButton active = tabs.First(tab => tab.IsChecked == true);
        ToggleButton inactive = tabs.First(tab => tab.IsChecked != true);

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(active.Background), Is.EqualTo(StudioPalette.Light.Selected));
            Assert.That(ColorOf(active.Foreground), Is.EqualTo(StudioPalette.Light.Foreground));
            Assert.That(ColorOf(inactive.Foreground), Is.EqualTo(StudioPalette.Light.ForegroundMuted));
        });
    }
    [Test]
    public void Apply_ShellBarCaptionKeepsActiveStateAfterFullApply()
    {
        // Общий проход по тексту в Apply ставит Foreground каждому TextBlock локальным значением,
        // поэтому состояние вкладки обязано переписываться после него: иначе все подписи становились
        // основным цветом и активную вкладку было не отличить от неактивной.
        (Grid root, ToggleButton[] tabs, TextBlock[] captions) = BuildDesignerTreeWithShellBar();
        StudioThemeColors palette = StudioPalette.Dark;

        WfDesignerTheme.Apply(root, palette);

        Assert.Multiple(() =>
        {
            for (int index = 0; index < tabs.Length; index++)
            {
                Color expected = tabs[index].IsChecked == true ? palette.Foreground : palette.ForegroundMuted;
                Assert.That(ColorOf(captions[index].Foreground), Is.EqualTo(expected),
                    $"подпись вкладки #{index}: цвет обязан следовать состоянию и после общего прохода по тексту");
            }
        });
    }



    // --- Стражи по исходникам и проекту. ---

    [Test]
    public void Sources_TableThemeIsWiredIntoDesignerTheme()
    {
        string theme = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDesignerTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(theme, Does.Contain("WfDataGridTheme.Apply(root, palette);"),
                "таблицы обязаны темизироваться из единственного входа WfDesignerTheme.Apply");
            Assert.That(theme.IndexOf("WfDataGridTheme.Apply(root, palette);", StringComparison.Ordinal),
                Is.GreaterThan(theme.IndexOf("WfVisualTreeThemer.Apply(root, palette);", StringComparison.Ordinal)),
                "проход по таблицам идёт после общего темизатора WF: иначе он вернул бы светлые кисти");
            Assert.That(theme, Does.Contain("ThemeShellBarItems(shellBar, palette);"),
                "вкладки shell bar темизируются вместе с полосой");
        });
    }

    [Test]
    public void Sources_TableThemeUsesPaletteOnly()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDataGridTheme.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("StudioThemeColors"), "тематизация берёт цвета только из палитры");
            Assert.That(source, Does.Contain("WfThemeStyles.NeutralizeLightBrushes"),
                "светлые плашки шаблона гасятся общим правилом WF-контролов");
            Assert.That(source, Does.Contain("WfThemeStyles.MarkThemed"),
                "стили помечаются: смена темы пересоздаёт обёртку, а не оставляет кисти прежней");
            Assert.That(source, Does.Not.Contain("CellEditingTemplate"),
                "шаблоны правки запечатаны: обращаться к ним нельзя (дефект 1.0.0.0)");
            Assert.That(Regex.IsMatch(source, @"#[0-9A-Fa-f]{6}", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "зашитые hex-цвета запрещены");
            Assert.That(Regex.IsMatch(source, @"\bColors\.", RegexOptions.None, TimeSpan.FromSeconds(2)), Is.False,
                "зашитые системные цвета запрещены");
        });
    }

    [Test]
    public void Sources_TableThemeThemesComboBoxes()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfDataGridTheme.cs"));

        int boxes = source.IndexOf("WfVisualTreeThemer.ThemeComboBoxes", StringComparison.Ordinal);
        int plates = source.IndexOf("WfThemeStyles.NeutralizeLightBrushes(grid, palette);", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(boxes, Is.GreaterThanOrEqualTo(0),
                "комбобоксы столбцов Scope и Modifiers обязаны темизироваться общим механизмом");
            Assert.That(boxes, Is.GreaterThan(plates),
                "проход по комбобоксам идёт последним: он сам гасит плашки шаблона внутри комбобокса");
            Assert.That(source, Does.Contain("WfVisualTreeThemer.ApplyEditorPalette"),
                "палитра редактора ставится при входе в правку ячейки");
            Assert.That(source, Does.Contain("grid.PreparingCellForEdit +="),
                "иначе комбобокс окрашивался кадром позже — видна вспышка белой плашки Aero2");
        });
    }

    [Test]
    public void ProjectFile_CompilesTableTheme()
    {
        string project = ReadSource(Path.Combine("AiRpaStudio", "AiRpaStudio.csproj"));

        Assert.That(project, Does.Contain("Helpers\\WfDataGridTheme.cs"),
            "файл темизатора таблиц обязан быть в проекте: csproj перечисляет файлы явно");
    }

    // --- Вспомогательное: синтетический дизайнер и живая таблица. ---

    private sealed class DesignerTree
    {
        public Grid Root { get; set; }

        public Grid BottomPanel { get; set; }

        public Grid VariablesPanel { get; set; }

        public DataGrid Variables { get; set; }

        public DataGrid Arguments { get; set; }

        public DataGrid Namespaces { get; set; }
    }

    /// <summary>
    /// Дерево, повторяющее структуру <c>DesignerView</c>: поверхность → <c>bottomPanel</c> → панели
    /// переменных/аргументов/пространств имён. Имена таблиц взяты из Reference Source
    /// (<c>variableDataGrid</c>, <c>argumentsDataGrid</c>, <c>importedNamespacesDataGrid</c>).
    /// </summary>
    private static DesignerTree BuildDesignerTree()
    {
        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName };
        var bottomPanel = new Grid { Name = "bottomPanel" };
        var variablesPanel = new Grid { Name = "variables1", Background = Brushes.White };
        var argumentsPanel = new Grid { Name = "arguments1", Background = Brushes.White };
        var namespacesPanel = new Grid { Name = "imports1", Background = Brushes.White };

        DataGrid variables = CreateTable("variableDataGrid");
        DataGrid arguments = CreateTable("argumentsDataGrid");
        DataGrid namespaces = CreateTable("importedNamespacesDataGrid");

        variablesPanel.Children.Add(variables);
        argumentsPanel.Children.Add(arguments);
        namespacesPanel.Children.Add(namespaces);

        bottomPanel.Children.Add(variablesPanel);
        bottomPanel.Children.Add(argumentsPanel);
        bottomPanel.Children.Add(namespacesPanel);
        designerView.Content = bottomPanel;

        var root = new Grid { Name = "root" };
        root.Children.Add(designerView);

        return new DesignerTree
        {
            Root = root,
            BottomPanel = bottomPanel,
            VariablesPanel = variablesPanel,
            Variables = variables,
            Arguments = arguments,
            Namespaces = namespaces
        };
    }

    /// <summary>Таблица в том виде, в каком её объявляет WF: стили приходят из <c>defaultDataGridStyle</c>.</summary>
    private static DataGrid CreateTable(string name)
    {
        var grid = new DataGrid
        {
            Name = name,
            AutoGenerateColumns = false,
            ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader)),
            CellStyle = new Style(typeof(DataGridCell)),
            RowStyle = new Style(typeof(DataGridRow))
        };

        grid.Columns.Add(new DataGridTextColumn { Header = "Имя" });
        grid.Columns.Add(new DataGridTextColumn { Header = "Тип" });
        return grid;
    }

    /// <summary>Живая таблица в окне: только там материализуются шапка, строки и ячейки.</summary>
    private static (Window Window, Grid Root, DataGrid Grid, ObservableCollection<string> Items) CreateLiveTable()
    {
        var items = new ObservableCollection<string>();
        var grid = new DataGrid
        {
            Name = DesignerVariableContextMenuTableName,
            AutoGenerateColumns = false,
            ItemsSource = items
        };

        grid.Columns.Add(new DataGridTextColumn { Header = "Имя", Binding = new Binding(".") });

        var panel = new Grid { Name = "variables1" };
        panel.Children.Add(grid);

        var bottomPanel = new Grid();
        bottomPanel.Children.Add(panel);

        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName, Content = bottomPanel };
        var root = new Grid();
        root.Children.Add(designerView);

        var window = new Window
        {
            Width = 360,
            Height = 240,
            ShowInTaskbar = false,
            Content = root
        };

        return (window, root, grid, items);
    }

    /// <summary>
    /// Живая таблица, в ячейке которой лежит комбобокс: так WF объявляет столбцы Scope и Modifiers
    /// (DataGridComboBoxColumn/DataGridTemplateColumn с редактором-списком).
    /// </summary>
    private static (Window Window, Grid Root, DataGrid Grid, ObservableCollection<string> Items) CreateLiveComboTable()
    {
        var items = new ObservableCollection<string>();
        var grid = new DataGrid
        {
            Name = DesignerVariableContextMenuTableName,
            AutoGenerateColumns = false,
            ItemsSource = items
        };

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Scope",
            CellTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(ComboBox)) }
        });

        var panel = new Grid { Name = "variables1" };
        panel.Children.Add(grid);

        var bottomPanel = new Grid();
        bottomPanel.Children.Add(panel);

        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName, Content = bottomPanel };
        var root = new Grid();
        root.Children.Add(designerView);

        var window = new Window
        {
            Width = 360,
            Height = 240,
            ShowInTaskbar = false,
            Content = root
        };

        return (window, root, grid, items);
    }

    private const string DesignerVariableContextMenuTableName = "variableDataGrid";

    /// <summary>Shell bar как у WF: вкладки — <see cref="ToggleButton"/> с подписью-<see cref="TextBlock"/>.</summary>
    private static (StatusBar ShellBar, ToggleButton[] Tabs, Button Button, TextBlock[] Captions) BuildShellBar()
    {
        var shellBar = new StatusBar { Name = WfDesignerTheme.ShellBarElementName };

        string[] titles = { "Переменные", "Аргументы", "Пространства имён" };
        var tabs = new List<ToggleButton>();
        var captions = new List<TextBlock>();

        for (int index = 0; index < titles.Length; index++)
        {
            var caption = new TextBlock { Text = titles[index] };
            var tab = new ToggleButton { Name = "tab" + index, Content = caption, IsChecked = index == 0 };
            var item = new StatusBarItem { Content = tab };

            _ = shellBar.Items.Add(item);
            tabs.Add(tab);
            captions.Add(caption);
        }

        var button = new Button { Name = "buttonResetZoom", Content = "100%" };
        _ = shellBar.Items.Add(new StatusBarItem { Content = button });

        return (shellBar, tabs.ToArray(), button, captions.ToArray());
    }
    /// <summary>Дерево дизайнера с shell bar и одной таблицей: проверяется полный проход <c>Apply</c>.</summary>
    private static (Grid Root, ToggleButton[] Tabs, TextBlock[] Captions) BuildDesignerTreeWithShellBar()
    {
        (StatusBar shellBar, ToggleButton[] tabs, _, TextBlock[] captions) = BuildShellBar();

        var panel = new Grid { Name = "variables1" };
        panel.Children.Add(CreateTable("variableDataGrid"));

        var bottomPanel = new Grid();
        bottomPanel.Children.Add(panel);

        var layout = new Grid();
        layout.Children.Add(bottomPanel);
        layout.Children.Add(shellBar);

        var designerView = new UserControl { Name = WfDesignerTheme.DesignerViewElementName, Content = layout };
        var root = new Grid();
        root.Children.Add(designerView);

        return (root, tabs, captions);
    }



    // --- Комбобоксы панели «Свойства активности»: Scope и Modifiers. ---
    // Регресс: свёрнутый комбобокс стоял на белом поле правки (PART_EditableTextBox материализуется только
    // после реализации шаблона, и проход темы его не находил), а список Modifiers (flags-enum → чекбоксы
    // ReadOnly/Mapped) оставался с системными чёрными буквами на тёмной подложке popup.

    [Test]
    public void ThemeComboBoxes_PaintsEditableFieldOfCollapsedComboBox()
    {
        StudioThemeColors palette = StudioPalette.Dark;
        var comboBox = new ComboBox
        {
            IsEditable = true,
            Text = "ReadOnly, Mapped",
            ItemsSource = new[] { "ReadOnly", "Mapped" }
        };

        var root = new Grid();
        root.Children.Add(comboBox);

        WfVisualTreeThemer.ThemeComboBoxes(root, palette);

        TextBox editableField = Walk(comboBox).OfType<TextBox>().FirstOrDefault();
        Assert.That(editableField, Is.Not.Null,
            "поле правки не материализовано: шаблон комбобокса не реализован до покраски");

        Assert.Multiple(() =>
        {
            Assert.That(ColorOf(editableField.Background), Is.EqualTo(palette.Surface),
                "поле правки осталось белым — это и есть «белый комбобокс» Scope/Modifiers в тёмной теме");
            Assert.That(ColorOf(editableField.Foreground), Is.EqualTo(palette.Foreground),
                "текст поля правки обязан быть цветом палитры");
        });
    }

    [Test]
    public void ThemeComboBoxes_PaintsDropDownCheckBoxTextWithPalette()
    {
        StudioThemeColors palette = StudioPalette.Dark;
        var comboBox = new ComboBox { ItemsSource = new[] { "ReadOnly", "Mapped" } };
        comboBox.ItemTemplate = CreateBlackCheckBoxTemplate();

        var root = new Grid();
        root.Children.Add(comboBox);
        var window = new Window
        {
            Width = 320,
            Height = 200,
            ShowInTaskbar = false,
            Content = root
        };

        try
        {
            window.Show();
            Pump(window);

            WfVisualTreeThemer.ThemeComboBoxes(root, palette);
            comboBox.IsDropDownOpen = true;
            Pump(window);

            var popup = comboBox.Template.FindName("PART_Popup", comboBox) as Popup;
            Assert.That(popup, Is.Not.Null, "предпосылка: шаблон комбобокса содержит popup");
            Assert.That(popup.Child, Is.Not.Null, "предпосылка: список раскрыт");

            List<CheckBox> checkBoxes = Walk(popup.Child).OfType<CheckBox>().ToList();
            Assert.That(checkBoxes, Is.Not.Empty, "предпосылка: элементы списка — чекбоксы (как у flags-enum Modifiers)");

            foreach (CheckBox checkBox in checkBoxes)
            {
                Assert.That(ColorOf(checkBox.Foreground), Is.EqualTo(palette.Foreground),
                    "подпись чекбокса осталась системной чёрной — буквы на тёмном фоне не видны");

                foreach (TextBlock textBlock in Walk(checkBox).OfType<TextBlock>())
                {
                    Assert.That(ColorOf(textBlock.Foreground), Is.EqualTo(palette.Foreground),
                        "текст чекбокса в списке обязан быть цветом палитры");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SetPropertyStyles_ThemesComboBoxOfRowCreatedAfterSweep()
    {
        var helper = new PropertyWindowStyleHelper();
        StudioThemeColors palette = StudioPalette.Dark;
        var root = new Grid();
        var window = new Window
        {
            Width = 360,
            Height = 240,
            ShowInTaskbar = false,
            Content = root
        };

        try
        {
            window.Show();
            Pump(window);
            helper.SetPropertyStyles(root, palette);

            // Строку свойств WF создаёт после применения темы: выбор активности, разворот категории.
            var comboBox = new ComboBox { ItemsSource = new[] { "Public", "Private" } };
            root.Children.Add(comboBox);

            // Клик внутри панели — точка, где строка уже существует.
            root.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseDownEvent
            });

            // Раскладка намеренно не прогоняется: проверяется именно обработчик взаимодействия,
            // а не проход по изменившемуся отпечатку дерева.

            Assert.Multiple(() =>
            {
                Assert.That(ColorOf(comboBox.Background), Is.EqualTo(palette.Surface),
                    "поздно созданный комбобокс строки «Свойства» остался белым");
                Assert.That(comboBox.ItemContainerStyle, Is.Not.Null,
                    "элементы списка остались на системном стиле Aero2");
                Assert.That(WfThemeStyles.IsThemed(comboBox.ItemContainerStyle), Is.True,
                    "стиль элементов списка обязан быть стилем палитры");
            });
        }
        finally
        {
            window.Close();
        }
    }

    // --- Комбобокс, созданный после прохода темы (та самая «белая плашка до первого клика»). ---

    [Test]
    public void ThemeComboBoxes_ComboBoxCreatedAfterSweep_IsThemedOnLoad()
    {
        StudioThemeColors palette = StudioPalette.Dark;
        var root = new Grid();
        var window = new Window
        {
            Width = 360,
            Height = 240,
            ShowInTaskbar = false,
            Content = root
        };

        try
        {
            window.Show();
            Pump(window);

            // В панели уже есть комбобоксы строк (редакторы соседних свойств): проход их находит,
            // поэтому отложенный обход не назначается — ровно то состояние, в котором оказывается
            // «Свойства активности» после выбора переменной.
            root.Children.Add(new ComboBox { ItemsSource = new[] { "Public", "Private" } });
            Pump(window);
            WfVisualTreeThemer.ThemeComboBoxes(root, palette);

            // Редактор значения WF подставляется позже прохода: FlagEditor (flags-enum Modifiers) —
            // редактируемый ComboBox со своим стилем элементов списка.
            var comboBox = new ComboBox
            {
                IsEditable = true,
                Text = "ReadOnly",
                ItemsSource = new[] { "ReadOnly", "Mapped" },
                ItemContainerStyle = new Style(typeof(ComboBoxItem))
            };

            root.Children.Add(comboBox);
            Pump(window);

            TextBox field = Walk(comboBox).OfType<TextBox>().FirstOrDefault();

            Assert.Multiple(() =>
            {
                Assert.That(field, Is.Not.Null, "предпосылка: у редактируемого комбобокса материализовано поле правки");
                Assert.That(ColorOf(comboBox.Background), Is.EqualTo(palette.Surface),
                    "комбобокс, созданный после прохода темы, остался на системной теме — белая плашка до первого клика");
                Assert.That(ColorOf(field.Background), Is.EqualTo(palette.Surface),
                    "поле правки поздно созданного комбобокса осталось белым");
                Assert.That(WfThemeStyles.IsThemed(comboBox.ItemContainerStyle), Is.True,
                    "элементы списка поздно созданного комбобокса остались на системном стиле Aero2");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ThemeComboBoxes_ComboBoxOutsideThemedRootsKeepsSystemChrome()
    {
        var comboBox = new ComboBox
        {
            IsEditable = true,
            Text = "a",
            ItemsSource = new[] { "a", "b" }
        };

        var window = new Window
        {
            Width = 240,
            Height = 160,
            ShowInTaskbar = false,
            Content = comboBox
        };

        try
        {
            window.Show();
            Pump(window);

            Assert.Multiple(() =>
            {
                Assert.That(comboBox.ItemContainerStyle, Is.Null,
                    "подписка на Loaded всех комбобоксов не должна темизировать контролы вне WF-поверхностей");
                Assert.That(WfThemeStyles.IsThemed(comboBox.Style), Is.False,
                    "стиль комбобокса вне WF-поверхностей подменять нельзя");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Sources_ThemerThemesLateCreatedComboBoxesOnLayout()
    {
        string source = ReadSource(Path.Combine("AiRpaStudio", "Helpers", "WfVisualTreeThemer.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("HookLayoutSweep"),
                "поздно созданные комбобоксы WF должны доводиться проходом по раскладке корня");
            Assert.That(source, Does.Contain("element.LayoutUpdated +="),
                "корень обязан перепроверяться по LayoutUpdated, иначе новый комбобокс не будет найден");
            Assert.That(source, Does.Contain("ThemeUnthemedComboBoxes"),
                "проход по раскладке должен перекрашивать только комбобоксы без отметки палитры");
            Assert.That(source, Does.Contain("public bool Pending { get; set; }"),
                "повторная проверка по раскладке обязана быть одноразовой на цикл раскладки");
            Assert.That(source, Does.Contain("ScheduleComboBoxRetry"),
                "комбобокс без материализованного шаблона должен доводиться отложенным проходом");
            Assert.That(Regex.IsMatch(source, @"private const bool EnableComboBoxDiagnostics = false;"),
                "временная диагностика обязана оставаться выключенной");
        });
    }

    /// <summary>Элемент списка как у WF-flags-enum: чекбокс с жёстко заданным системным чёрным текстом.</summary>
    private static DataTemplate CreateBlackCheckBoxTemplate()
    {
        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetValue(Control.ForegroundProperty, Brushes.Black);
        checkBox.SetBinding(ContentControl.ContentProperty, new Binding());

        return new DataTemplate { VisualTree = checkBox };
    }

    private static Color SetterColourOf(Style style, DependencyProperty property)
    {
        for (Style current = style; current != null; current = current.BasedOn)
        {
            foreach (Setter setter in current.Setters.OfType<Setter>())
            {
                if (setter.Property == property && setter.Value is Brush brush)
                {
                    return ColorOf(brush);
                }
            }
        }

        Assert.Fail($"в стиле нет сеттера {property.Name}");
        return default;
    }

    private static Color TriggerColourOf(Style style, DependencyProperty triggerProperty, DependencyProperty setterProperty)
    {
        for (Style current = style; current != null; current = current.BasedOn)
        {
            foreach (Trigger trigger in current.Triggers.OfType<Trigger>())
            {
                if (trigger.Property != triggerProperty)
                {
                    continue;
                }

                foreach (Setter setter in trigger.Setters.OfType<Setter>())
                {
                    if (setter.Property == setterProperty && setter.Value is Brush brush)
                    {
                        return ColorOf(brush);
                    }
                }
            }
        }

        Assert.Fail($"в стиле нет триггера {triggerProperty.Name} → {setterProperty.Name}");
        return default;
    }

    private static Color ColorOf(Brush brush)
    {
        Assert.That(brush, Is.InstanceOf<SolidColorBrush>(), "ожидалась однотонная кисть палитры");
        return ((SolidColorBrush)brush).Color;
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }

    /// <summary>Обход поддерева: логические дети (до применения шаблона) + визуальные (после).</summary>
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        if (root == null)
        {
            yield break;
        }

        var seen = new HashSet<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            DependencyObject element = pending.Pop();
            if (!seen.Add(element))
            {
                continue;
            }

            yield return element;

            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                if (child is DependencyObject logicalChild)
                {
                    pending.Push(logicalChild);
                }
            }

            if (element is Visual)
            {
                int count = VisualTreeHelper.GetChildrenCount(element);
                for (int index = 0; index < count; index++)
                {
                    pending.Push(VisualTreeHelper.GetChild(element, index));
                }
            }
        }
    }

    /// <summary>
    /// Светлые кисти внутри поддерева: остатки шаблонов системной темы (Aero2). Кисти палитры и текст
    /// разрешены — они и есть результат темизации.
    /// </summary>
    private static List<string> LightBrushesIn(DependencyObject root, StudioThemeColors palette)
    {
        var violations = new List<string>();
        double surface = Luminance(palette.Surface);
        var paletteColors = new HashSet<Color>(palette.Tokens.Values);

        foreach (DependencyObject element in Walk(root))
        {
            foreach (DependencyProperty property in BrushDpsOf(element.GetType()))
            {
                if (property.Name == "Foreground" || !(element.GetValue(property) is Brush brush)
                    || ReferenceEquals(brush, Brushes.Transparent))
                {
                    continue;
                }

                if (brush is SolidColorBrush solid)
                {
                    if (IsLightArtifact(solid.Color, surface, paletteColors))
                    {
                        violations.Add($"{element.GetType().Name}.{property.Name} = {solid.Color}");
                    }

                    continue;
                }

                if (brush is GradientBrush gradient
                    && gradient.GradientStops.Any(stop => IsLightArtifact(stop.Color, surface, paletteColors)))
                {
                    violations.Add($"{element.GetType().Name}.{property.Name} = gradient");
                }
            }
        }

        return violations;
    }

    private static bool IsLightArtifact(Color color, double surfaceLuminance, HashSet<Color> paletteColors)
    {
        return color.A != 0
            && !paletteColors.Contains(color)
            && Luminance(color) > surfaceLuminance + WfThemeStyles.LightPlateThreshold;
    }

    private static double Luminance(Color color)
    {
        return (0.2126 * LinearChannel(color.R)) + (0.7152 * LinearChannel(color.G)) + (0.0722 * LinearChannel(color.B));
    }

    private static double LinearChannel(byte value)
    {
        double channel = value / 255d;
        return channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    /// <summary>Свойства типа Brush, объявленные в типе и его базовых типах.</summary>
    private static IEnumerable<DependencyProperty> BrushDpsOf(Type type)
    {
        for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (System.Reflection.FieldInfo field in current.GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(DependencyProperty)
                    && field.GetValue(null) is DependencyProperty property
                    && typeof(Brush).IsAssignableFrom(property.PropertyType))
                {
                    yield return property;
                }
            }
        }
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
