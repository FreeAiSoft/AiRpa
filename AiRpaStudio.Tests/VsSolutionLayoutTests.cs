using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Agent.Core.Services;
using NUnit.Framework;
using RPA.Entities;
using RPA.Enums;
using RPA.VsProject;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-06` — открытие решения .sln: заголовок и поколение VS, папки решения и их вложенность,
/// конфигурации, проекты вне каталога решения и отсутствующие файлы (последние раньше исчезали молча).
/// </summary>
[TestFixture]
public class VsSolutionLayoutTests
{
    private const string InFolderProjectGuid = "{AAAA0000-0000-0000-0000-000000000002}";
    private const string RootProjectGuid = "{AAAA0000-0000-0000-0000-000000000003}";
    private const string MissingProjectGuid = "{AAAA0000-0000-0000-0000-000000000004}";
    private const string FolderGuid = "{AAAA0000-0000-0000-0000-000000000001}";
    private const string OutsideProjectGuid = "{AAAA0000-0000-0000-0000-000000000005}";

    private const string SolutionText =
        "\r\n" +
        "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
        "# Visual Studio Version 17\r\n" +
        "VisualStudioVersion = 17.9.34728.123\r\n" +
        "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
        "Project(\"" + VsSolutionParser.SolutionFolderTypeGuid + "\") = \"src\", \"src\", \"" + FolderGuid + "\"\r\n" +
        "EndProject\r\n" +
        "Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"LibInFolder\", \"src\\LibInFolder\\LibInFolder.csproj\", \"" + InFolderProjectGuid + "\"\r\n" +
        "EndProject\r\n" +
        "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"RootLib\", \"RootLib\\RootLib.csproj\", \"" + RootProjectGuid + "\"\r\n" +
        "EndProject\r\n" +
        "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Missing\", \"Gone\\Missing.csproj\", \"" + MissingProjectGuid + "\"\r\n" +
        "EndProject\r\n" +
        "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Outside\", \"..\\Outside\\Outside.csproj\", \"" + OutsideProjectGuid + "\"\r\n" +
        "EndProject\r\n" +
        "Global\r\n" +
        "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
        "\t\tDebug|Any CPU = Debug|Any CPU\r\n" +
        "\t\tRelease|x64 = Release|x64\r\n" +
        "\tEndGlobalSection\r\n" +
        "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
        "\t\t" + InFolderProjectGuid + ".Debug|Any CPU.ActiveCfg = Debug|x64\r\n" +
        "\t\t" + InFolderProjectGuid + ".Debug|Any CPU.Build.0 = Debug|x64\r\n" +
        "\t\t" + RootProjectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n" +
        "\tEndGlobalSection\r\n" +
        "\tGlobalSection(NestedProjects) = preSolution\r\n" +
        "\t\t" + InFolderProjectGuid + " = " + FolderGuid + "\r\n" +
        "\tEndGlobalSection\r\n" +
        "\tGlobalSection(ExtensibilityGlobals) = postSolution\r\n" +
        "\t\tVisualStudioVersion = 17.9.34728.123\r\n" +
        "\tEndGlobalSection\r\n" +
        "EndGlobal\r\n";

    private const string LibraryProject =
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <AssemblyName>Lib</AssemblyName>\r\n" +
        "    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>\r\n" +
        "  </PropertyGroup>\r\n" +
        "</Project>";

    [Test]
    public void Parse_ReadsHeaderFoldersConfigurationsAndToolchain()
    {
        using (SolutionFixture fixture = SolutionFixture.Create())
        {
            VsSolution solution = VsSolutionParser.Parse(fixture.SolutionPath);

            Assert.That(solution.FormatVersion, Is.EqualTo("12.00"));
            Assert.That(solution.VisualStudioVersion, Is.EqualTo("17"));
            Assert.That(solution.RequiredToolchain, Is.EqualTo("Visual Studio 2022"));

            Assert.That(solution.Configurations.Select(c => c.ToString()),
                Is.EquivalentTo(new[] { "Debug|Any CPU", "Release|x64" }));

            VsSolutionProject folder = solution.Projects.Single(p => p.Name == "src");
            Assert.That(folder.IsSolutionFolder, Is.True);
            Assert.That(folder.FileExists, Is.False);

            VsSolutionProject inFolder = solution.Projects.Single(p => p.Name == "LibInFolder");
            Assert.That(inFolder.IsSolutionFolder, Is.False);
            Assert.That(inFolder.ParentGuid, Is.EqualTo(FolderGuid));
            Assert.That(inFolder.FileExists, Is.True);
            Assert.That(inFolder.FullPath, Is.EqualTo(IOPath.Combine(fixture.Root, "src", "LibInFolder", "LibInFolder.csproj")));
            Assert.That(inFolder.Configurations["Debug|Any CPU"], Is.EqualTo("Debug|x64"));
        }
    }

    [Test]
    public void Parse_MissingProject_IsReportedInsteadOfDropped()
    {
        using (SolutionFixture fixture = SolutionFixture.Create())
        {
            VsSolution solution = VsSolutionParser.Parse(fixture.SolutionPath);

            VsSolutionProject missing = solution.Projects.Single(p => p.Name == "Missing");

            Assert.That(missing.FileExists, Is.False);
            Assert.That(solution.Projects.Any(p => p.Name == "Missing"), Is.True, "запись должна остаться в модели");
            Assert.That(solution.Warnings, Has.Some.Contain("файл проекта не найден"),
                "отсутствующий проект обязан попасть в предупреждения, а не исчезнуть молча (TD-12)");
        }
    }

    [Test]
    public void Parse_ProjectOutsideSolutionDirectory_IsResolved()
    {
        using (SolutionFixture fixture = SolutionFixture.Create())
        {
            VsSolution solution = VsSolutionParser.Parse(fixture.SolutionPath);

            VsSolutionProject outside = solution.Projects.Single(p => p.Name == "Outside");

            Assert.That(outside.FileExists, Is.True, "проект вне каталога решения должен разрешаться через ..");
            Assert.That(outside.FullPath, Is.EqualTo(IOPath.Combine(fixture.ParentRoot, "Outside", "Outside.csproj")));
        }
    }

    [Test]
    public void ToolchainFor_MapsSolutionGeneration()
    {
        Assert.That(VsSolutionParser.ToolchainFor("17"), Is.EqualTo(MsBuildToolchain.Vs2022));
        Assert.That(VsSolutionParser.ToolchainFor("18"), Is.EqualTo(MsBuildToolchain.Vs2026));
        Assert.That(VsSolutionParser.ToolchainFor("15"), Is.Null);
        Assert.That(VsSolutionParser.ToolchainFor(null), Is.Null);
    }

    [Test]
    public void LoadSolution_MirrorsFoldersAndKeepsMissingNode()
    {
        using (SolutionFixture fixture = SolutionFixture.Create())
        {
            FSNode root = VsProjectLoader.LoadSolution(fixture.SolutionPath, out List<string> warnings);

            Assert.That(root.FSNodeType, Is.EqualTo(FSNodeType.Solution));

            FSNode folder = root.Childs.Single(c => c.Name == "src");
            Assert.That(folder.FSNodeType, Is.EqualTo(FSNodeType.SolutionFolder));
            Assert.That(folder.Childs.Count, Is.EqualTo(1));
            Assert.That(folder.Childs[0].FSNodeType, Is.EqualTo(FSNodeType.VsProject));
            Assert.That(folder.Childs[0].Name, Is.EqualTo("Lib"));

            FSNode missing = root.Childs.Single(c => c.Name == "Missing");
            Assert.That(missing.FSNodeType, Is.EqualTo(FSNodeType.VsProject));
            Assert.That(missing.IsMissing, Is.True);

            Assert.That(warnings, Has.Some.Contain("файл проекта не найден"));
        }
    }

    [Test]
    public void Parse_RealSolution_ReadsAiRpaSlnWithoutLosingProjects()
    {
        string solutionPath = IOPath.Combine(FindRepositoryRoot(), "AiRpa.sln");

        VsSolution solution = VsSolutionParser.Parse(solutionPath);

        Assert.That(solution.FormatVersion, Is.EqualTo("12.00"));
        Assert.That(solution.RequiredToolchain, Is.EqualTo("Visual Studio 2026"),
            "AiRpa.sln сохранён VS 2026 (# Visual Studio Version 18)");
        Assert.That(solution.Projects.Count, Is.GreaterThanOrEqualTo(30));
        Assert.That(solution.Projects.Any(p => p.IsSolutionFolder), Is.True, "папки решения должны распознаваться");
        Assert.That(solution.Configurations, Is.Not.Empty);

        var csprojProjects = solution.Projects.Where(p => VsSolutionParser.IsCsproj(p)).ToList();
        Assert.That(csprojProjects, Is.Not.Empty);
        Assert.That(csprojProjects.All(p => p.FullPath != null), Is.True);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");

        return directory.FullName;
    }

    private sealed class SolutionFixture : IDisposable
    {
        private SolutionFixture(string parentRoot)
        {
            ParentRoot = parentRoot;
            Root = IOPath.Combine(parentRoot, "Solution");
            SolutionPath = IOPath.Combine(Root, "Sample.sln");
        }

        public string ParentRoot { get; }

        public string Root { get; }

        public string SolutionPath { get; }

        public static SolutionFixture Create()
        {
            string parentRoot = IOPath.Combine(IOPath.GetTempPath(), "vs_sln_layout_" + Guid.NewGuid().ToString("N"));
            var fixture = new SolutionFixture(parentRoot);

            Directory.CreateDirectory(IOPath.Combine(fixture.Root, "src", "LibInFolder"));
            Directory.CreateDirectory(IOPath.Combine(fixture.Root, "RootLib"));
            Directory.CreateDirectory(IOPath.Combine(parentRoot, "Outside"));

            File.WriteAllText(fixture.SolutionPath, SolutionText);
            File.WriteAllText(IOPath.Combine(fixture.Root, "src", "LibInFolder", "LibInFolder.csproj"), LibraryProject);
            File.WriteAllText(IOPath.Combine(fixture.Root, "RootLib", "RootLib.csproj"), LibraryProject);
            File.WriteAllText(IOPath.Combine(parentRoot, "Outside", "Outside.csproj"), LibraryProject);

            return fixture;
        }

        public void Dispose()
        {
            if (Directory.Exists(ParentRoot))
            {
                Directory.Delete(ParentRoot, true);
            }
        }
    }
}
