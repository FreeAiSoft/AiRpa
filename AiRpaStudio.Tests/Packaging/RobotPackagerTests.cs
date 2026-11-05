using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

using NUnit.Framework;

using RPA.Orchestrator.Contracts.Packages;
using RPA.Packaging;

using AiRpaStudio.Tests.Packages;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Packaging;

/// <summary>
/// `OR-03` — упаковка робота: сборка проекта → сбор файлов → манифест → архив → неизменяемое
/// размещение.
/// <para>
/// Сборка через MSBuild здесь подменена (<see cref="IRobotProjectBuilder"/>), чтобы тесты не
/// зависели от тулчейна и не занимали минуты: проверяется логика упаковки. Живая сборка и живой
/// <c>RpaPack.exe</c> проверяются отдельно — на эталонном роботе.
/// </para>
/// </summary>
[TestFixture]
public class RobotPackagerTests
{
    [Test]
    public void Pack_ReferenceRobotWithoutBuild_CreatesPackage()
    {
        if (!PackageFixtures.ReferenceRobotBuilt())
        {
            Assert.Ignore("эталон Net472Robot не собран: тест упаковывает его готовый bin");
        }

        string directory = PackageFixtures.TempDirectory();
        string output = IOPath.Combine(directory, "robot.zip");

        RobotPackageResult result = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = PackageFixtures.ReferenceRobotProject(),
            Configuration = "Debug",
            Version = "1.0.0",
            OutputPath = output,
            Build = false
        });

        Assert.That(result.IsSuccess, Is.True, result.Summary);
        Assert.That(File.Exists(output), Is.True);

        RobotPackageManifest manifest = result.Manifest;
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RobotPackStatus.Created));
            Assert.That(manifest.Id, Is.EqualTo("Net472Robot"));
            Assert.That(manifest.Version, Is.EqualTo("1.0.0"));
            Assert.That(manifest.TargetFramework, Is.EqualTo("net472"));
            Assert.That(manifest.EntryType, Is.EqualTo("Net472Robot.Main"));
            Assert.That(manifest.EntryAssembly, Is.EqualTo("lib/Net472Robot.dll"));
            Assert.That(manifest.EntryWorkflow, Is.EqualTo("robot/Main.xaml"));
            Assert.That(manifest.ContentHash, Is.Not.Empty);
            Assert.That(manifest.Arguments.Select(a => a.Name), Is.EqualTo(new[] { "InputText", "Result" }));
            Assert.That(manifest.Files.Select(f => f.Path), Does.Contain("lib/Net472Robot.dll"));
            Assert.That(manifest.Files.Select(f => f.Path), Does.Contain("robot/Main.xaml"));
            Assert.That(manifest.Warnings, Is.Empty, "у эталона в net472 замечаний быть не должно");
        });

        // Пакет обязан читаться и проходить проверку: это и есть контракт с раннером.
        Assert.That(RobotPackageReader.TryOpen(output, out RobotPackage package, out string error), Is.True, error);
        Assert.That(package.Manifest.ContentHash, Is.EqualTo(manifest.ContentHash));
    }

    [Test]
    public void Pack_SameContentTwice_ReusesPackage()
    {
        if (!PackageFixtures.ReferenceRobotBuilt())
        {
            Assert.Ignore("эталон Net472Robot не собран");
        }

        string directory = PackageFixtures.TempDirectory();
        var request = new RobotPackageRequest
        {
            ProjectPath = PackageFixtures.ReferenceRobotProject(),
            Configuration = "Debug",
            Version = "1.0.0",
            OutputPath = IOPath.Combine(directory, "robot.zip"),
            Build = false
        };

        RobotPackageResult first = new RobotPackager().Pack(request);
        RobotPackageResult second = new RobotPackager().Pack(request);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(RobotPackStatus.Created), first.Summary);
            Assert.That(second.Status, Is.EqualTo(RobotPackStatus.Reused), second.Summary);
            Assert.That(second.Manifest.ContentHash, Is.EqualTo(first.Manifest.ContentHash));
            Assert.That(second.PackagePath, Is.EqualTo(first.PackagePath));
        });
    }

    [Test]
    public void Pack_ChangedContentSameVersion_ReportsVersionConflict()
    {
        if (!PackageFixtures.ReferenceRobotBuilt())
        {
            Assert.Ignore("эталон Net472Robot не собран");
        }

        string directory = PackageFixtures.TempDirectory();
        string project = CopyReferenceRobot(directory);
        string output = IOPath.Combine(directory, "robot.zip");

        RobotPackageResult first = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = project,
            Configuration = "Debug",
            Version = "1.0.0",
            OutputPath = output,
            Build = false
        });

        Assert.That(first.Status, Is.EqualTo(RobotPackStatus.Created), first.Summary);

        // Меняем содержимое сборки: та же версия с другими байтами публиковаться не должна.
        string assembly = IOPath.Combine(directory, "bin", "Debug", "Net472Robot.dll");
        byte[] bytes = File.ReadAllBytes(assembly);
        bytes[bytes.Length - 1] = (byte)(bytes[bytes.Length - 1] ^ 0xFF);
        File.WriteAllBytes(assembly, bytes);

        RobotPackageResult second = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = project,
            Configuration = "Debug",
            Version = "1.0.0",
            OutputPath = output,
            Build = false
        });

        Assert.Multiple(() =>
        {
            Assert.That(second.Status, Is.EqualTo(RobotPackStatus.VersionConflict), second.Summary);
            Assert.That(second.Error, Does.Contain("неизменяем"));
        });
    }

    [Test]
    public void Pack_NotBuiltRobot_ReportsNotBuilt()
    {
        string directory = PackageFixtures.TempDirectory();
        string project = WriteProject(directory, "НеСобранРобот", "NotBuilt.Main");

        RobotPackageResult result = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = project,
            OutputPath = IOPath.Combine(directory, "robot.zip"),
            Build = false
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RobotPackStatus.NotBuilt), result.Summary);
            Assert.That(result.Error, Does.Contain("не собран"));
        });
    }

    [Test]
    public void Pack_ProjectWithoutEntryPoint_ReportsProjectInvalid()
    {
        string directory = PackageFixtures.TempDirectory();
        string project = IOPath.Combine(directory, "NoEntry.csproj");

        File.WriteAllText(project, @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <AssemblyName>NoEntry</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>
</Project>");

        File.WriteAllText(IOPath.Combine(directory, "Main.xaml"), @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
  xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <WriteLine Text=""без x:Class"" />
</Activity>");

        RobotPackageResult result = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = project,
            OutputPath = IOPath.Combine(directory, "robot.zip"),
            Build = false
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RobotPackStatus.ProjectInvalid), result.Summary);
            Assert.That(result.Error, Does.Contain("точка входа"));
        });
    }

    [Test]
    public void Pack_MissingProject_ReportsProjectInvalid()
    {
        RobotPackageResult result = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = IOPath.Combine(PackageFixtures.TempDirectory(), "нет.csproj"),
            Build = false
        });

        Assert.That(result.Status, Is.EqualTo(RobotPackStatus.ProjectInvalid));
    }

    [Test]
    public void Pack_BuildFailed_ReportsBuildFailed()
    {
        string directory = PackageFixtures.TempDirectory();
        string project = WriteProject(directory, "Broken", "Broken.Main");

        var packager = new RobotPackager(new FailingBuilder());

        RobotPackageResult result = packager.Pack(new RobotPackageRequest
        {
            ProjectPath = project,
            OutputPath = IOPath.Combine(directory, "robot.zip"),
            Build = true
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(RobotPackStatus.BuildFailed), result.Summary);
            Assert.That(result.Error, Does.Contain("Ошибка компиляции XAML"));
            Assert.That(result.Log.Any(line => line.Contains("сборка не удалась")), Is.True, "ход упаковки обязан попасть в журнал");
        });
    }

    [Test]
    public void Pack_ArchiveContainsManifestAndEntryAssembly()
    {
        if (!PackageFixtures.ReferenceRobotBuilt())
        {
            Assert.Ignore("эталон Net472Robot не собран");
        }

        string directory = PackageFixtures.TempDirectory();
        string output = IOPath.Combine(directory, "robot.zip");

        RobotPackageResult result = new RobotPackager().Pack(new RobotPackageRequest
        {
            ProjectPath = PackageFixtures.ReferenceRobotProject(),
            Configuration = "Debug",
            OutputPath = output,
            Build = false
        });

        Assert.That(result.IsSuccess, Is.True, result.Summary);

        using (var stream = File.OpenRead(output))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            string[] entries = archive.Entries.Select(e => e.FullName).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(entries, Does.Contain(RobotPackageFormat.ManifestFileName));
                Assert.That(entries, Does.Contain("lib/Net472Robot.dll"));
                Assert.That(entries, Does.Contain("robot/Main.xaml"));
                Assert.That(entries.Any(e => e.Contains('\\')), Is.False, "пути внутри пакета — только через «/»");
            });
        }
    }

    /// <summary>Копия эталонного робота (проект, XAML, собранный bin) во временный каталог.</summary>
    private static string CopyReferenceRobot(string destination)
    {
        string source = IOPath.GetDirectoryName(PackageFixtures.ReferenceRobotProject());

        Directory.CreateDirectory(IOPath.Combine(destination, "bin", "Debug"));

        foreach (string file in Directory.GetFiles(source, "*.*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, IOPath.Combine(destination, IOPath.GetFileName(file)), true);
        }

        foreach (string file in Directory.GetFiles(IOPath.Combine(source, "bin", "Debug")))
        {
            File.Copy(file, IOPath.Combine(destination, "bin", "Debug", IOPath.GetFileName(file)), true);
        }

        return IOPath.Combine(destination, "Net472Robot.csproj");
    }

    private static string WriteProject(string directory, string assemblyName, string entryType)
    {
        string project = IOPath.Combine(directory, assemblyName + ".csproj");

        File.WriteAllText(project, @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <AssemblyName>" + assemblyName + @"</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>
</Project>");

        File.WriteAllText(IOPath.Combine(directory, "Main.xaml"), @"<Activity x:Class=""" + entryType + @"""
  xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
  xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <WriteLine Text=""робот"" />
</Activity>");

        return project;
    }

    /// <summary>Сборка, которая «падает»: проверяется, что упаковка не продолжается после неё.</summary>
    private sealed class FailingBuilder : IRobotProjectBuilder
    {
        public RobotProjectBuildResult Build(RobotProject project, string configuration, Action<string> log)
        {
            var result = new RobotProjectBuildResult
            {
                Attempted = true,
                Success = false,
                ExitCode = 1,
                MsBuildVersion = "17.0"
            };

            result.Errors.Add("Main.xaml(12,5): error XLS0414: Ошибка компиляции XAML");

            return result;
        }
    }
}
