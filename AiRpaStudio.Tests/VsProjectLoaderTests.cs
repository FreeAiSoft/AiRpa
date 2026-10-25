using System;
using System.IO;
using System.Linq;
using RPA.Enums;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

public class VsProjectLoaderTests
{
    private const string SampleSln =
        "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
        "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"MyLib\", \"MyLib\\MyLib.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
        "EndProject\r\n";

    private const string SampleCsproj =
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <AssemblyName>MyLib</AssemblyName>\r\n" +
        "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>\r\n" +
        "  </PropertyGroup>\r\n" +
        "  <ItemGroup>\r\n" +
        "    <XamlAppDef Include=\"Activity1.xaml\" />\r\n" +
        "  </ItemGroup>\r\n" +
        "</Project>";

    [Test]
    public void LoadSolution_BuildsSolutionWithProjectNode()
    {
        string root = Path.Combine(Path.GetTempPath(), "vs_load_" + Guid.NewGuid().ToString("N"));
        string sln = Path.Combine(root, "Sample.sln");
        string projDir = Path.Combine(root, "MyLib");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(sln, SampleSln);
        File.WriteAllText(Path.Combine(projDir, "MyLib.csproj"), SampleCsproj);
        File.WriteAllText(Path.Combine(projDir, "Activity1.xaml"), "<Activity/>");
        try
        {
            var node = VsProjectLoader.LoadSolution(sln);

            Assert.That(node.FSNodeType, Is.EqualTo(FSNodeType.Solution));
            Assert.That(node.Name, Is.EqualTo("Sample"));
            Assert.That(node.Childs.Count, Is.EqualTo(1));

            var project = node.Childs[0];
            Assert.That(project.FSNodeType, Is.EqualTo(FSNodeType.VsProject));
            Assert.That(project.Name, Is.EqualTo("MyLib"));
            Assert.That(project.Childs.Any(c => c.FSNodeType == FSNodeType.File && c.Name == "Activity1.xaml"), Is.True);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void LoadProject_BuildsVsProjectNode()
    {
        string root = Path.Combine(Path.GetTempPath(), "vs_load_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string csproj = Path.Combine(root, "MyLib.csproj");
        File.WriteAllText(csproj, SampleCsproj);
        File.WriteAllText(Path.Combine(root, "Activity1.xaml"), "<Activity/>");
        try
        {
            var node = VsProjectLoader.LoadProject(csproj);

            Assert.That(node.FSNodeType, Is.EqualTo(FSNodeType.VsProject));
            Assert.That(node.Name, Is.EqualTo("MyLib"));
            Assert.That(node.Childs.Any(c => c.FSNodeType == FSNodeType.File && c.Name == "Activity1.xaml"), Is.True);
            Assert.That(node.Childs.Any(c => c.Name == "MyLib.csproj"), Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
