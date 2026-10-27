using RPA.VsTemplate;

namespace AiRpaStudio.Tests;

public class VsTemplateModelsTests
{
    [Test]
    public void VsTemplateType_Values()
    {
        Assert.That((int)VsTemplateType.Project, Is.EqualTo(0));
        Assert.That((int)VsTemplateType.Item, Is.EqualTo(1));
    }

    [Test]
    public void VsTemplateFile_Defaults()
    {
        var f = new VsTemplateFile();
        Assert.That(f.SourceFile, Is.Null);
        Assert.That(f.TargetFileName, Is.Null);
        Assert.That(f.ReplaceParameters, Is.False);
        Assert.That(f.OpenInEditor, Is.False);
        Assert.That(f.TargetFolder, Is.Null);
        Assert.That(f.IsProjectFile, Is.False);
    }

    [Test]
    public void VsTemplate_Defaults()
    {
        var t = new VsTemplate();
        Assert.That(t.TemplatePath, Is.Null);
        Assert.That(t.TemplateDirectory, Is.Null);
        Assert.That(t.Type, Is.EqualTo(VsTemplateType.Project));
        Assert.That(t.Version, Is.Null);
        Assert.That(t.TemplateId, Is.Null);
        Assert.That(t.ProjectType, Is.Null);
        Assert.That(t.DefaultName, Is.Null);
        Assert.That(t.Name, Is.Null);
        Assert.That(t.RequiredFrameworkVersion, Is.Null);
        Assert.That(t.Files, Is.Not.Null);
        Assert.That(t.Files, Is.Empty);
        Assert.That(t.References, Is.Not.Null);
        Assert.That(t.References, Is.Empty);
    }
}
