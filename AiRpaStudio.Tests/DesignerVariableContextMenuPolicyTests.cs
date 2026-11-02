using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using NUnit.Framework;
using RPA.Core.Services;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Чистая логика контекстного меню в таблицах переменных/аргументов дизайнера
/// (<see cref="DesignerVariableContextMenuPolicy"/>, <see cref="DesignerContextMenuState"/>).
/// <para>
/// Принципы и инварианты — docs\Designer_Variable_ContextMenu_Principles.md. Эти тесты не требуют
/// HWND, <c>WorkflowDesigner</c> и реального ввода мышью: проверяются решения, из-за которых меню
/// исторически ломалось (клик мимо поля взводил «залипающий» флаг отмены, меню закрывалось по любой
/// клавише, не был задан <c>PlacementTarget</c>).
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class DesignerVariableContextMenuPolicyTests
{
    // --- Куда пришёл правый клик: поле правки и взвод отмены завершения правки. ---

    [Test]
    public void FindEditingField_TextBox_IsReturned()
    {
        var textBox = new TextBox();

        Assert.That(DesignerVariableContextMenuPolicy.FindEditingField(textBox), Is.SameAs(textBox),
            "правый клик в поле ввода ячейки — это поле правки");
    }

    [Test]
    public void FindEditingField_VisualChildOfTextBox_IsReturned()
    {
        var textBox = new TextBox { Text = "cell" };
        var window = new Window { Width = 200, Height = 80, ShowInTaskbar = false, Content = textBox };
        window.Show();

        try
        {
            window.UpdateLayout();

            DependencyObject child = VisualTreeHelper.GetChildrenCount(textBox) > 0
                ? VisualTreeHelper.GetChild(textBox, 0)
                : null;

            Assert.That(child, Is.Not.Null, "в шаблоне TextBox нет визуального потомка — проверка невозможна");
            Assert.That(DesignerVariableContextMenuPolicy.FindEditingField(child), Is.SameAs(textBox),
                "клик по визуальному потомку (текст, каретка) — тот же клик в поле правки");
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void FindEditingField_NonFieldSource_IsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerVariableContextMenuPolicy.FindEditingField(new Border()), Is.Null,
                "клик мимо поля ввода (заголовок, скроллбар, пустая область) — не поле правки");
            Assert.That(DesignerVariableContextMenuPolicy.FindEditingField(null), Is.Null);
            Assert.That(DesignerVariableContextMenuPolicy.FindEditingField(new RowDefinition()), Is.Null,
                "не-визуальный узел (RowDefinition) не должен бросать при поиске визуального родителя");
        });
    }

    [Test]
    public void ShouldArmCancel_TextBox_IsArmed()
    {
        Assert.That(DesignerVariableContextMenuPolicy.ShouldArmCancel(new TextBox()), Is.True,
            "правый клик в поле ввода ячейки обязан взводить отмену CellEditEnding");
    }

    [Test]
    public void ShouldArmCancel_NonFieldSource_IsNotArmed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerVariableContextMenuPolicy.ShouldArmCancel(new Border()), Is.False,
                "клик мимо поля ввода не должен взводить отмену");
            Assert.That(DesignerVariableContextMenuPolicy.ShouldArmCancel(null), Is.False);
            Assert.That(DesignerVariableContextMenuPolicy.ShouldArmCancel(new RowDefinition()), Is.False);
        });
    }

    [Test]
    public void ShouldArmCancel_DelegatesToFindEditingField()
    {
        var textBox = new TextBox();
        var window = new Window { Width = 200, Height = 80, ShowInTaskbar = false, Content = textBox };
        window.Show();

        try
        {
            window.UpdateLayout();

            DependencyObject child = VisualTreeHelper.GetChildrenCount(textBox) > 0
                ? VisualTreeHelper.GetChild(textBox, 0)
                : null;

            Assert.That(child, Is.Not.Null);
            Assert.That(DesignerVariableContextMenuPolicy.ShouldArmCancel(child),
                Is.EqualTo(DesignerVariableContextMenuPolicy.FindEditingField(child) != null),
                "одно правило на взвод отмены и на выбор поля для меню: дублирование проверок давало расхождения");
        }
        finally
        {
            window.Close();
        }
    }

    // --- Автомат: отмена одноразовая, взвод снимается закрытием меню. ---

    [Test]
    public void State_Initially_Idle()
    {
        var state = new DesignerContextMenuState();

        Assert.Multiple(() =>
        {
            Assert.That(state.IsMenuOpen, Is.False);
            Assert.That(state.IsCancelArmed, Is.False);
            Assert.That(state.NotifyCellEditEnding(), Is.False, "без правого клика завершение правки не отменяется");
        });
    }

    [Test]
    public void State_RightButtonDown_ArmsCancel()
    {
        var state = new DesignerContextMenuState();

        state.NotifyRightButtonDown();

        Assert.That(state.IsCancelArmed, Is.True);
    }

    [Test]
    public void State_CellEditEnding_CancelsOnceOnly()
    {
        var state = new DesignerContextMenuState();
        state.NotifyRightButtonDown();

        Assert.Multiple(() =>
        {
            Assert.That(state.NotifyCellEditEnding(), Is.True, "первое завершение правки отменяется");
            Assert.That(state.NotifyCellEditEnding(), Is.False,
                "второе завершение правки отменять нельзя: флаг обязан сбрасываться");
            Assert.That(state.IsCancelArmed, Is.False);
        });
    }

    [Test]
    public void State_RepeatedArm_IsStillSingleShot()
    {
        var state = new DesignerContextMenuState();
        state.NotifyRightButtonDown();
        state.NotifyRightButtonDown();

        Assert.That(state.NotifyCellEditEnding(), Is.True);
        Assert.That(state.NotifyCellEditEnding(), Is.False, "повторный взвод не делает отмену многоразовой");
    }

    [Test]
    public void State_MenuOpened_KeepsCancelArmed()
    {
        // Меню открывается отложенно (DispatcherPriority.Input) и Opened приходит раньше CellEditEnding:
        // если сбрасывать взвод в Opened, правка всё равно завершится и ячейка закроется.
        var state = new DesignerContextMenuState();
        state.NotifyRightButtonDown();
        state.NotifyMenuOpened();

        Assert.Multiple(() =>
        {
            Assert.That(state.IsMenuOpen, Is.True);
            Assert.That(state.IsCancelArmed, Is.True, "Opened не должен снимать взвод отмены");
            Assert.That(state.NotifyCellEditEnding(), Is.True);
        });
    }

    [Test]
    public void State_MenuClosed_ResetsEverything()
    {
        var state = new DesignerContextMenuState();
        state.NotifyRightButtonDown();
        state.NotifyMenuOpened();

        state.NotifyMenuClosed();

        Assert.Multiple(() =>
        {
            Assert.That(state.IsMenuOpen, Is.False);
            Assert.That(state.IsCancelArmed, Is.False, "закрытие меню снимает взвод: иначе он «залипал» до следующей правки");
            Assert.That(state.NotifyCellEditEnding(), Is.False);
        });
    }

    // --- Закрытие меню: клик вне и Escape. ---

    [Test]
    public void ShouldCloseOnMouseDown_OnlyOutsideMenuAndField()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerVariableContextMenuPolicy.ShouldCloseOnMouseDown(false, false), Is.True,
                "клик вне меню и вне поля ввода закрывает меню");
            Assert.That(DesignerVariableContextMenuPolicy.ShouldCloseOnMouseDown(true, false), Is.False,
                "клик по меню не должен закрывать его до выбора пункта");
            Assert.That(DesignerVariableContextMenuPolicy.ShouldCloseOnMouseDown(false, true), Is.False,
                "клик по полю ввода оставляет меню открытым");
        });
    }

    [Test]
    public void ShouldCloseOnKey_OnlyEscape()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerVariableContextMenuPolicy.ShouldCloseOnKey(Key.Escape), Is.True);
            Assert.That(DesignerVariableContextMenuPolicy.ShouldCloseOnKey(Key.C), Is.False,
                "Ctrl+C внутри меню не должен закрывать меню");
            Assert.That(DesignerVariableContextMenuPolicy.ShouldCloseOnKey(Key.None), Is.False);
        });
    }

    // --- Таблица: можно ли подключаться. ---

    [Test]
    public void CanAttach_RequiresColumns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerVariableContextMenuPolicy.CanAttach(null), Is.False);
            Assert.That(DesignerVariableContextMenuPolicy.CanAttach(new DataGrid()), Is.False,
                "таблица без столбцов ещё не собрана — подключаться некуда");
            Assert.That(DesignerVariableContextMenuPolicy.CanAttach(CreateGridWithColumns(1)), Is.True);
            Assert.That(DesignerVariableContextMenuPolicy.CanAttach(CreateTextOnlyGrid()), Is.True,
                "столбцы DataGridTextColumn тоже правятся в TextBox — подключаться нужно");
        });
    }

    // --- Инициализация по видимости панели: ровно один раз. ---

    [Test]
    public void ShouldInitializeOnVisibilityChanged_OnlyFirstTimeWhenVisible()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DesignerVariableContextMenuPolicy.ShouldInitializeOnVisibilityChanged(true, false), Is.True);
            Assert.That(DesignerVariableContextMenuPolicy.ShouldInitializeOnVisibilityChanged(true, true), Is.False,
                "повторная инициализация дублировала бы обработчики таблицы");
            Assert.That(DesignerVariableContextMenuPolicy.ShouldInitializeOnVisibilityChanged(false, false), Is.False);
        });
    }

    // --- Позиционирование меню: открывается программно, цель обязана быть задана. ---

    [Test]
    public void ConfigureMenuPlacement_SetsTargetAndMousePoint()
    {
        var textBox = new TextBox();
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);

        DesignerVariableContextMenuPolicy.ConfigureMenuPlacement(menu, textBox);

        Assert.Multiple(() =>
        {
            Assert.That(menu.PlacementTarget, Is.SameAs(textBox),
                "без PlacementTarget меню открывается в углу экрана, а не у ячейки");
            Assert.That(menu.Placement, Is.EqualTo(PlacementMode.MousePoint));
        });
    }

    [Test]
    public void ConfigureMenuPlacement_NullArguments_DoNotThrow()
    {
        ContextMenu menu = WfDesignerTheme.CreateEditingContextMenu(StudioPalette.Dark);

        Assert.DoesNotThrow(() => DesignerVariableContextMenuPolicy.ConfigureMenuPlacement(null, new TextBox()));
        Assert.DoesNotThrow(() => DesignerVariableContextMenuPolicy.ConfigureMenuPlacement(menu, null));
    }

    private static DataGrid CreateGridWithColumns(int count)
    {
        var dataGrid = new DataGrid();
        for (int index = 0; index < count; index++)
        {
            dataGrid.Columns.Add(CreateTemplateColumn());
        }

        return dataGrid;
    }

    private static DataGrid CreateTextOnlyGrid()
    {
        var dataGrid = new DataGrid();
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "Имя" });
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "Тип" });
        return dataGrid;
    }

    private static DataGridTemplateColumn CreateTemplateColumn()
    {
        return new DataGridTemplateColumn { CellEditingTemplate = CreateTemplate() };
    }

    private static DataTemplate CreateTemplate()
    {
        var textBox = new FrameworkElementFactory(typeof(TextBox));
        return new DataTemplate { VisualTree = textBox };
    }
}
