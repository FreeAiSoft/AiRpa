using System;
using System.IO;
using System.Xml.Linq;
using RPA.VsTemplate;

namespace AiRpaStudio.Tests;

public class VsTemplateInstantiatorTests
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

    private static VsTemplateInstantiationRequest CreateRequest(string outputDirectory)
    {
        return new VsTemplateInstantiationRequest
        {
            ProjectName = "MyProject",
            SafeProjectName = "MyProject",
            RootNamespace = "MyProject",
            TargetFrameworkVersion = "4.8",
            RegisteredOrganization = "Acme Corp",
            Year = "2025",
            OutputDirectory = outputDirectory
        };
    }

    [Test]
    public void Instantiate_ProjectTemplate_CreatesExpectedFiles()
    {
        string output = Path.Combine(Path.GetTempPath(), "vs_inst_" + Guid.NewGuid().ToString("N"));
        try
        {
            var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
            var result = VsTemplateInstantiator.Instantiate(template, CreateRequest(output));

            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(result.CreatedFiles.Count, Is.EqualTo(3));

            Assert.That(File.Exists(Path.Combine(output, "MyProject.csproj")), Is.True);
            Assert.That(File.Exists(Path.Combine(output, "Activity1.xaml")), Is.True);
            Assert.That(File.Exists(Path.Combine(output, "Properties", "AssemblyInfo.cs")), Is.True);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [Test]
    public void Instantiate_ProjectTemplate_ResolvesParameters()
    {
        string output = Path.Combine(Path.GetTempPath(), "vs_inst_" + Guid.NewGuid().ToString("N"));
        try
        {
            var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
            VsTemplateInstantiator.Instantiate(template, CreateRequest(output));

            string csproj = File.ReadAllText(Path.Combine(output, "MyProject.csproj"));
            Assert.That(csproj, Does.Contain("<RootNamespace>MyProject</RootNamespace>"));
            Assert.That(csproj, Does.Contain("<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>"));

            string xaml = File.ReadAllText(Path.Combine(output, "Activity1.xaml"));
            Assert.That(xaml, Does.Contain("x:Class=\"MyProject.Activity1\""));

            string assemblyInfo = File.ReadAllText(Path.Combine(output, "Properties", "AssemblyInfo.cs"));
            Assert.That(assemblyInfo, Does.Contain("[assembly: AssemblyTitle(\"MyProject\")]"));
            Assert.That(assemblyInfo, Does.Contain("[assembly: AssemblyCompany(\"Acme Corp\")]"));
            Assert.That(assemblyInfo, Does.Contain("Copyright © Acme Corp 2025"));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [Test]
    public void Instantiate_ProjectTemplate_AppliesConditionsForTarget48()
    {
        string output = Path.Combine(Path.GetTempPath(), "vs_inst_" + Guid.NewGuid().ToString("N"));
        try
        {
            var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
            VsTemplateInstantiator.Instantiate(template, CreateRequest(output));

            string xaml = File.ReadAllText(Path.Combine(output, "Activity1.xaml"));

            // For .NET 4.8 (>= 4.5) the C# expression editor markup is kept...
            Assert.That(xaml, Does.Contain("mc:Ignorable=\"sap2010\""));
            Assert.That(xaml, Does.Contain("TextExpression.NamespacesForImplementation"));
            // ...and the Visual Basic settings block (< 4.5) is removed.
            Assert.That(xaml, Does.Not.Contain("mva:VisualBasic.Settings"));
            Assert.That(xaml, Does.Not.Contain("$if$"));

            // The generated files must remain well-formed XML.
            XDocument.Parse(xaml);
            XDocument.Parse(File.ReadAllText(Path.Combine(output, "MyProject.csproj")));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [Test]
    public void Instantiate_MissingOutputDirectory_ReportsError()
    {
        var template = new VsTemplate();
        var result = VsTemplateInstantiator.Instantiate(template, new VsTemplateInstantiationRequest { OutputDirectory = null });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Does.Contain("Output directory is required."));
    }
}
