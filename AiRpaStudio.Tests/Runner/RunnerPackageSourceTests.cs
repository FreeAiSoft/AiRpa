using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using RPA.Orchestrator.Contracts.Packages;
using RPA.Packaging;
using RPA.Runner;

using AiRpaStudio.Tests.Packages;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Runner;

/// <summary>
/// `OR-03` — прогон пакета: параметр <c>--package</c>, разрешение пакета локатором и настоящий
/// прогон робота из распакованного пакета тем же рантаймом, что и раньше.
/// <para>
/// Смысл карточки именно здесь: «пакет собран» ничего не стоит, если раннер его не запускает.
/// Поэтому проверяется вся цепочка: упаковка эталона → <c>--package</c> → проверка манифеста →
/// распаковка в кэш → загрузка активности → выходные аргументы.
/// </para>
/// </summary>
[TestFixture]
public class RunnerPackageSourceTests
{
    [Test]
    public void Options_PackageIsASource()
    {
        Assert.That(Parse("--package", "robot.zip"), Is.True);

        RunnerOptions options = Options("--package", "robot.zip");

        Assert.That(options.PackagePath, Is.EqualTo("robot.zip"));
        Assert.That(options.DescribeSource(), Is.EqualTo("--package robot.zip"));
    }

    [Test]
    public void Options_PackageWithProject_IsBadArguments()
    {
        Assert.That(Parse("--package", "robot.zip", "--project", "Robot.csproj"), Is.False);
    }

    [Test]
    public void Options_PackageCacheStored()
    {
        RunnerOptions options = Options("--package", "robot.zip", "--package-cache", @"C:\cache");

        Assert.That(options.PackageCacheRoot, Is.EqualTo(@"C:\cache"));
    }

    [Test]
    public void Usage_ListsPackageParameter()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RunnerUsage.Text, Does.Contain("--package"));
            Assert.That(RunnerUsage.Text, Does.Contain("--package-cache"));
        });
    }

    [Test]
    public void Locator_MissingPackage_IsRobotNotFound()
    {
        string missing = IOPath.Combine(PackageFixtures.TempDirectory(), "нет.zip");

        Assert.That(
            RobotLocator.TryResolve(
                Options("--package", missing),
                out _,
                out string error,
                out RunnerExitCode code),
            Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(RunnerExitCode.RobotNotFound));
            Assert.That(error, Does.Contain("не найден"));
        });
    }

    [Test]
    public void Locator_TamperedPackage_IsLoadFailed()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = PackageFixtures.CreatePackage(directory);

        // Подменяем сценарий, не трогая манифест: пакет есть, но он непригоден — это код 5.
        using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update))
        {
            archive.GetEntry("robot/Main.xaml")?.Delete();

            using (var writer = new StreamWriter(archive.CreateEntry("robot/Main.xaml").Open()))
            {
                writer.Write("<Activity x:Class=\"DemoRobot.Evil\" />");
            }
        }

        Assert.That(
            RobotLocator.TryResolve(
                Options("--package", packagePath, "--package-cache", IOPath.Combine(directory, "cache")),
                out _,
                out string error,
                out RunnerExitCode code),
            Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(code, Is.EqualTo(RunnerExitCode.LoadFailed));
            Assert.That(error, Does.Contain("не совпал с манифестом"));
        });
    }

    [Test]
    public void Locator_PackageResolvesEntryTypeAndLibraryDirectory()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = PackageFixtures.CreatePackage(directory);
        string cache = IOPath.Combine(directory, "cache");

        Assert.That(
            RobotLocator.TryResolve(
                Options("--package", packagePath, "--package-cache", cache),
                out RobotLocation location,
                out string error),
            Is.True,
            error);

        Assert.Multiple(() =>
        {
            Assert.That(location.SourceKind, Is.EqualTo("package"));
            Assert.That(location.TypeName, Is.EqualTo("DemoRobot.Main"));
            Assert.That(location.PackageVersion, Is.EqualTo("1.0.0"));
            Assert.That(location.PackageContentHash, Is.Not.Empty);
            Assert.That(IOPath.GetFileName(location.AssemblyPath), Is.EqualTo("DemoRobot.dll"));
            Assert.That(IOPath.GetFileName(location.BinDirectory), Is.EqualTo("lib"));
            Assert.That(File.Exists(location.WorkflowPath), Is.True);
            Assert.That(location.Notes.Any(note => note.Contains("распакован в кэш")), Is.True);
        });
    }

    [Test]
    public void Host_RunsReferenceRobotFromPackage()
    {
        if (!PackageFixtures.ReferenceRobotBuilt())
        {
            Assert.Ignore("эталон Net472Robot не собран: тест упаковывает и прогоняет его");
        }

        string directory = PackageFixtures.TempDirectory();
        string packagePath = IOPath.Combine(directory, "robot.zip");

        RobotPackageResult packed = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = PackageFixtures.ReferenceRobotProject(),
            Configuration = "Debug",
            Version = "1.0.0",
            OutputPath = packagePath,
            Build = false
        });

        Assert.That(packed.IsSuccess, Is.True, packed.Summary);

        RunnerOptions options = Options(
            "--package", packagePath,
            "--package-cache", IOPath.Combine(directory, "cache"),
            "--arg", "InputText=max",
            "--run-id", "package-1");

        RunnerOutcome outcome = WorkflowRunnerHost.Execute(options);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.Success), outcome.ErrorText);
            Assert.That(outcome.Outputs["Result"], Is.EqualTo("MAX"));
            Assert.That(outcome.SourceKind, Is.EqualTo("package"));
            Assert.That(outcome.PackageId, Is.EqualTo("Net472Robot"));
            Assert.That(outcome.PackageVersion, Is.EqualTo("1.0.0"));
            Assert.That(outcome.ToJson()["packageHash"], Is.Not.Empty);
        });
    }

    private static bool Parse(params string[] args)
    {
        return RunnerOptions.TryParse(args, out _, out _);
    }

    private static RunnerOptions Options(params string[] args)
    {
        Assert.That(RunnerOptions.TryParse(args, out RunnerOptions options, out string error), Is.True, error);

        return options;
    }
}
