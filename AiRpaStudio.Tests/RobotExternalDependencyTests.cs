using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Agent.Core.Services;
using NUnit.Framework;
using RPA.Core;
using RPA.Helpers;
using RPA.VsProject;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Шаг 4 плана <c>LooseWorkflowRun</c>: робот с внешней зависимостью (не host-owned) прогоняется
/// по собранной сборке без <c>FileNotFoundException</c>.
/// <para>
/// Эталон собирается настоящим MSBuild (как и <c>Net472Robot</c>): внешняя библиотека
/// <c>RobotExternalLib</c> компилируется отдельным проектом, робот <c>ExternalDependencyRobot</c>
/// ссылается на неё и в теле <c>CodeActivity.Execute</c> вызывает <c>StringTool.Upper</c>.
/// Библиотека лежит в <c>bin\Debug</c> робота и в процесс тестов не загружена: без
/// <see cref="RobotAssemblyResolver"/> такой робот не смог бы исполниться. Проверяется ровно
/// проводка <c>ActivityRunner.Start</c>: <c>InstallForProject</c> до загрузки сборки, затем
/// загрузка из сборки и прогон общим движком.
/// </para>
/// </summary>
[TestFixture]
public class RobotExternalDependencyTests
{
    private readonly List<string> _tempDirectories = new List<string>();

    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = IOPath.Combine(IOPath.GetTempPath(), "robot_external_dep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _tempDirectories.Add(_root);
        RobotAssemblyResolver.Uninstall();
    }

