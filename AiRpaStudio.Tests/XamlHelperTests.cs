using System.Threading;
using System.Windows;
using System.Windows.Controls;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

[Apartment(ApartmentState.STA)]
public class XamlHelperTests
{
    [Test]
    public void FindVisualChild_NullParent_ReturnsNull()
    {
        Assert.That(XamlHelper.FindVisualChild<TextBox>(null), Is.Null);
    }

    [Test]
    public void FindVisualChild_FindsByName()
    {
        var grid = new Grid();
        var textBox = new TextBox { Name = "MyBox" };
        grid.Children.Add(textBox);
        grid.Measure(new Size(100, 100));
        grid.Arrange(new Rect(0, 0, 100, 100));

        Assert.That(XamlHelper.FindVisualChild<TextBox>(grid, "MyBox"), Is.SameAs(textBox));
    }

    [Test]
    public void FindLogicalChild_FindsByName()
    {
        var panel = new StackPanel();
        var textBox = new TextBox { Name = "MyBox" };
        panel.Children.Add(textBox);

        Assert.That(XamlHelper.FindLogicalChild<TextBox>(panel, "MyBox"), Is.SameAs(textBox));
    }

    [Test]
    public void FindVisualParent_ReturnsParent()
    {
        var grid = new Grid();
        var textBox = new TextBox();
        grid.Children.Add(textBox);

        Assert.That(XamlHelper.FindVisualParent<Grid>(textBox), Is.SameAs(grid));
    }

    [Test]
    public void FindVisualChildByTypeAndName_ReturnsElement()
    {
        var grid = new Grid();
        var textBox = new TextBox { Name = "MyBox" };
        grid.Children.Add(textBox);
        grid.Measure(new Size(100, 100));
        grid.Arrange(new Rect(0, 0, 100, 100));

        Assert.That(XamlHelper.FindVisualChildByTypeAndName(grid, "TextBox", "MyBox"), Is.SameAs(textBox));
    }
}
