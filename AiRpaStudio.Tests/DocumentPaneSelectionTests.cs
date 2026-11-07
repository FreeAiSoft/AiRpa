using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using AvalonDock.Layout;
using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Инвариант панели документов: выбрана (то есть видна и подсвечена) <b>ровно одна</b> вкладка, и она же
/// — активный документ раскладки.
/// <para>
/// Баг («после перезапуска активна не та вкладка, а две вкладки подсвечены как активные») — не в
/// закрытии вкладок, а в вендорной панели документов AvalonDock: <c>LayoutContent.IsSelected</c> для
/// <see cref="LayoutDocumentPane"/> принудительно обнулял <c>SelectedContentIndex</c>, поэтому
/// <c>IsSelected</c> накапливался на нескольких вкладках (подсветка по <c>Selector.IsSelected</c>), а
/// <c>SelectedContent</c> всегда оставался <c>null</c> — то есть <c>TabControl</c> показывал не ту
/// вкладку, и восстановление активной вкладки не снимало выбор с последней добавленной.
/// </para>
/// <para>
/// Здесь настоящие модели AvalonDock (<see cref="LayoutRoot"/>, <see cref="LayoutDocumentPane"/>,
/// <see cref="LayoutDocument"/>) без окна: <c>TabItem.IsSelected</c> в теме студии привязан к
/// <c>IsSelected</c> TwoWay, а содержимое окна берётся из <c>SelectedContent</c>, поэтому проверка
/// модели проверяет и то, что видит пользователь.
/// </para>
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class DocumentPaneSelectionTests
{
    private const string WorkflowA = @"C:\project\A.xaml";
    private const string WorkflowB = @"C:\project\B.xaml";
    private const string WorkflowC = @"C:\project\C.xaml";
    private const string CodeB = @"C:\project\B.cs";

    [Test]
    public void OpeningTabs_KeepsExactlyOneSelectedTab()
    {
        LayoutDocumentPane pane = CreatePane(out _);

        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);

        List<LayoutDocument> selected = SelectedTabs(pane);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Has.Count.EqualTo(1), "выбранной (видимой) может быть только одна вкладка");
            Assert.That(selected[0], Is.SameAs(c), "видна последняя открытая вкладка");
            Assert.That(a.IsSelected, Is.False);
            Assert.That(b.IsSelected, Is.False);
        });
    }

    [Test]
    public void SelectedContent_IsTheVisibleTab()
    {
        LayoutDocumentPane pane = CreatePane(out _);

        AddDesignerTab(pane, WorkflowA);
        AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);

        Assert.Multiple(() =>
        {
            Assert.That(pane.SelectedContentIndex, Is.EqualTo(2), "индекс выбранной вкладки должен быть валидным");
            Assert.That(pane.SelectedContent, Is.SameAs(c),
                "SelectedContent — содержимое окна документов; null означает «ничего не показано»");
        });
    }

    [Test]
    public void SelectingTab_LeavesOtherTabsUnselected()
    {
        LayoutDocumentPane pane = CreatePane(out _);

        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        AddDesignerTab(pane, WorkflowB);
        AddDesignerTab(pane, WorkflowC);

        // Клик по заголовку вкладки: TabItem.IsSelected привязан к IsSelected TwoWay.
        a.IsSelected = true;

        Assert.Multiple(() =>
        {
            Assert.That(SelectedTabs(pane), Has.Count.EqualTo(1), "выбор переносится, а не добавляется");
            Assert.That(pane.SelectedContent, Is.SameAs(a));
            Assert.That(pane.SelectedContentIndex, Is.EqualTo(0));
        });
    }

    [Test]
    public void ActivatingRestoredTab_ClearsLastAddedTab()
    {
        LayoutDocumentPane pane = CreatePane(out LayoutRoot root);

        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);

        // Восстановление сессии: ActivateDocument → SetActiveDesigner → SetSelectedTab → IsActive = true.
        b.IsActive = true;

        Assert.Multiple(() =>
        {
            Assert.That(SelectedTabs(pane), Has.Count.EqualTo(1), "две подсвеченные вкладки — это и есть наблюдавшийся баг");
            Assert.That(b.IsSelected, Is.True, "активной должна стать сохранённая вкладка");
            Assert.That(c.IsSelected, Is.False, "последняя открытая вкладка не должна оставаться выбранной");
            Assert.That(a.IsSelected, Is.False);
            Assert.That(pane.SelectedContent, Is.SameAs(b), "окно документов показывает восстановленную вкладку");
            Assert.That(root.ActiveContent, Is.SameAs(b));
        });
    }

    [Test]
    public void RestoreFlow_SavesTheTabThatWasActivated()
    {
        LayoutDocumentPane pane = CreatePane(out _);

        // Восстановление открывает вкладки в сохранённом порядке ...
        AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        AddDesignerTab(pane, WorkflowC);

        // ... и затем активирует сохранённую активную вкладку.
        b.IsActive = true;

        // Сохранение сессии: Main.ActiveDocumentPath → MainView.GetActiveDocumentPath.
        LayoutDocument active = DocumentTabsCloseCoordinator.FindActive(pane.Children.OfType<LayoutDocument>());

        Assert.That(DocumentTabsCloseCoordinator.PathOf(active), Is.EqualTo(WorkflowB),
            "в настройках должна сохраниться активированная вкладка, а не последняя открытая");
    }

    [Test]
    public void ActivatingTab_PrefersActiveContentOverStaleSelection()
    {
        LayoutDocumentPane pane = CreatePane(out _);

        AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);

        // Рассинхрон двух флагов (наследие старого патча): выбрать надо активный документ раскладки.
        b.IsActive = true;

        Assert.That(DocumentTabsCloseCoordinator.FindActive(pane.Children.OfType<LayoutDocument>()), Is.SameAs(b),
            "активный документ раскладки — источник правды при рассинхроне с IsSelected");
    }

    [Test]
    public void ClosingMiddleTab_KeepsExactlyOneSelectedTab()
    {
        LayoutDocumentPane pane = CreatePane(out _);
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);

        AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);
        b.IsActive = true;

        b.Close();
        _ = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(SelectedTabs(pane), Has.Count.EqualTo(1));
            Assert.That(c.IsSelected, Is.True, "соседняя вкладка справа");
            Assert.That(pane.SelectedContent, Is.SameAs(c));
        });
    }

    [Test]
    public void ClosingBackgroundTab_KeepsSelectionOnActiveTab()
    {
        LayoutDocumentPane pane = CreatePane(out _);
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);

        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        AddDesignerTab(pane, WorkflowC);
        b.IsActive = true;

        a.Close();
        _ = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(SelectedTabs(pane), Has.Count.EqualTo(1));
            Assert.That(b.IsSelected, Is.True, "закрытие фоновой вкладки не меняет активную");
            Assert.That(pane.SelectedContent, Is.SameAs(b));
        });
    }

    [Test]
    public void CodeAndDesignerTabs_ShareOneSelection()
    {
        LayoutDocumentPane pane = CreatePane(out _);

        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument code = AddCodeTab(pane, CodeB);

        Assert.That(SelectedTabs(pane), Has.Count.EqualTo(1), "вкладка редактора кода участвует в том же выборе");

        a.IsSelected = true;

        Assert.Multiple(() =>
        {
            Assert.That(code.IsSelected, Is.False);
            Assert.That(pane.SelectedContent, Is.SameAs(a));
        });
    }

    [Test]
    public void ClosingLastTab_LeavesPaneWithoutSelection()
    {
        LayoutDocumentPane pane = CreatePane(out _);
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);

        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        a.IsActive = true;

        a.Close();
        _ = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(pane.Children, Is.Empty);
            Assert.That(pane.SelectedContent, Is.Null, "в пустой панели показывать нечего");
            Assert.That(pane.SelectedContentIndex, Is.EqualTo(-1));
        });
    }

    [Test]
    public void UserScenario_RestartAfterSwitchingTab_RestoresThatTab()
    {
        // Первый запуск: три открытые вкладки, видна последняя открытая — 3-я.
        LayoutDocumentPane firstRun = CreatePane(out _);
        AddDesignerTab(firstRun, WorkflowA);
        LayoutDocument second = AddDesignerTab(firstRun, WorkflowB);
        LayoutDocument third = AddDesignerTab(firstRun, WorkflowC);

        // Пользователь переключился на 2-ю вкладку: клик по заголовку — это LayoutDocumentTabItem → IsActive = true.
        second.IsActive = true;

        Assert.Multiple(() =>
        {
            Assert.That(SelectedTabs(firstRun), Has.Count.EqualTo(1), "переключение не оставляет подсвеченной 3-ю вкладку");
            Assert.That(third.IsSelected, Is.False);
        });

        // Закрытие студии: сохраняется активная вкладка.
        string saved = DocumentTabsCloseCoordinator.PathOf(
            DocumentTabsCloseCoordinator.FindActive(firstRun.Children.OfType<LayoutDocument>()));
        Assert.That(saved, Is.EqualTo(WorkflowB));

        // Новый запуск: вкладки открываются по одной (каждая IsSelected = true), затем активируется сохранённая.
        LayoutDocumentPane secondRun = CreatePane(out _);
        AddDesignerTab(secondRun, WorkflowA);
        LayoutDocument restored = AddDesignerTab(secondRun, WorkflowB);
        LayoutDocument lastOpened = AddDesignerTab(secondRun, WorkflowC);
        restored.IsActive = true;

        Assert.Multiple(() =>
        {
            Assert.That(SelectedTabs(secondRun), Has.Count.EqualTo(1), "две подсвеченные вкладки — это и был наблюдавшийся баг");
            Assert.That(restored.IsSelected, Is.True, "активна вкладка из сессии, а не последняя открытая");
            Assert.That(lastOpened.IsSelected, Is.False);
            Assert.That(secondRun.SelectedContent, Is.SameAs(restored), "и показана на экране именно она");
            Assert.That(
                DocumentTabsCloseCoordinator.PathOf(
                    DocumentTabsCloseCoordinator.FindActive(secondRun.Children.OfType<LayoutDocument>())),
                Is.EqualTo(WorkflowB), "в сессии остаётся та же вкладка");
        });
    }
    /// <summary>Панель документов в настоящем <see cref="LayoutRoot"/> (нужен для <c>ActiveContent</c>).</summary>
    private static LayoutDocumentPane CreatePane(out LayoutRoot root)
    {
        root = new LayoutRoot();
        return root.RootPanel.Children.OfType<LayoutDocumentPane>().First();
    }

    /// <summary>Открытие вкладки дизайнера — как <c>DocGroupHelper.AddDocument</c>.</summary>
    private static LayoutDocument AddDesignerTab(LayoutDocumentPane pane, string path)
    {
        LayoutDocument tab = new LayoutDocument { Content = new Border { Tag = path } };
        pane.Children.Add(tab);
        tab.IsSelected = true;
        return tab;
    }

    /// <summary>Открытие вкладки редактора кода/XAML — как <c>MainView.AddCodeDocumentTab</c>.</summary>
    private static LayoutDocument AddCodeTab(LayoutDocumentPane pane, string path)
    {
        LayoutDocument tab = new LayoutDocument { Content = new Border(), ContentId = "code:" + path };
        pane.Children.Add(tab);
        tab.IsSelected = true;
        tab.IsActive = true;
        return tab;
    }

    private static List<LayoutDocument> SelectedTabs(LayoutDocumentPane pane)
    {
        return pane.Children.OfType<LayoutDocument>().Where(document => document.IsSelected).ToList();
    }
}
