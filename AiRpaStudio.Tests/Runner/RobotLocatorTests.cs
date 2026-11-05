using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using RPA.Runner;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Runner;

/// <summary>
/// `OR-02`, шаг `W3` — локатор робота: раннер ничего не собирает, он находит уже собранное.
/// <para>
/// Проверяются четыре раскладки <c>bin</c>, которые реально встречаются (студия — <c>bin\x64\Debug</c>,
/// SDK-style робот — <c>bin\Debug\net472</c>, legacy — <c>bin\Debug</c>), правило «несколько сборок —
/// берём свежую, остальные называем» и главное: если сборки нет, текст ошибки обязан перечислять
/// каталоги поиска и говорить «соберите проект», а не «не удалось загрузить workflow».
/// </para>
/// </summary>
[TestFixture]
public class RobotLocatorTests
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = IOPath.Combine(IOPath.GetTempPath(), "runner_locator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Resolve_LegacyLayout_FindsBinDebugAssembly()
    {
        string project = CreateRobot(legacy: true);
        CreateAssembly(project, "bin", "Debug");

        Assert.That(RobotLocator.TryResolve(Parse("--project", Csproj(project)), out RobotLocation location, out string error),
            Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(location.SourceKind, Is.EqualTo("project"));
            Assert.That(location.AssemblyPath, Does.EndWith(IOPath.Combine("bin", "Debug", "Robot.dll")));
            Assert.That(location.TypeName, Is.EqualTo("Robot.Main"), "тип берётся из x:Class сценария");
            Assert.That(location.WorkflowPath, Does.EndWith("Main.xaml"));
            Assert.That(location.BinDirectory, Does.EndWith(IOPath.Combine("bin", "Debug")),
                "зависимости робота ищутся рядом с собранной сборкой");
        });
    }

    [Test]
    public void Resolve_SdkStyleLayout_FindsTfmFolder()
    {
        string project = CreateRobot(legacy: false, framework: "net472");
        CreateAssembly(project, "bin", "Debug", "net472");

        Assert.That(RobotLocator.TryResolve(Parse("--project", Csproj(project)), out RobotLocation location, out string error),
            Is.True, error);

        Assert.That(location.AssemblyPath, Does.EndWith(IOPath.Combine("bin", "Debug", "net472", "Robot.dll")));
    }

    [Test]
    public void Resolve_StudioLayout_FindsPlatformFolder()
    {
        string project = CreateRobot(legacy: true);
        CreateAssembly(project, "bin", "x64", "Debug");

        Assert.That(RobotLocator.TryResolve(Parse("--project", Csproj(project)), out RobotLocation location, out string error),
            Is.True, error);

        Assert.That(location.AssemblyPath, Does.EndWith(IOPath.Combine("bin", "x64", "Debug", "Robot.dll")));
    }

    [Test]
    public void Resolve_NotBuilt_NamesSearchedDirectoriesAndHint()
    {
        string project = CreateRobot(legacy: true);

        bool resolved = RobotLocator.TryResolve(Parse("--project", Csproj(project)), out RobotLocation location, out string error);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.False, "несобранный робот не может считаться найденным");
            Assert.That(location, Is.Null);
            Assert.That(error, Does.Contain("не собран"), "причина обязана быть названа прямо");
            Assert.That(error, Does.Contain("Robot.dll"), "обязано быть сказано, какую сборку искали");
            Assert.That(error, Does.Contain(IOPath.Combine("bin", "Debug")), "обязаны быть перечислены каталоги поиска");
            Assert.That(error, Does.Contain("Соберите проект"), "обязана быть подсказка, что делать");
        });
    }

    [Test]
    public void Resolve_SeveralAssemblies_PicksNewestAndNamesAllCandidates()
    {
        string project = CreateRobot(legacy: true);
        string oldAssembly = CreateAssembly(project, "bin", "Debug");
        string newAssembly = CreateAssembly(project, "bin", "x64", "Debug");

        File.SetLastWriteTimeUtc(oldAssembly, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(newAssembly, DateTime.UtcNow);

        Assert.That(RobotLocator.TryResolve(Parse("--project", Csproj(project)), out RobotLocation location, out string error),
            Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(location.AssemblyPath, Is.EqualTo(newAssembly), "выбирается самая свежая сборка");
            Assert.That(location.Notes.Any(n => n.Contains("Robot.dll")), Is.True, "какая сборка выбрана — сказано в логе");
            Assert.That(location.Candidates, Does.Contain(IOPath.Combine(project, "bin", "Debug")));
            Assert.That(location.Candidates, Does.Contain(IOPath.Combine(project, "bin", "x64", "Debug")));
        });
    }

    [Test]
    public void Resolve_XamlWithoutClass_AsksForType()
    {
        string project = CreateRobot(legacy: true, className: null);
        CreateAssembly(project, "bin", "Debug");

        Assert.That(RobotLocator.TryResolve(Parse("--project", Csproj(project)), out _, out string error),
            Is.False);
        Assert.That(error, Does.Contain("x:Class").And.Contain("--type"));

        Assert.That(
            RobotLocator.TryResolve(
                Parse("--project", Csproj(project), "--type", "Robot.Main"),
                out RobotLocation location,
                out string typeError),
            Is.True,
            typeError);
        Assert.That(location.TypeName, Is.EqualTo("Robot.Main"));
    }

    [Test]
    public void Resolve_MissingProject_NamesPath()
    {
        string missing = IOPath.Combine(_root, "NoSuch.csproj");

        Assert.That(RobotLocator.TryResolve(Parse("--project", missing), out _, out string error), Is.False);
        Assert.That(error, Does.Contain(missing));
    }

    [Test]
    public void Resolve_MissingWorkflowFile_IsError()
    {
        string missing = IOPath.Combine(_root, "NoSuch.xaml");

        Assert.That(RobotLocator.TryResolve(Parse("--workflow", missing), out _, out string error), Is.False);
        Assert.That(error, Does.Contain(missing));
    }

    [Test]
    public void Resolve_AssemblyWithoutType_IsError()
    {
        string project = CreateRobot(legacy: true);
        string assembly = CreateAssembly(project, "bin", "Debug");

        Assert.That(RobotLocator.TryResolve(RunnerOptions.FromValues(assembly), out _, out string error), Is.False);
        Assert.That(error, Does.Contain("--type"));
    }

    [Test]
    public void NormalizeFramework_ConvertsBothSpellings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RobotLocator.NormalizeFramework("v4.7.2"), Is.EqualTo("net472"));
            Assert.That(RobotLocator.NormalizeFramework("v4.8"), Is.EqualTo("net48"));
            Assert.That(RobotLocator.NormalizeFramework("net472"), Is.EqualTo("net472"));
            Assert.That(RobotLocator.NormalizeFramework("netstandard2.0"), Is.EqualTo("netstandard2.0"));
            Assert.That(RobotLocator.NormalizeFramework("мусор"), Is.Null);
        });
    }

    private static RunnerOptions Parse(params string[] args)
    {
        Assert.That(RunnerOptions.TryParse(args, out RunnerOptions options, out string error), Is.True, error);

        return options;
    }

    private static string Csproj(string directory)
    {
        return IOPath.Combine(directory, "Robot.csproj");
    }

    private string CreateRobot(bool legacy, string framework = "v4.7.2", string className = "Robot.Main")
    {
        string directory = IOPath.Combine(_root, "Robot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string project = legacy
            ? @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <AssemblyName>Robot</AssemblyName>
    <RootNamespace>Robot</RootNamespace>
    <TargetFrameworkVersion>" + framework + @"</TargetFrameworkVersion>
  </PropertyGroup>
</Project>"
            : @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>" + framework + @"</TargetFramework>
    <AssemblyName>Robot</AssemblyName>
    <RootNamespace>Robot</RootNamespace>
  </PropertyGroup>
</Project>";

        File.WriteAllText(Csproj(directory), project);

        string xaml = className == null
            ? @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <WriteLine Text=""без класса"" />
</Activity>"
            : @"<Activity x:Class=""" + className + @""" xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <WriteLine Text=""робот"" />
</Activity>";

        File.WriteAllText(IOPath.Combine(directory, "Main.xaml"), xaml);

        return directory;
    }

    private static string CreateAssembly(string projectDirectory, params string[] parts)
    {
        string directory = IOPath.Combine(new[] { projectDirectory }.Concat(parts).ToArray());
        Directory.CreateDirectory(directory);

        string assembly = IOPath.Combine(directory, "Robot.dll");
        File.WriteAllText(assembly, "не сборка, а маркер раскладки: локатору важен факт файла");

        return assembly;
    }
}
