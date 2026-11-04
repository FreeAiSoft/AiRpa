using System;
using System.Activities;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Agent.Core.Services;
using NUnit.Framework;
using RPA.VsProject;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-09` — эталонный регресс-набор в <c>AiRpaStudio\TestProcess</c>:
/// открыть (парсер/дерево) → собрать (restore + MSBuild) → прогнать (headless) → «трип».
/// </summary>
[TestFixture]
public class TestProcessRegressionTests
{
    private readonly List<string> shadowDirectories = new List<string>();

    [OneTimeTearDown]
    public void CleanupShadowCopies()
    {
        foreach (string directory in shadowDirectories)
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
    }

    [Test]
    public void LegacyRobot_OpensAsWorkflowProjectOnNet472()
    {
        VsProjectInfo info = VsProjectParser.Parse(LegacyRobotProject);

        Assert.That(info.IsSdkStyle, Is.False);
        Assert.That(info.IsWorkflowProject, Is.True, "эталон должен быть WF-проектом (ProjectTypeGuids)");
        Assert.That(info.TargetFrameworks, Is.EqualTo(new[] { "net472" }));
        Assert.That(info.NeedsRestore, Is.False, "legacy-эталон без пакетов");

        // Дерево проекта открывается теми же классами, что и в студии.
        RPA.Entities.FSNode node = VsProjectLoader.LoadProject(LegacyRobotProject);

        Assert.That(node.Name, Is.EqualTo("Net472Robot"));
        Assert.That(node.Childs.Any(c => c.Name == "Main.xaml"), Is.True);
    }

    [Test]
    public void SdkRobot_OpensAsSdkStyleWithPackageReference()
    {
        VsProjectInfo info = VsProjectParser.Parse(SdkRobotProject);

        Assert.That(info.IsSdkStyle, Is.True, "эталон №2 — SDK-style проект");
        Assert.That(info.TargetFrameworks, Is.EqualTo(new[] { "net48" }));
        Assert.That(info.Packages.Select(p => p.Id), Has.Some.EqualTo("Newtonsoft.Json"));
        Assert.That(info.NeedsRestore, Is.True);
    }

    [Test]
    public void ReferenceRobots_BuildWithRestorePass()
    {
        VsBuildResult legacy = Build(LegacyRobotProject);
        Build(SdkRobotProject);

        Assert.That(legacy.ToolchainName, Is.Not.Null.And.Not.Empty, "тулчейн должен быть освещён в результате");
        Assert.That(legacy.RestoreAttempted, Is.True);

        Assert.That(File.Exists(LegacyRobotDll), Is.True);
        Assert.That(File.Exists(IOPath.Combine(TestProcessRoot, "Net48Robot", "bin", "Debug", "net48", "Net48Robot.dll")), Is.True);
    }

    [Test]
    public void LegacyRobot_RunsHeadlessAndReturnsResult()
    {
        Build(LegacyRobotProject);

        string dllPath = IOPath.Combine(ShadowCopy(IOPath.GetDirectoryName(LegacyRobotDll)), "Net472Robot.dll");

        IDictionary<string, object> outputs = WorkflowInvoker.Invoke(CreateActivity(dllPath), new Dictionary<string, object>
        {
            { "InputText", "max" }
        });

        Assert.That(outputs.ContainsKey("Result"), Is.True, "сценарий обязан вернуть выходной аргумент Result");
        Assert.That(outputs["Result"], Is.EqualTo("MAX"));

        IDictionary<string, object> emptyOutputs = WorkflowInvoker.Invoke(CreateActivity(dllPath), new Dictionary<string, object>
        {
            { "InputText", string.Empty }
        });

        Assert.That(emptyOutputs["Result"], Is.EqualTo(string.Empty), "пустой вход обрабатывается ветвью If.Else");
    }

    [Test]
    public void BrokenRobot_TripProducesStructuredError()
    {
        Activity activity = ActivityXamlServices.Load(BrokenRobotXaml);

        Exception error = Assert.Catch<Exception>(() => WorkflowInvoker.Invoke(activity));

        Assert.That(error, Is.Not.Null, "«трип» обязан вернуть исключение, а не упасть процессом");
        Assert.That(error.Message, Does.Contain("boom"));
        Assert.That(error.GetType().Name, Is.EqualTo("Exception"), "тип исключения сохраняется как есть");
    }

    private static VsBuildResult Build(string projectPath)
    {
        MsBuildLocation location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Auto);

        if (!location.Found)
        {
            Assert.Ignore("MSBuild не найден на машине — сборка эталона пропущена");
        }

        var log = new List<string>();
        VsBuildResult result = VsProjectBuilder.BuildWithToolchain(projectPath, "Debug", MsBuildToolchain.Auto, log.Add, restore: true);

        Assert.That(result.RestoreAttempted, Is.True, "сборка эталона обязана идти с restore (CP-05)");
        Assert.That(result.RestoreSucceeded, Is.True,
            "restore упал: " + Environment.NewLine + (result.RestoreOutput ?? string.Empty));

        Assert.That(result.Success, Is.True,
            "сборка эталона не прошла: " + Environment.NewLine +
            string.Join(Environment.NewLine, result.Errors.Take(10)) + Environment.NewLine + log.FirstOrDefault());

        return result;
    }

    /// <summary>
    /// Копирует выход сборки во временный каталог: загруженная сборка не должна блокировать
    /// bin проекта, иначе следующая сборка эталона упадёт на MSB3027.
    /// </summary>
    private string ShadowCopy(string sourceDirectory)
    {
        string target = IOPath.Combine(IOPath.GetTempPath(), "testprocess_shadow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(file, IOPath.Combine(target, IOPath.GetFileName(file)), true);
        }

        shadowDirectories.Add(target);

        return target;
    }

    private static Activity CreateActivity(string assemblyPath)
    {
        object handle = Activator.CreateInstanceFrom(assemblyPath, "Net472Robot.Main").Unwrap();

        return handle as Activity ?? throw new InvalidOperationException("тип Net472Robot.Main не является Activity");
    }

    private static string RepoRoot
    {
        get
        {
            DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

            while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");

            return directory.FullName;
        }
    }

    private static string TestProcessRoot
    {
        get { return IOPath.Combine(RepoRoot, "AiRpaStudio", "TestProcess"); }
    }

    private static string LegacyRobotProject
    {
        get { return IOPath.Combine(TestProcessRoot, "Net472Robot", "Net472Robot.csproj"); }
    }

    private static string LegacyRobotDll
    {
        get { return IOPath.Combine(TestProcessRoot, "Net472Robot", "bin", "Debug", "Net472Robot.dll"); }
    }

    private static string SdkRobotProject
    {
        get { return IOPath.Combine(TestProcessRoot, "Net48Robot", "Net48Robot.csproj"); }
    }

    private static string BrokenRobotXaml
    {
        get { return IOPath.Combine(TestProcessRoot, "BrokenRobot", "BrokenMain.xaml"); }
    }
}
