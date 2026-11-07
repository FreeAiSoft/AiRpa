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
/// Закрытие вкладки в панели документов (баг «при закрытии открывается не та вкладка»): соседнюю
/// вкладку выбирает <see cref="DocumentTabsCloseCoordinator"/> по правилу <see cref="DocumentTabPolicy"/>.
/// <para>
/// Здесь настоящие модели AvalonDock (<see cref="LayoutDocumentPane"/>, <see cref="LayoutDocument"/>) без
/// окна: закрытие вкладки — <c>LayoutDocument.Close()</c>, то есть ровно тот путь, которым идёт кнопка
/// «×» на вкладке. В панели лежат вкладки обоих видов: дизайнер (путь к файлу в <c>Tag</c>) и редактор
/// кода (<c>ContentId</c> = <c>code:…</c>).
/// </para>
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class DocumentTabsCloseTests
{
    private const string WorkflowA = @"C:\project\A.xaml";
    private const string WorkflowB = @"C:\project\B.xaml";
    private const string WorkflowC = @"C:\project\C.xaml";
    private const string WorkflowD = @"C:\project\D.xaml";
    private const string CodeB = @"C:\project\B.cs";

    [Test]
    public void CloseMiddleTab_ActivatesRightNeighbour()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);
        Select(b);

        b.Close();
        LayoutDocument activated = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.SameAs(c), "после закрытия средней вкладки активной становится правая соседняя");
            Assert.That(c.IsSelected, Is.True, "видимая вкладка — соседняя справа");
            Assert.That(a.IsSelected, Is.False, "первая вкладка не должна становиться активной");
        });
    }

    [Test]
    public void CloseFirstTab_ActivatesRightNeighbour_NotTheFirstOneAgain()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);
        LayoutDocument d = AddDesignerTab(pane, WorkflowD);
        Select(b);

        // Старое поведение (WorkflowDesigners[0]) активировало первую вкладку — вкладку A.
        b.Close();
        LayoutDocument activated = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.SameAs(c));
            Assert.That(a.IsSelected, Is.False, "активировалась не первая вкладка панели");
            Assert.That(d.IsSelected, Is.False);
        });
    }

    [Test]
    public void CloseLastTab_ActivatesLeftNeighbour()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);
        Select(c);

        c.Close();
        LayoutDocument activated = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.SameAs(b), "для последней вкладки соседняя — слева");
            Assert.That(b.IsSelected, Is.True);
            Assert.That(a.IsSelected, Is.False);
        });
    }

    [Test]
    public void CloseBackgroundTab_KeepsActiveTab()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);
        Select(b);

        c.Close();
        LayoutDocument activated = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.PendingActivation, Is.Null, "закрытие фоновой вкладки не назначает новую активную");
            Assert.That(activated, Is.Null);
            Assert.That(b.IsSelected, Is.True, "активная вкладка не меняется");
            Assert.That(a.IsSelected, Is.False);
        });
    }

    [Test]
    public void CloseSingleTab_NothingToActivate()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        Select(a);

        Assert.DoesNotThrow(() => a.Close());
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.ApplyPendingActivation(), Is.Null);
            Assert.That(pane.Children, Is.Empty);
        });
    }

    [Test]
    public void CloseCodeTab_ActivatesNeighbourRightAway()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument code = AddCodeTab(pane, CodeB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);
        Select(code);

        code.Close();

        // Вкладку кода модель закрытия не переставляет — соседа активирует сама панель документов.
        Assert.Multiple(() =>
        {
            Assert.That(coordinator.PendingActivation, Is.Null, "сосед уже активирован, ждать больше нечего");
            Assert.That(c.IsSelected, Is.True, "активной стала соседняя вкладка справа");
            Assert.That(a.IsSelected, Is.False);
        });
    }

    [Test]
    public void CloseDesignerTab_NeighbourMayBeACodeTab()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument code = AddCodeTab(pane, CodeB);
        Select(a);

        a.Close();
        LayoutDocument activated = coordinator.ApplyPendingActivation();

        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.SameAs(code), "сосед считается по фактическому порядку панели, а не по коллекции дизайнеров");
            Assert.That(code.IsSelected, Is.True);
        });
    }

    [Test]
    public void NeighbourClosedBeforeActivation_DoesNotThrow()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        LayoutDocument c = AddDesignerTab(pane, WorkflowC);
        Select(b);

        b.Close();
        c.Close();   // сосед тоже закрыли, пока активация не применилась
        LayoutDocument activated = null;

        Assert.DoesNotThrow(() => activated = coordinator.ApplyPendingActivation());
        Assert.Multiple(() =>
        {
            Assert.That(activated, Is.Null);
            Assert.That(coordinator.PendingActivation, Is.Null, "закрытого соседа не активируем");
            Assert.That(pane.Children, Has.Count.EqualTo(1));
            Assert.That(a.IsSelected, Is.True, "панель сама оставляет выбранной единственную оставшуюся вкладку");
        });
    }

    [Test]
    public void CloseTab_AfterPaneReset_DoesNotThrow()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);
        Select(b);

        b.Close();
        pane.Children.Clear();   // LoadProject: панель документов сбрасывается

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.PendingActivation, Is.Null);
            Assert.That(coordinator.ApplyPendingActivation(), Is.Null);
            Assert.That(pane.SelectedContent, Is.Null, "после сброса панели документов показывать нечего");
        });
    }

    [Test]
    public void PathOf_ReturnsPathsForBothTabKinds()
    {
        LayoutDocument designerTab = DesignerTab(WorkflowA);
        LayoutDocument codeTab = CodeTab(CodeB);
        LayoutDocument anonymousTab = new LayoutDocument { Content = new Border() };

        Assert.Multiple(() =>
        {
            Assert.That(DocumentTabsCloseCoordinator.PathOf(designerTab), Is.EqualTo(WorkflowA),
                "путь вкладки дизайнера — Tag поверхности");
            Assert.That(DocumentTabsCloseCoordinator.PathOf(codeTab), Is.EqualTo(CodeB),
                "путь вкладки редактора — ContentId без префикса code:");
            Assert.That(DocumentTabsCloseCoordinator.PathOf(anonymousTab), Is.Null,
                "вкладка без файла не даёт путь");
            Assert.That(DocumentTabsCloseCoordinator.PathOf(null), Is.Null);
        });
    }

    [Test]
    public void FindActive_PrefersSelectedTab()
    {
        LayoutDocument a = DesignerTab(WorkflowA);
        LayoutDocument b = DesignerTab(WorkflowB);
        LayoutDocument c = DesignerTab(WorkflowC);
        List<LayoutDocument> tabs = new List<LayoutDocument> { a, b, c };

        Assert.Multiple(() =>
        {
            Assert.That(DocumentTabsCloseCoordinator.FindActive(tabs), Is.Null, "ни одна вкладка не выбрана — активной нет");

            b.IsSelected = true;
            Assert.That(DocumentTabsCloseCoordinator.FindActive(tabs), Is.SameAs(b));

            b.IsSelected = false;
            c.IsActive = true;
            Assert.That(DocumentTabsCloseCoordinator.FindActive(tabs), Is.SameAs(c),
                "активный документ раскладки тоже считается активной вкладкой");
        });
    }

    [Test]
    public void IsCodeTab_DistinguishesCodeFromDesignerTabs()
    {
        LayoutDocument designerTab = DesignerTab(WorkflowA);
        LayoutDocument codeTab = CodeTab(CodeB);

        Assert.Multiple(() =>
        {
            Assert.That(DocumentTabsCloseCoordinator.IsCodeTab(codeTab), Is.True);
            Assert.That(DocumentTabsCloseCoordinator.IsCodeTab(designerTab), Is.False);
            Assert.That(DocumentTabsCloseCoordinator.IsCodeTab(null), Is.False);
        });
    }

    [Test]
    public void IsActive_DetectsSelectedAndActiveTabs()
    {
        LayoutDocument tab = DesignerTab(WorkflowA);

        Assert.Multiple(() =>
        {
            Assert.That(DocumentTabsCloseCoordinator.IsActive(tab), Is.False);
            Assert.That(DocumentTabsCloseCoordinator.IsActive(null), Is.False);

            tab.IsSelected = true;
            Assert.That(DocumentTabsCloseCoordinator.IsActive(tab), Is.True);
        });
    }

    [Test]
    public void Coordinator_AttachesToTabsAlreadyInPane()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);

        // Координатор создаётся в MainView_Loaded, вкладки могут быть уже восстановлены из сессии.
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        Select(a);
        a.Close();

        Assert.That(coordinator.ApplyPendingActivation(), Is.SameAs(b),
            "вкладки, добавленные до создания координатора, тоже активируют соседа");
    }

    [Test]
    public void TabsAddedAfterPaneReset_StillActivateNeighbour()
    {
        LayoutDocumentPane pane = new LayoutDocumentPane();
        DocumentTabsCloseCoordinator coordinator = new DocumentTabsCloseCoordinator(pane);
        LayoutDocument a = AddDesignerTab(pane, WorkflowA);
        LayoutDocument b = AddDesignerTab(pane, WorkflowB);

        pane.Children.Clear();
        pane.Children.Add(a);
        pane.Children.Add(b);
        Select(a);
        a.Close();

        // Вкладки, добавленные после сброса панели (LoadProject), тоже обязаны активировать соседа.
        Assert.That(coordinator.Tabs.Select(DocumentTabsCloseCoordinator.PathOf), Is.EqualTo(new[] { WorkflowB }));
        Assert.That(coordinator.ApplyPendingActivation(), Is.SameAs(b));
    }

    private static LayoutDocument AddDesignerTab(LayoutDocumentPane pane, string filePath)
    {
        LayoutDocument tab = DesignerTab(filePath);
        pane.Children.Add(tab);
        return tab;
    }

    private static LayoutDocument AddCodeTab(LayoutDocumentPane pane, string filePath)
    {
        LayoutDocument tab = CodeTab(filePath);
        pane.Children.Add(tab);
        return tab;
    }

    private static LayoutDocument DesignerTab(string filePath)
    {
        return new LayoutDocument
        {
            Title = System.IO.Path.GetFileName(filePath),
            Content = new Border { Tag = filePath }
        };
    }

    private static LayoutDocument CodeTab(string filePath)
    {
        return new LayoutDocument
        {
            Title = System.IO.Path.GetFileName(filePath),
            Content = new Border(),
            ContentId = DocumentTabsCloseCoordinator.CodeContentIdPrefix + filePath
        };
    }

    private static void Select(LayoutDocument tab)
    {
        tab.IsActive = true;
        tab.IsSelected = true;
    }
}
