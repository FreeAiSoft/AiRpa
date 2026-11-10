using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using RPA.Orchestrator.Contracts.Packages;
using RPA.Packaging;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Packages;

/// <summary>
/// Заготовки пакетов для тестов `OR-03`.
/// <para>
/// Пакет собирается тем же <see cref="RobotPackageWriter"/>, что и в рабочем коде: тесты читают
/// и распаковывают ровно то, что умеет писать упаковщик, поэтому «тест на самодельном архиве»
/// не спрячет расхождение формата. Содержимое файлов — текст: для чтения, сверки хэшей и
/// распаковки настоящая сборка не нужна.
/// </para>
/// </summary>
internal static class PackageFixtures
{
    /// <summary>Создаёт пакет с двумя файлами и возвращает его путь.</summary>
    public static string CreatePackage(
        string directory,
        string id = "DemoRobot",
        string version = "1.0.0",
        string payload = "v1",
        string fileName = "robot.zip")
    {
        Directory.CreateDirectory(directory);

        string library = IOPath.Combine(directory, "lib");
        string workflow = IOPath.Combine(directory, "robot");
        Directory.CreateDirectory(library);
        Directory.CreateDirectory(workflow);

        string assembly = IOPath.Combine(library, id + ".dll");
        string xaml = IOPath.Combine(workflow, "Main.xaml");

        File.WriteAllText(assembly, "assembly-content-" + payload, Encoding.UTF8);
        File.WriteAllText(xaml, "<Activity x:Class=\"" + id + ".Main\" />", Encoding.UTF8);

        RobotPackageManifest manifest = CreateManifest(
            id,
            version,
            new[]
            {
                Entry(RobotPackageFormat.LibraryEntry(id + ".dll"), assembly),
                Entry(RobotPackageFormat.WorkflowEntry("Main.xaml"), xaml)
            },
            id + ".Main");

        string destination = IOPath.Combine(directory, fileName);

        if (!RobotPackageWriter.TryWrite(manifest, Entries(manifest, library, workflow, id), destination, out string error))
        {
            throw new InvalidOperationException("не удалось собрать тестовый пакет: " + error);
        }

        return destination;
    }

    /// <summary>Манифест по списку файлов: хэши и <c>contentHash</c> считаются по содержимому.</summary>
    public static RobotPackageManifest CreateManifest(
        string id = "DemoRobot",
        string version = "1.0.0",
        IEnumerable<RobotPackageFile> files = null,
        string entryType = null,
        string targetFramework = "net472")
    {
        List<RobotPackageFile> list = files == null ? new List<RobotPackageFile>() : new List<RobotPackageFile>(files);

        var manifest = new RobotPackageManifest
        {
            FormatVersion = RobotPackageFormat.CurrentVersion,
            Id = id,
            Name = id,
            Version = version,
            TargetFramework = targetFramework,
            EntryType = string.IsNullOrWhiteSpace(entryType) ? id + ".Main" : entryType,
            EntryAssembly = RobotPackageFormat.LibraryEntry(id + ".dll"),
            EntryWorkflow = RobotPackageFormat.WorkflowEntry("Main.xaml"),
            CreatedAt = "2026-11-06T10:00:00+07:00",
            Builder = new RobotPackageBuilderInfo { Tool = "RpaPack", Version = "1.0.0.0" },
            Source = new RobotPackageSourceInfo { Project = id + ".csproj", Configuration = "Release" },
            Arguments = new List<RobotPackageArgument>
            {
                new RobotPackageArgument { Name = "InputText", Type = "System.String", Direction = "In", Required = true },
                new RobotPackageArgument { Name = "Result", Type = "System.String", Direction = "Out" }
            },
            Dependencies = new List<RobotPackageDependency>(),
            Files = list,
            Warnings = new List<string>()
        };

        manifest.ContentHash = RobotPackageHash.OfContent(list);

        return manifest;
    }

    /// <summary>Файл манифеста по реальному файлу на диске.</summary>
    public static RobotPackageFile Entry(string entryPath, string sourcePath)
    {
        return new RobotPackageFile
        {
            Path = entryPath,
            Size = new FileInfo(sourcePath).Length,
            Sha256 = RobotPackageHash.OfFile(sourcePath)
        };
    }

    /// <summary>Пары «путь в пакете → файл на диске» для writer'а.</summary>
    public static List<KeyValuePair<string, string>> Entries(
        RobotPackageManifest manifest,
        string libraryDirectory,
        string workflowDirectory,
        string id)
    {
        var result = new List<KeyValuePair<string, string>>();

        foreach (RobotPackageFile file in manifest.Files)
        {
            if (RobotPackageFormat.IsLibraryEntry(file.Path))
            {
                result.Add(new KeyValuePair<string, string>(
                    file.Path,
                    IOPath.Combine(libraryDirectory, RobotPackageFormat.FileNameOf(file.Path))));
            }
            else if (RobotPackageFormat.IsWorkflowEntry(file.Path))
            {
                result.Add(new KeyValuePair<string, string>(
                    file.Path,
                    IOPath.Combine(workflowDirectory, RobotPackageFormat.FileNameOf(file.Path))));
            }
        }

        return result;
    }

    /// <summary>Временный каталог теста.</summary>
    public static string TempDirectory(string prefix = "or03_")
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        return directory;
    }

    /// <summary>Корень репозитория: поднимаемся от каталога тестов до <c>AiRpa.sln</c>.</summary>
    public static string RepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(NUnit.Framework.TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }

    /// <summary>Путь к эталонному роботу <c>TestProcess\Net472Robot</c>.</summary>
    public static string ReferenceRobotProject()
    {
        string root = RepositoryRoot();

        return root == null
            ? null
            : IOPath.Combine(root, "AiRpaStudio", "TestProcess", "Net472Robot", "Net472Robot.csproj");
    }

    /// <summary>Эталонный робот собран (есть <c>bin\Debug\Net472Robot.dll</c>).</summary>
    public static bool ReferenceRobotBuilt()
    {
        string project = ReferenceRobotProject();

        return project != null
            && File.Exists(IOPath.Combine(IOPath.GetDirectoryName(project), "bin", "Debug", "Net472Robot.dll"));
    }
}
