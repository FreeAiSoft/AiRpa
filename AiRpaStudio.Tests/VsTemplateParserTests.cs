using System;
using System.IO;
using System.Linq;
using RPA.VsTemplate;

namespace AiRpaStudio.Tests;

public class VsTemplateParserTests
{
    private static string TemplateRoot
    {
        get
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "AiRpaStudio", "VSTemplates", "ActivityLibrary2010", "ActivityLibrary.vstemplate");
                if (File.Exists(candidate))
                    return Path.Combine(dir, "AiRpaStudio", "VSTemplates");

                dir = Path.GetDirectoryName(dir);
            }

            Assert.Fail("Could not locate the VSTemplates source folder.");
            return null;
        }
    }

    private static string ProjectTemplatePath => Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate");
    private static string ItemTemplatePath => Path.Combine(TemplateRoot, "Activity", "Activity.vstemplate");

    [Test]
    public void Parse_ProjectTemplate_ReadsTypeAndData()
    {
        var template = VsTemplateParser.Parse(ProjectTemplatePath);

        Assert.That(template.Type, Is.EqualTo(VsTemplateType.Project));
        Assert.That(template.TemplateId, Is.EqualTo("Microsoft.CSharp.ActivityLibrary"));
        Assert.That(template.DefaultName, Is.EqualTo("ActivityLibrary"));
        Assert.That(template.RequiredFrameworkVersion, Is.EqualTo("4.7"));
        Assert.That(template.Version, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void Parse_ProjectTemplate_ExtractsFiles()
    {
        var template = VsTemplateParser.Parse(ProjectTemplatePath);

        Assert.That(template.Files.Count, Is.EqualTo(3));

        var projectFile = template.Files.Single(f => f.IsProjectFile);
        Assert.That(projectFile.SourceFile, Is.EqualTo("ActivityLibrary.csproj"));
        Assert.That(projectFile.ReplaceParameters, Is.True);

        var activity = template.Files.Single(f => f.SourceFile == "Activity1.xaml");
        Assert.That(activity.ReplaceParameters, Is.True);
        Assert.That(activity.TargetFolder, Is.Null);

        var assemblyInfo = template.Files.Single(f => f.SourceFile == "AssemblyInfo.cs");
        Assert.That(assemblyInfo.TargetFolder, Is.EqualTo("Properties"));
    }

    [Test]
    public void Parse_ItemTemplate_ReadsTypeReferencesAndFiles()
    {
        var template = VsTemplateParser.Parse(ItemTemplatePath);

        Assert.That(template.Type, Is.EqualTo(VsTemplateType.Item));
        Assert.That(template.TemplateId, Is.EqualTo("Microsoft.CSharp.Activity"));
        Assert.That(template.References.Count, Is.EqualTo(10));
        Assert.That(template.References, Does.Contain("System.Activities"));

        Assert.That(template.Files.Count, Is.EqualTo(1));
        var file = template.Files[0];
        Assert.That(file.SourceFile, Is.EqualTo("Activity.xaml"));
        Assert.That(file.TargetFileName, Is.EqualTo("$fileinputname$.xaml"));
        Assert.That(file.ReplaceParameters, Is.True);
    }

    [Test]
    public void Parse_ItemTemplate_IgnoresWizardExtension()
    {
        // WizardExtension must not be executed and must not break parsing.
        var template = VsTemplateParser.Parse(ItemTemplatePath);

        Assert.That(template.Files.Count, Is.EqualTo(1));
        Assert.That(template.References.Count, Is.EqualTo(10));
    }

    [Test]
    public void Parse_NonVstemplate_Throws()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vs_parse_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "bad.vstemplate");
        File.WriteAllText(path, "<NotATemplate />");
        try
        {
            Assert.Throws<InvalidDataException>(() => VsTemplateParser.Parse(path));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
