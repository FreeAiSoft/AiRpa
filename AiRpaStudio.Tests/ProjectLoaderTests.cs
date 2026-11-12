using System;
using System.IO;
using System.Linq;
using RPA.Entities;
using RPA.Enums;
using RPA.ProjectModel;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

/// <summary>
/// Единая загрузка проекта/дерева из любого источника: папка с project.json, .csproj или .sln.
/// Нативный и VS-источники обязаны давать один и тот же доменный объект <see cref="Project"/>.
/// </summary>
public class ProjectLoaderTests
{
    private const string LegacyCsproj =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <AssemblyName>MyLib</AssemblyName>\r\n" +
        "    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>\r\n" +
        "    <ProjectTypeGuids>{32f31d43-81cc-4c15-9de6-3fc5453562b6};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>\r\n" +
        "  </PropertyGroup>\r\n" +
        "  <ItemGroup>\r\n" +
        "    <XamlAppDef Include=\"Main.xaml\"><SubType>Designer</SubType></XamlAppDef>\r\n" +
        "  </ItemGroup>\r\n" +
        "</Project>\r\n";

    [Test]
    public void Load_NativeProjectDir_ReturnsNativeProjectAndTree()
    {
        string dir = CreateTempDir();
        File.WriteAllText(Path.Combine(dir, "project.json"),
            "{\"Name\":\"MyBot\",\"MainActivity\":\"Main.xaml\",\"ProjectType\":0}");

        var result = ProjectLoader.Load(dir);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Project, Is.Not.Null);
            Assert.That(result.Project.Format, Is.EqualTo(ProjectFormat.Native));
            Assert.That(result.Project.Name, Is.EqualTo("MyBot"));
            Assert.That(result.Project.ProjectDirPath, Is.EqualTo(dir));
            Assert.That(result.Root, Is.Not.Null);
            Assert.That(result.Root.FSNodeType, Is.EqualTo(FSNodeType.Project));
            Assert.That(result.Root.Project, Is.SameAs(result.Project));
        });
    }

    [Test]
    public void Load_Csproj_ReturnsVsProjectWithLinkedRoot()
    {
        string dir = CreateTempDir();
        string csproj = Path.Combine(dir, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        var result = ProjectLoader.Load(csproj);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Project, Is.Not.Null);
            Assert.That(result.Project.Format, Is.EqualTo(ProjectFormat.VsProject));
            Assert.That(result.Project.Name, Is.EqualTo("MyLib"));
            Assert.That(result.Project.ProjectType, Is.EqualTo(ProjectType.Library));
            Assert.That(result.Project.MainActivity, Is.EqualTo("Main.xaml"));
            Assert.That(result.Root, Is.Not.Null);
            Assert.That(result.Root.FSNodeType, Is.EqualTo(FSNodeType.VsProject));
            Assert.That(result.Root.Project, Is.SameAs(result.Project));
        });
    }

    [Test]
    public void Load_Sln_ReturnsSolutionWorkspaceWithProjects()
    {
        string dir = CreateTempDir();
        string csproj = Path.Combine(dir, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);
        string sln = Path.Combine(dir, "Sample.sln");
        File.WriteAllText(sln, SolutionWithSingleProject("MyLib", "MyLib.csproj", "{11111111-1111-1111-1111-111111111111}"));

        var result = ProjectLoader.Load(sln);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Solution, Is.Not.Null);
            Assert.That(result.Solution.Projects, Has.Count.EqualTo(1));
            Assert.That(result.Solution.Projects[0].Name, Is.EqualTo("MyLib"));
            Assert.That(result.Solution.Projects[0].Format, Is.EqualTo(ProjectFormat.VsProject));
            Assert.That(result.Root.FSNodeType, Is.EqualTo(FSNodeType.Solution));
        });
    }

    [Test]
    public void Load_MissingPath_ReturnsError()
    {
        string missing = Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N"));
        var result = ProjectLoader.Load(missing);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    private static string SolutionWithSingleProject(string projectName, string csprojRelativePath, string projectGuid)
    {
        return "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
               "# Visual Studio Version 17\r\n" +
               "VisualStudioVersion = 17.0.31903.59\r\n" +
               "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
               "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"" + projectName + "\", \"" + csprojRelativePath + "\", \"" + projectGuid + "\"\r\n" +
               "EndProject\r\n" +
               "Global\r\n" +
               "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
               "\t\tDebug|Any CPU = Debug|Any CPU\r\n" +
               "\tEndGlobalSection\r\n" +
               "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
               "\t\t" + projectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n" +
               "\t\t" + projectGuid + ".Debug|Any CPU.Build.0 = Debug|Any CPU\r\n" +
               "\tEndGlobalSection\r\n" +
               "EndGlobal\r\n";
    }

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "projload_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
