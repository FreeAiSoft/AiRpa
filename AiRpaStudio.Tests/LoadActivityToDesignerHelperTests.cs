using System.Windows;

using System.Windows.Controls;

using System.Windows.Controls.Primitives;

using System.Windows.Media;



using RPA.Helpers;



namespace AiRpaStudio.Tests;



/// <summary>

/// Регресс на «Не удалось загрузить в дизайнер: Последовательность не содержит соответствующий элемент»

/// при двойном клике по активности из свежего проекта.

/// <para>

/// Тогда <c>SetVariablesStyle</c> искал <c>shellBar</c> через <c>Enumerable.First</c> только по визуальному

/// дереву, а шаблон <c>DesignerView</c> к этому моменту ещё не применён: вьюха попадает в визуальное дерево

/// позже, в <c>DocGroupHelper.AddDocument</c>. Поэтому поиск обязан спокойно возвращать «не найдено», а

/// оформление — применяться по <see cref="FrameworkElement.Loaded"/>.

/// </para>

/// </summary>

[Apartment(ApartmentState.STA)]

public class LoadActivityToDesignerHelperTests

{

    [Test]

    public void TryApplyShellBarStyle_ReturnsFalse_WhenSurfaceHasNoShellBar()

    {

        Grid root = CreateSurfaceWithoutShellBar();



        bool applied = true;

        Assert.DoesNotThrow(() => applied = LoadActivityToDesignerHelper.TryApplyShellBarStyle(root));

        Assert.That(applied, Is.False, "до построения шаблона DesignerView элемента shellBar нет — это норма, а не ошибка");

    }



    [Test]

    public void TryApplyShellBarStyle_ReturnsFalse_ForNullSurface()

    {

        bool applied = true;

        Assert.DoesNotThrow(() => applied = LoadActivityToDesignerHelper.TryApplyShellBarStyle(null));

        Assert.That(applied, Is.False);

    }



    [Test]

    public void TryApplyShellBarStyle_FindsShellBar_InLogicalTreeBeforeTemplateIsApplied()

    {

        // ContentControl без применённого шаблона не имеет визуальных детей, поэтому shellBar виден

        // только через логическое дерево — обход по VisualTreeHelper его не находил.

        var host = new ContentControl { Content = new StatusBar { Name = WfDesignerTheme.ShellBarElementName } };

        var root = new Grid();

        root.Children.Add(host);



        Assert.That(VisualTreeHelper.GetChildrenCount(host), Is.EqualTo(0), "предусловие: шаблон ContentControl не применён");

        Assert.That(LoadActivityToDesignerHelper.TryApplyShellBarStyle(root), Is.True);

    }



    [Test]

    public void TryApplyShellBarStyle_UsesPaletteBackground()

    {

        var shellBar = new StatusBar { Name = WfDesignerTheme.ShellBarElementName };

        var root = new Grid();

        root.Children.Add(shellBar);



        Assert.That(LoadActivityToDesignerHelper.TryApplyShellBarStyle(root), Is.True);

        Assert.Multiple(() =>

        {

            Assert.That(ColorOf(shellBar.Background), Is.EqualTo(StudioPalette.Active.Surface), "фон shell bar обязан приходить из палитры: Brushes.Gray не следовал теме");

            Assert.That(shellBar.FontFamily.Source, Does.Contain("Roboto"));

        });

    }



    private static Color ColorOf(Brush brush)

    {

        Assert.That(brush, Is.InstanceOf<SolidColorBrush>());

        return ((SolidColorBrush)brush).Color;

    }



    private static Grid CreateSurfaceWithoutShellBar()

    {

        var root = new Grid();

        root.Children.Add(new Border { Child = new TextBlock { Text = "surface" } });

        return root;

    }

}