    [TearDown]
    public void TearDown()
    {
        RobotAssemblyResolver.Uninstall();

        foreach (string directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
                // Загруженная в процесс сборка держит файл — каталог останется во временной папке.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        _tempDirectories.Clear();
    }

    [Test]
    public void BuiltAssembly_ExternalDependency_ResolvesFromRobotBin_AndRuns()
    {
        string libDir = IOPath.Combine(_root, "ExternalLib");
        string robotDir = IOPath.Combine(_root, "Robot");
        Directory.CreateDirectory(libDir);
        Directory.CreateDirectory(robotDir);

        string libDll = IOPath.Combine(libDir, "bin", "Debug", "RobotExternalLib.dll");
        string robotDll = IOPath.Combine(robotDir, "bin", "Debug", "ExternalDependencyRobot.dll");
        string copiedLib = IOPath.Combine(robotDir, "bin", "Debug", "RobotExternalLib.dll");

        WriteExternalLib(libDir);
        Build(IOPath.Combine(libDir, "RobotExternalLib.csproj"));

        Assert.That(File.Exists(libDll), Is.True, "внешняя библиотека обязана собраться");

        WriteRobot(robotDir, libDll);
        Build(IOPath.Combine(robotDir, "Robot.csproj"));

        Assert.That(File.Exists(robotDll), Is.True, "робот обязан собраться");
        Assert.That(File.Exists(copiedLib), Is.True, "MSBuild обязан скопировать зависимость робота в его bin");

        // Проводка ActivityRunner.Start: обработчик ставится до загрузки сборки.
        RobotAssemblyResolver.InstallForProject(robotDir);

        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromAssembly(
            robotDll, "ExternalDependencyRobot.Main");

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "max" } },
            TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Description);
            Assert.That(result.GetOutput("Result"), Is.EqualTo("MAX"),
                "результат обязан прийти из внешней зависимости робота — значит, она разрешилась из его bin");
        });
    }

    private void Build(string csproj)
    {
        MsBuildLocation location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Auto);

        if (!location.Found)
        {
            Assert.Ignore("MSBuild не найден на машине — сборка эталона пропущена");
        }

        var log = new List<string>();
        VsBuildResult result = VsProjectBuilder.BuildWithToolchain(
            csproj, "Debug", MsBuildToolchain.Auto, log.Add, restore: false);

        Assert.That(result.Success, Is.True,
            "сборка эталона не прошла: " + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Take(10)) + Environment.NewLine
            + log.FirstOrDefault());
    }

    private static void WriteExternalLib(string libDir)
    {
        File.WriteAllText(IOPath.Combine(libDir, "RobotExternalLib.csproj"), Csproj(
            assemblyName: "RobotExternalLib",
            rootNamespace: "RobotExternalLib",
            compile: "StringTool.cs",
            extraReferences: null));

        File.WriteAllText(IOPath.Combine(libDir, "StringTool.cs"), StringToolSource);
    }

    private static void WriteRobot(string robotDir, string libDll)
    {
        File.WriteAllText(IOPath.Combine(robotDir, "Robot.csproj"), Csproj(
            assemblyName: "ExternalDependencyRobot",
            rootNamespace: "ExternalDependencyRobot",
            compile: "Main.cs",
            extraReferences: new[]
            {
                Reference("System.Activities"),
                Reference("RobotExternalLib", libDll)
            }));

        File.WriteAllText(IOPath.Combine(robotDir, "Main.cs"), RobotMainSource);
    }

    private static string Csproj(string assemblyName, string rootNamespace, string compile, IEnumerable<string> extraReferences)
    {
        var text = new StringBuilder();
        text.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        text.AppendLine("<Project ToolsVersion=\"15.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        text.AppendLine("  <PropertyGroup>");
        text.AppendLine("    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>");
        text.AppendLine("    <Platform Condition=\" '$(Platform)' == '' \">AnyCPU</Platform>");
        text.AppendLine("    <OutputType>Library</OutputType>");
        text.AppendLine("    <RootNamespace>" + rootNamespace + "</RootNamespace>");
        text.AppendLine("    <AssemblyName>" + assemblyName + "</AssemblyName>");
        text.AppendLine("    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>");
        text.AppendLine("  </PropertyGroup>");
        text.AppendLine("  <PropertyGroup Condition=\" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' \">");
        text.AppendLine("    <DebugSymbols>true</DebugSymbols>");
        text.AppendLine("    <DebugType>full</DebugType>");
        text.AppendLine("    <Optimize>false</Optimize>");
        text.AppendLine("    <OutputPath>bin/Debug/</OutputPath>");
        text.AppendLine("    <DefineConstants>DEBUG;TRACE</DefineConstants>");
        text.AppendLine("    <ErrorReport>prompt</ErrorReport>");
        text.AppendLine("    <WarningLevel>4</WarningLevel>");
        text.AppendLine("  </PropertyGroup>");
        text.AppendLine("  <ItemGroup>");
        text.AppendLine("    <Reference Include=\"System\" />");
        text.AppendLine("    <Reference Include=\"System.Core\" />");

        if (extraReferences != null)
        {
            foreach (string reference in extraReferences)
            {
                text.AppendLine("    " + reference);
            }
        }

        text.AppendLine("  </ItemGroup>");
        text.AppendLine("  <ItemGroup>");
        text.AppendLine("    <Compile Include=\"" + compile + "\" />");
        text.AppendLine("  </ItemGroup>");
        text.AppendLine("  <Import Project=\"" + IOPath.Combine("$(MSBuildToolsPath)", "Microsoft.CSharp.targets") + "\" />");
        text.AppendLine("</Project>");

        return text.ToString();
    }

    private static string Reference(string name)
    {
        return "<Reference Include=\"" + name + "\" />";
    }

    private static string Reference(string name, string hintPath)
    {
        return "<Reference Include=\"" + name + "\">"
            + "<HintPath>" + hintPath + "</HintPath>"
            + "<Private>True</Private>"
            + "</Reference>";
    }

    private const string StringToolSource =
@"namespace RobotExternalLib
{
    public static class StringTool
    {
        public static string Upper(string value)
        {
            return (value ?? string.Empty).ToUpperInvariant();
        }
    }
}";

    private const string RobotMainSource =
@"using System.Activities;
using RobotExternalLib;

namespace ExternalDependencyRobot
{
    public sealed class Main : CodeActivity
    {
        public InArgument<string> InputText { get; set; }
        public OutArgument<string> Result { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            string input = InputText.Get(context);
            Result.Set(context, StringTool.Upper(input));
        }
    }
}";
}
