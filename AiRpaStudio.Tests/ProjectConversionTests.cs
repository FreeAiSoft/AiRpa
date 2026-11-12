using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using RPA.ProjectModel;
using RPA.Entities;
using RPA.Enums;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

/// <summary>
/// Программная конвертация единого объекта <see cref="Project"/> между встроенным (project.json)
/// и Visual Studio (.csproj/.sln) форматом. VS-проект всегда является activity library.
/// </summary>
public class ProjectConversionTests
{
    private static readonly XNamespace MsBuildNs = "http://schemas.microsoft.com/developer/msbuild/2003";

    [Test]
    public void ConvertToVsProject_CreatesLegacyWfProject()
    {
        string dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "Main.xaml"), "<Activity />");
        File.WriteAllText(Path.Combine(dir, "Activity1.xaml"), "<Activity />");
        File.WriteAllText(Path.Combine(dir, "Helper.cs"), "class Helper {}");
        File.WriteAllText(Path.Combine(dir, "app.config"), "<?xml version=\"1.0\"?>");

        var project = new Project(ProjectType.Process) { Name = "MyRobot", MainActivity = "Main.xaml" };
        project.SetProjectDir(dir);

        var result = ProjectConversionService.ConvertToVsProject(project);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(File.Exists(Path.Combine(dir, "MyRobot.csproj")), Is.True, "должен создаваться legacy .csproj");
            Assert.That(File.Exists(Path.Combine(dir, "MyRobot.sln")), Is.True, "по умолчанию генерируется .sln");
            Assert.That(File.Exists(Path.Combine(dir, "Properties", "AssemblyInfo.cs")), Is.True);
            Assert.That(project.Format, Is.EqualTo(ProjectFormat.VsProject));
            Assert.That(project.ProjectType, Is.EqualTo(ProjectType.Library));
            Assert.That(project.TargetFrameworkVersion, Is.EqualTo("v4.7.2"));
        });

        XDocument doc = XDocument.Load(Path.Combine(dir, "MyRobot.csproj"));
        XElement root = doc.Root;

        Assert.Multiple(() =>
        {
            Assert.That(root.Descendants(MsBuildNs + "OutputType").FirstOrDefault()?.Value, Is.EqualTo("Library"));
            Assert.That(root.Descendants(MsBuildNs + "TargetFrameworkVersion").FirstOrDefault()?.Value, Is.EqualTo("v4.7.2"));
            Assert.That(root.Descendants(MsBuildNs + "ProjectTypeGuids").FirstOrDefault()?.Value, Does.Contain("32f31d43-81cc-4c15-9de6-3fc5453562b6"),
                "проект должен быть WF activity library");

            string[] xaml = root.Descendants(MsBuildNs + "XamlAppDef").Select(e => (string)e.Attribute("Include")).ToArray();
            Assert.That(xaml, Does.Contain("Main.xaml"));
            Assert.That(xaml, Does.Contain("Activity1.xaml"));

            string[] compile = root.Descendants(MsBuildNs + "Compile").Select(e => (string)e.Attribute("Include")).ToArray();
            Assert.That(compile, Does.Contain("Helper.cs"));
            Assert.That(compile, Does.Contain(@"Properties\AssemblyInfo.cs"));
        });
    }

    [Test]
    public void ConvertToVsProject_NativeProcess_NormalizesToLibrary()
    {
        string dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "Main.xaml"), "<Activity />");

        var project = new Project(ProjectType.Process) { Name = "MyProcess", MainActivity = "Main.xaml" };
        project.SetProjectDir(dir);

        var result = ProjectConversionService.ConvertToVsProject(project);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(project.ProjectType, Is.EqualTo(ProjectType.Library),
                "VS-проект всегда activity library — Process нормализуется в Library");
        });
    }

    [Test]
    public void ConvertToNative_ProjectWithMainXaml_IsLibrary()
    {
        string dir = CreateTempDir();
        string csproj = Path.Combine(dir, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj("<XamlAppDef Include=\"Main.xaml\" />"));

        var project = ProjectMapper.FromVsProjectInfo(VsProjectParser.Parse(csproj));
        project.SetProjectDir(dir);

        var result = ProjectConversionService.ConvertToNative(project);

        Project round = JsonConvert.DeserializeObject<Project>(File.ReadAllText(Path.Combine(dir, "project.json")));
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(round.ProjectType, Is.EqualTo(ProjectType.Library));
            Assert.That(round.MainActivity, Is.EqualTo("Main.xaml"));
        });
    }

    [Test]
    public void ConvertToNative_ProjectWithoutMainXaml_IsLibrary()
    {
        string dir = CreateTempDir();
        string csproj = Path.Combine(dir, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj("<XamlAppDef Include=\"Activity1.xaml\" />"));

        var project = ProjectMapper.FromVsProjectInfo(VsProjectParser.Parse(csproj));
        project.SetProjectDir(dir);

        var result = ProjectConversionService.ConvertToNative(project);

        Project round = JsonConvert.DeserializeObject<Project>(File.ReadAllText(Path.Combine(dir, "project.json")));
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Errors));
            Assert.That(round.ProjectType, Is.EqualTo(ProjectType.Library),
                "эвристики «есть Main.xaml → Process» нет: VS-проект всегда Library");
            Assert.That(round.MainActivity, Is.EqualTo("Activity1.xaml"));
        });
    }

    [Test]
    public void RoundTrip_NativeToVsToNative_PreservesNameMainActivityAndTfm()
    {
        string dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "Main.xaml"), "<Activity />");

        var project = new Project(ProjectType.Process) { Name = "MyRobot", MainActivity = "Main.xaml" };
        project.SetProjectDir(dir);

        Assert.That(ProjectConversionService.ConvertToVsProject(project).Success, Is.True);
        Assert.That(ProjectConversionService.ConvertToNative(project).Success, Is.True);

        Project round = JsonConvert.DeserializeObject<Project>(File.ReadAllText(Path.Combine(dir, "project.json")));

        Assert.Multiple(() =>
        {
            Assert.That(round.Name, Is.EqualTo("MyRobot"));
            Assert.That(round.MainActivity, Is.EqualTo("Main.xaml"));
            Assert.That(round.TargetFrameworkVersion, Is.EqualTo("v4.7.2"));
            Assert.That(round.ProjectType, Is.EqualTo(ProjectType.Library),
                "round-trip намеренно нормализует Process → Library (VS всегда library)");
        });
    }

    [Test]
    public void ConvertToVsProject_IsIdempotent()
    {
        string dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "Main.xaml"), "<Activity />");

        var project = new Project(ProjectType.Process) { Name = "MyRobot", MainActivity = "Main.xaml" };
        project.SetProjectDir(dir);

        var first = ProjectConversionService.ConvertToVsProject(project);
        var second = ProjectConversionService.ConvertToVsProject(project);

        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True, string.Join(Environment.NewLine, first.Errors));
            Assert.That(second.Success, Is.True, string.Join(Environment.NewLine, second.Errors));
        });

        XDocument doc = XDocument.Load(Path.Combine(dir, "MyRobot.csproj"));
        Assert.That(doc.Descendants(MsBuildNs + "XamlAppDef").Count(), Is.EqualTo(1),
            "повторная конвертация не должна дублировать элементы");
    }

    [Test]
    public void ConvertToVsProject_MissingFolder_Fails()
    {
        var project = new Project(ProjectType.Process) { Name = "MyRobot" };
        project.SetProjectDir(Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N")));

        var result = ProjectConversionService.ConvertToVsProject(project);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);
    }

    [Test]
    public void ConvertToVsProject_GeneratedProject_Builds()
    {
        string dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.xaml"), ValidWorkflowXaml("MyRobot.Main"));

            var project = new Project(ProjectType.Process) { Name = "MyRobot", MainActivity = "Main.xaml" };
            project.SetProjectDir(dir);

            var conversion = ProjectConversionService.ConvertToVsProject(project);
            Assert.That(conversion.Success, Is.True, string.Join(Environment.NewLine, conversion.Errors));

            var build = VsProjectBuilder.Build(conversion.ProjectFilePath, "Debug");
            Assert.That(build.Success, Is.True, "Сгенерированный .csproj должен собираться MSBuild: " + build.Output);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    private static string ValidWorkflowXaml(string xClass)
    {
        return
            "<Activity x:Class=\"" + xClass + "\"" +
            " xmlns=\"http://schemas.microsoft.com/netfx/2009/xaml/activities\"" +
            " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"" +
            " xmlns:sap2010=\"http://schemas.microsoft.com/netfx/2010/xaml/activities/presentation\"" +
            " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
            " mc:Ignorable=\"sap2010\"" +
            " sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\">" +
            "</Activity>";
    }

    private static string LegacyCsproj(string itemsXml)
    {
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
               "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
               "<PropertyGroup><AssemblyName>MyLib</AssemblyName><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup>" +
               "<ItemGroup>" + itemsXml + "</ItemGroup>" +
               "</Project>";
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "projconv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
