using System.Threading;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

[Apartment(ApartmentState.STA)]
public class CodeLanguageProviderTests
{
    [Test]
    public void GetLanguageForExtension_Xaml_ReturnsXaml()
    {
        Assert.That(CodeLanguageProvider.GetLanguageForExtension("MainWindow.xaml"), Is.EqualTo(CodeLanguageProvider.XamlLanguage));
    }

    [Test]
    public void GetLanguageForExtension_CSharp_ReturnsCSharp()
    {
        Assert.That(CodeLanguageProvider.GetLanguageForExtension("Program.cs"), Is.EqualTo("C#"));
    }

    [Test]
    public void GetLanguageForExtension_Json_ReturnsJson()
    {
        Assert.That(CodeLanguageProvider.GetLanguageForExtension("appsettings.json"), Is.EqualTo("Json"));
    }

    [Test]
    public void GetLanguageForExtension_Unknown_ReturnsNull()
    {
        Assert.That(CodeLanguageProvider.GetLanguageForExtension("image.png"), Is.Null);
    }

    [Test]
    public void GetXamlDefinition_LoadsThemedAndCached()
    {
        IHighlightingDefinition definition = CodeLanguageProvider.GetXamlDefinition();

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.Name, Is.EqualTo("XAML"));
        Assert.That(CodeLanguageProvider.GetXamlDefinition(), Is.SameAs(definition), "определение кэшируется на язык и тему");
    }

    [Test]
    public void CreateFoldingStrategy_Xaml_ReturnsXmlFoldingStrategy()
    {
        Assert.That(CodeLanguageProvider.CreateFoldingStrategy("XAML"), Is.InstanceOf<XmlFoldingStrategy>());
    }

    [Test]
    public void CreateFoldingStrategy_Unsupported_ReturnsNull()
    {
        Assert.That(CodeLanguageProvider.CreateFoldingStrategy("C#"), Is.Null);
        Assert.That(CodeLanguageProvider.CreateFoldingStrategy(null), Is.Null);
    }
}
