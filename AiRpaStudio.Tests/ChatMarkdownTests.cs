using System.Threading;
using System.Windows.Controls;
using RPA.Chat.Markdown;

namespace AiRpaStudio.Tests;

public class ChatMarkdownTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_PlainText_ReturnsPanelWithContent()
    {
        var element = MarkdownRenderer.Render("Hello world");
        Assert.That(element, Is.InstanceOf<StackPanel>());
        Assert.That(((StackPanel)element).Children.Count, Is.GreaterThan(0));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_CodeBlock_ReturnsPanelWithContent()
    {
        var element = MarkdownRenderer.Render("```csharp\nint x = 1;\n```");
        Assert.That(element, Is.InstanceOf<StackPanel>());
        Assert.That(((StackPanel)element).Children.Count, Is.GreaterThan(0));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Empty_ReturnsEmptyPanel()
    {
        var element = MarkdownRenderer.Render("");
        Assert.That(element, Is.InstanceOf<StackPanel>());
        Assert.That(((StackPanel)element).Children.Count, Is.EqualTo(0));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Render_Null_ReturnsEmptyPanel()
    {
        var element = MarkdownRenderer.Render(null);
        Assert.That(element, Is.InstanceOf<StackPanel>());
    }
}
