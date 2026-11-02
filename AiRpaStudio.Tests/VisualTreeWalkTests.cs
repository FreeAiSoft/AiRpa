using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

/// <summary>
/// Обход дерева и «не Visual» узлы.
/// <para>
/// <c>LogicalTreeHelper.GetChildren(Grid)</c> отдаёт не только элементы, но и
/// <c>RowDefinition</c>/<c>ColumnDefinition</c>. <c>VisualTreeHelper</c> на таком узле бросает
/// <c>ArgumentException</c> («…не является Visual или Visual3D»), поэтому любой визуальный обход
/// обязан пропускать не-визуальные узлы. Инцидент: загрузка активности в дизайнер падала с
/// «Не удалось загрузить в дизайнер: «System.Windows.Controls.RowDefinition» не является Visual
/// или Visual3D» — исключение приходило из поиска элемента по имени в панели свойств.
/// </para>
/// </summary>
[Apartment(ApartmentState.STA)]
public class VisualTreeWalkTests
{
    [Test]
    public void FindVisualChildren_NonVisualNode_ReturnsEmpty()
    {
        var rowDefinition = new RowDefinition();

        Assert.That(XamlHelper.FindVisualChildren<FrameworkElement>(rowDefinition), Is.Empty,
            "обход должен пропускать RowDefinition, а не бросать ArgumentException");
    }

    [Test]
    public void FindVisualParent_NonVisualNode_ReturnsNull()
    {
        var rowDefinition = new RowDefinition();

        Assert.That(XamlHelper.FindVisualParent<Grid>(rowDefinition), Is.Null,
            "VisualTreeHelper.GetParent на RowDefinition бросает исключение — нужен null");
    }

    [Test]
    public void FindVisualChild_NonVisualNode_ReturnsNull()
    {
        Assert.That(XamlHelper.FindVisualChild<Button>(new RowDefinition()), Is.Null);
    }

    [Test]
    public void PropertyWindowStyleHelper_FindByName_SkipsGridDefinitions()
    {
        // Реальный сценарий: Grid панели свойств с RowDefinitions; искомый элемент лежит в детях,
        // а до него обход доходит через LogicalTreeHelper — то есть через RowDefinition.
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var target = new Button { Name = "PART_SearchClearButton" };
        grid.Children.Add(target);

        FrameworkElement found = null;

        Assert.Multiple(() =>
        {
            Assert.That(() => found = PropertyWindowStyleHelper.FindByName<FrameworkElement>(grid, "PART_SearchClearButton"),
                Throws.Nothing, "поиск по имени не должен падать на RowDefinition из логического дерева");
            Assert.That(found, Is.SameAs(target));
        });
    }

    [Test]
    public void WfVisualTreeThemer_Apply_NonVisualRoot_DoesNotThrow()
    {
        Assert.That(() => WfVisualTreeThemer.Apply(new RowDefinition(), StudioPalette.Dark), Throws.Nothing);
    }

    [Test]
    public void TreeWalkers_UseSafeHelper()
    {
        // Приватные обходчики контролов нельзя вызвать с RowDefinition напрямую, поэтому держим
        // страж по исходникам: VisualTreeHelper.GetChildrenCount вне XamlHelper не используется.
        string[] guardedFiles =
        {
            @"AiRpaStudio\Controls\ToolboxPanel.xaml.cs",
            @"AiRpaStudio\Controls\ProjectManagerControl.xaml.cs",
            @"AiRpaStudio\Controls\TextBoxFocusBehavior.cs",
            @"AiRpaStudio\Helpers\PropertyWindowStyleHelper.cs",
            @"AiRpaStudio\Helpers\WfVisualTreeThemer.cs"
        };

        foreach (string relativePath in guardedFiles)
        {
            string source = ReadSource(relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Not.Contain("VisualTreeHelper.GetChildrenCount"),
                    $"{relativePath}: визуальный обход только через XamlHelper.FindVisualChildren");
                Assert.That(source, Does.Not.Contain("VisualTreeHelper.GetParent"),
                    $"{relativePath}: подъём по дереву только через XamlHelper.FindVisualParent");
            });
        }
    }

    private static string ReadSource(string relativePath)
    {
        string directory = TestContext.CurrentContext.TestDirectory;

        while (directory != null)
        {
            string candidate = System.IO.Path.Combine(directory, relativePath);

            if (System.IO.File.Exists(candidate))
            {
                return System.IO.File.ReadAllText(candidate);
            }

            string solution = System.IO.Path.Combine(directory, "AiRpa.sln");

            if (System.IO.File.Exists(solution))
            {
                return System.IO.File.ReadAllText(System.IO.Path.Combine(directory, relativePath));
            }

            directory = System.IO.Path.GetDirectoryName(directory);
        }

        throw new System.IO.FileNotFoundException($"не найден файл {relativePath}");
    }
}
