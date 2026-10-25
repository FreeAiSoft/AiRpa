using System;
using System.IO;
using System.Linq;
using RPA.Enums;
using RPA.VsProject;
using RPA.VsTemplate;

namespace AiRpaStudio.Tests;

public class VsProjectBuilderTests
{
    [Test]
    public void BuildArguments_ContainsPathAndConfiguration()
    {
        string args = VsProjectBuilder.BuildArguments(@"C:\dir\MyLib.csproj", "Release");

        Assert.That(args, Does.Contain("\"C:\\dir\\MyLib.csproj\""));
        Assert.That(args, Does.Contain("/p:Configuration=Release"));
    }

    [Test]
    public void BuildArguments_DefaultsToDebug()
    {
        string args = VsProjectBuilder.BuildArguments(@"C:\dir\MyLib.csproj", null);

        Assert.That(args, Does.Contain("/p:Configuration=Debug"));
    }

    [Test]
    public void FindMSBuild_ReturnsNonEmpty()
    {
        Assert.That(VsProjectBuilder.FindMSBuild(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Build_MissingPath_ReportsError()
    {
        var result = VsProjectBuilder.Build(@"C:\no\such\project.csproj");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Some.Contain("not found"));
    }

    [Test]
    public void Build_GeneratedProject_ProducesResult()
    {
        string output = Path.Combine(Path.GetTempPath(), "vs_build_" + Guid.NewGuid().ToString("N"));
        try
        {
            var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
            var request = new VsTemplateInstantiationRequest
            {
                ProjectName = "BuildTest",
                SafeProjectName = "BuildTest",
                RootNamespace = "BuildTest",
                TargetFrameworkVersion = "4.8",
                OutputDirectory = output
            };
            VsTemplateInstantiator.Instantiate(template, request);

            string csproj = Path.Combine(output, "BuildTest.csproj");
            var result = VsProjectBuilder.Build(csproj, "Debug");

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Output, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Success, Is.EqualTo(result.ExitCode == 0));

            if (result.Success)
            {
                Assert.That(File.Exists(Path.Combine(output, "bin", "Debug", "BuildTest.dll")), Is.True);
            }
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [Test]
    public void EndToEnd_GenerateLoadBuild()
    {
        string output = Path.Combine(Path.GetTempPath(), "vs_e2e_" + Guid.NewGuid().ToString("N"));
        try
        {
            var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
            var request = new VsTemplateInstantiationRequest
            {
                ProjectName = "E2E",
                SafeProjectName = "E2E",
                RootNamespace = "E2E",
                TargetFrameworkVersion = "4.8",
                OutputDirectory = output
            };
            VsTemplateInstantiator.Instantiate(template, request);

            string csproj = Path.Combine(output, "E2E.csproj");

            // Phase 4: load into the file tree.
            var node = VsProjectLoader.LoadProject(csproj);
            Assert.That(node.FSNodeType, Is.EqualTo(FSNodeType.VsProject));
            Assert.That(node.Childs.Any(c => c.FSNodeType == FSNodeType.File && c.Name == "Activity1.xaml"), Is.True);

            // Phase 5: build via MSBuild.
            var result = VsProjectBuilder.Build(csproj, "Debug");
            Assert.That(result.Output, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Success, Is.EqualTo(result.ExitCode == 0));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

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
}
