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
/// Инвариант замены вкладки «код → дизайнер» и «дизайнер → код» в панели документов:
/// замена меняет <c>Content</c> (и <c>ContentId</c>) у того же <see cref="LayoutDocument"/>, а не
/// создаёт вторую вкладку — поэтому выбранной остаётся ровно одна вкладка и она же сохраняет позицию.
/// <para>
/// Здесь настоящие модели AvalonDock (<see cref="LayoutDocumentPane"/>, <see cref="LayoutDocument"/>)
/// без окна, как в <see cref="DocumentPaneSelectionTests"/>. Сама логика замены — в
/// <c>MainView.TakeReplaceableCodeTab</c>/<c>DocGroupHelper.AddDocument</c>; этот тест фиксирует
/// контракт идентификации вкладки после замены, от которого зависят <c>IsCodeTab</c>/<c>PathOf</c>.
/// </para>
/// </summary>
[TestFixture]
[Apartment(ApartmentState.STA)]
public class CodeDesignerTabReplacementTests
{
    private const string Path = @"C:\project\Main.xaml";

    [Test]
    public void ReplacingCodeTabWithDesigner_KeepsSameDocumentAndOneSelection()
    {
        LayoutDocumentPane pane = CreatePane(out _);
        LayoutDocument code = AddCodeTab(pane, Path);
        Select(code);

        ReplaceWithDesigner(code, Path);

        List<LayoutDocument> selected = SelectedTabs(pane);

        Assert.Multiple(() =>
        {
            Assert.That(pane.Children, Has.Count.EqualTo(1), "замена не создаёт вторую вкладку");
            Assert.That(pane.Children[0], Is.SameAs(code), "дизайнер показывается в той же вкладке");
            Assert.That(selected, Has.Count.EqualTo(1), "выбранной остаётся ровно одна вкладка");
            Assert.That(selected[0], Is.SameAs(code), "выбранная вкладка не меняется при замене");
            Assert.That(DocumentTabsCloseCoordinator.IsCodeTab(code), Is.False,
                "после замены вкладка больше не идентифицируется как код-вкладка");
            Assert.That(DocumentTabsCloseCoordinator.PathOf(code), Is.EqualTo(Path),
                "после замены путь вкладки берётся из Tag дизайнера");
        });
    }

    [Test]
    public void ReplacingDesignerTabWithCode_KeepsSameDocumentAndOneSelection()
    {
        LayoutDocumentPane pane = CreatePane(out _);
        LayoutDocument designer = AddDesignerTab(pane, Path);
        Select(designer);

        ReplaceWithCode(designer, Path);

        List<LayoutDocument> selected = SelectedTabs(pane);

        Assert.Multiple(() =>
        {
            Assert.That(pane.Children, Has.Count.EqualTo(1), "замена не создаёт вторую вкладку");
            Assert.That(pane.Children[0], Is.SameAs(designer), "редактор кода показывается в той же вкладке");
            Assert.That(selected, Has.Count.EqualTo(1), "выбранной остаётся ровно одна вкладка");
            Assert.That(selected[0], Is.SameAs(designer), "выбранная вкладка не меняется при замене");
            Assert.That(DocumentTabsCloseCoordinator.IsCodeTab(designer), Is.True,
                "после замены вкладка идентифицируется как код-вкладка");
            Assert.That(DocumentTabsCloseCoordinator.PathOf(designer), Is.EqualTo(Path),
                "после замены путь вкладки берётся из ContentId");
        });
    }

    private static LayoutDocumentPane CreatePane(out LayoutRoot root)
    {
        root = new LayoutRoot();
        return root.RootPanel.Children.OfType<LayoutDocumentPane>().First();
    }

    private static LayoutDocument AddDesignerTab(LayoutDocumentPane pane, string path)
    {
        LayoutDocument tab = new LayoutDocument { Content = new Border { Tag = path } };
        pane.Children.Add(tab);
        tab.IsSelected = true;
        return tab;
    }

    private static LayoutDocument AddCodeTab(LayoutDocumentPane pane, string path)
    {
        LayoutDocument tab = new LayoutDocument { Content = new Border(), ContentId = "code:" + path };
        pane.Children.Add(tab);
        tab.IsSelected = true;
        tab.IsActive = true;
        return tab;
    }

    private static void Select(LayoutDocument document)
    {
        document.IsSelected = true;
        document.IsActive = true;
    }

    private static void ReplaceWithDesigner(LayoutDocument code, string path)
    {
        code.Content = new Border { Tag = path };
        code.ContentId = null;
    }

    private static void ReplaceWithCode(LayoutDocument designer, string path)
    {
        designer.Content = new Border();
        designer.ContentId = "code:" + path;
    }

    private static List<LayoutDocument> SelectedTabs(LayoutDocumentPane pane)
    {
        return pane.Children.OfType<LayoutDocument>().Where(document => document.IsSelected).ToList();
    }
}
