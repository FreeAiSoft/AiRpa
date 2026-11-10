using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using NUnit.Framework;

using RPA.Orchestrator.Contracts.Packages;
using RPA.Packaging;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Packages;

/// <summary>
/// `OR-03` — формат пакета, манифест, хэши, правила годности, общие правила проекта и кэш.
/// <para>
/// Проверяется то, на чём оркестратор (`OR-05`) и раннер строят свои решения: по <c>id</c> и
/// <c>version</c> различаются версии, по <c>contentHash</c> — «та же версия или другая»,
/// по правилам путей — можно ли распаковывать пакет вообще. Поэтому негативные случаи
/// (абсолютный путь, <c>..</c>, подмена файла) проверяются наравне с положительными.
/// </para>
/// </summary>
[TestFixture]
public class RobotPackageManifestTests
{
    [Test]
    public void Format_NormalizesEntryPaths()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RobotPackageFormat.NormalizeEntryPath("lib\\Robot.dll"), Is.EqualTo("lib/Robot.dll"));
            Assert.That(RobotPackageFormat.NormalizeEntryPath("./lib/Robot.dll"), Is.EqualTo("lib/Robot.dll"));
            Assert.That(RobotPackageFormat.NormalizeEntryPath(" robot/Main.xaml "), Is.EqualTo("robot/Main.xaml"));
            Assert.That(RobotPackageFormat.NormalizeEntryPath(null), Is.Empty);
        });
    }

    [TestCase("lib/Robot.dll", true)]
    [TestCase("robot/Main.xaml", true)]
    [TestCase("robot/sub/Main.xaml", true)]
    [TestCase("../evil.dll", false)]
    [TestCase("lib/../../Windows/evil.dll", false)]
    [TestCase("C:/Windows/evil.dll", false)]
    [TestCase("/etc/passwd", false)]
    [TestCase("lib//Robot.dll", false)]
    [TestCase("lib/Robot.dll/", false)]
    [TestCase("lib/Ro*bot.dll", false)]
    [TestCase("", false)]
    public void Format_ChecksEntryPaths(string path, bool expected)
    {
        Assert.That(RobotPackageFormat.IsSafeEntryPath(path), Is.EqualTo(expected), path);
    }

    [Test]
    public void Format_ClassifiesDirectories()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RobotPackageFormat.IsLibraryEntry("lib/Robot.dll"), Is.True);
            Assert.That(RobotPackageFormat.IsWorkflowEntry("robot/Main.xaml"), Is.True);
            Assert.That(RobotPackageFormat.IsLibraryEntry("robot/Robot.dll"), Is.False);
            Assert.That(RobotPackageFormat.FileNameOf("lib/deep/Robot.dll"), Is.EqualTo("Robot.dll"));
        });
    }

    [TestCase("v4.7.2", "net472")]
    [TestCase("v4.8", "net48")]
    [TestCase("net472", "net472")]
    [TestCase("NET48", "net48")]
    [TestCase("", null)]
    public void Layout_NormalizesFrameworks(string value, string expected)
    {
        Assert.That(RobotProjectLayout.NormalizeFramework(value), Is.EqualTo(expected));
    }

    [Test]
    public void Hash_ContentDoesNotDependOnFileOrder()
    {
        var first = new List<RobotPackageFile>
        {
            new RobotPackageFile { Path = "lib/A.dll", Size = 1, Sha256 = new string('a', 64) },
            new RobotPackageFile { Path = "robot/Main.xaml", Size = 2, Sha256 = new string('b', 64) }
        };

        var second = new List<RobotPackageFile>(first);
        second.Reverse();

        Assert.That(RobotPackageHash.OfContent(second), Is.EqualTo(RobotPackageHash.OfContent(first)));
    }

    [Test]
    public void Hash_ContentChangesWithFileHash()
    {
        var files = new List<RobotPackageFile>
        {
            new RobotPackageFile { Path = "lib/A.dll", Size = 1, Sha256 = new string('a', 64) }
        };

        string before = RobotPackageHash.OfContent(files);
        files[0].Sha256 = new string('c', 64);

        Assert.That(RobotPackageHash.OfContent(files), Is.Not.EqualTo(before));
    }

    [Test]
    public void Hash_ValidatesSha256Text()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RobotPackageHash.IsSha256Hex(new string('0', 64)), Is.True);
            Assert.That(RobotPackageHash.IsSha256Hex(new string('0', 63)), Is.False);
            Assert.That(RobotPackageHash.IsSha256Hex(new string('z', 64)), Is.False);
            Assert.That(RobotPackageHash.IsSha256Hex(null), Is.False);
        });
    }

    [Test]
    public void Json_RoundTripsManifest()
    {
        RobotPackageManifest manifest = PackageFixtures.CreateManifest(
            "Net472Robot",
            "1.2.3",
            new[]
            {
                new RobotPackageFile { Path = "lib/Net472Robot.dll", Size = 10, Sha256 = new string('a', 64) },
                new RobotPackageFile { Path = "robot/Main.xaml", Size = 20, Sha256 = new string('b', 64) }
            });

        string json = RobotPackageManifestJson.Write(manifest);

        Assert.That(RobotPackageManifestJson.TryRead(json, out RobotPackageManifest restored, out string error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Id, Is.EqualTo("Net472Robot"));
            Assert.That(restored.Version, Is.EqualTo("1.2.3"));
            Assert.That(restored.EntryType, Is.EqualTo("Net472Robot.Main"));
            Assert.That(restored.TargetFramework, Is.EqualTo("net472"));
            Assert.That(restored.Files, Has.Count.EqualTo(2));
            Assert.That(restored.ContentHash, Is.EqualTo(manifest.ContentHash));
            Assert.That(restored.Arguments.Select(a => a.Name), Is.EqualTo(new[] { "InputText", "Result" }));
        });
    }

    [Test]
    public void Json_IsDeterministic()
    {
        RobotPackageManifest manifest = PackageFixtures.CreateManifest();

        string first = RobotPackageManifestJson.Write(manifest);
        string second = RobotPackageManifestJson.Write(PackageFixtures.CreateManifest());

        Assert.That(first, Is.EqualTo(second), "одинаковые манифесты обязаны давать одинаковый JSON");
    }

    [Test]
    public void Json_BrokenText_ReportsError()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RobotPackageManifestJson.TryRead("{ \"id\": ", out _, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(RobotPackageManifestJson.TryRead("   ", out _, out _), Is.False);
        });
    }

    [Test]
    public void Validator_AcceptsManifestWithFiles()
    {
        RobotPackageManifest manifest = PackageFixtures.CreateManifest(
            files: new[]
            {
                new RobotPackageFile { Path = "lib/DemoRobot.dll", Size = 10, Sha256 = new string('a', 64) },
                new RobotPackageFile { Path = "robot/Main.xaml", Size = 20, Sha256 = new string('b', 64) }
            });

        RobotPackageValidationResult validation = RobotPackageValidator.Validate(manifest);

        Assert.That(validation.IsValid, Is.True, validation.Describe());
    }

    [Test]
    public void Validator_RejectsUnsupportedFormat()
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.FormatVersion = RobotPackageFormat.CurrentVersion + 1;

        Assert.That(Codes(manifest), Does.Contain("format.unsupported"));
    }

    [TestCase("робот 1", "1.0.0", "id.invalid")]
    [TestCase("", "1.0.0", "id.missing")]
    [TestCase("DemoRobot", "версия", "version.invalid")]
    [TestCase("DemoRobot", "", "version.missing")]
    public void Validator_RejectsBadIdentity(string id, string version, string expectedCode)
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.Id = id;
        manifest.Version = version;

        Assert.That(Codes(manifest), Does.Contain(expectedCode));
    }

    [Test]
    public void Validator_RejectsContentHashMismatch()
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.ContentHash = new string('f', 64);

        Assert.That(Codes(manifest), Does.Contain("contentHash.mismatch"));
    }

    [Test]
    public void Validator_RejectsEntryAssemblyNotListed()
    {
        RobotPackageManifest manifest = RobotPackageFixturesWithoutAssembly();

        Assert.That(Codes(manifest), Does.Contain("entryAssembly.notListed"));
    }

    [Test]
    public void Validator_RejectsFileOutsidePackage()
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.Files.Add(new RobotPackageFile { Path = "bin/Tool.dll", Size = 1, Sha256 = new string('c', 64) });
        manifest.ContentHash = RobotPackageHash.OfContent(manifest.Files);

        Assert.That(Codes(manifest), Does.Contain("files.outsidePackage"));
    }

    [Test]
    public void Validator_RejectsDuplicateFiles()
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.Files.Add(new RobotPackageFile { Path = "lib/DemoRobot.dll", Size = 10, Sha256 = new string('d', 64) });
        manifest.ContentHash = RobotPackageHash.OfContent(manifest.Files);

        Assert.That(Codes(manifest), Does.Contain("files.duplicate"));
    }

    [Test]
    public void Validator_RejectsUnsafePathInFiles()
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.Files.Add(new RobotPackageFile { Path = "lib/../../evil.dll", Size = 1, Sha256 = new string('e', 64) });
        manifest.ContentHash = RobotPackageHash.OfContent(manifest.Files);

        Assert.That(Codes(manifest), Does.Contain("files.unsafePath"));
    }

    [Test]
    public void Validator_RejectsDuplicateArguments()
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.Arguments.Add(new RobotPackageArgument { Name = "InputText", Type = "System.String", Direction = "In" });

        Assert.That(Codes(manifest), Does.Contain("arguments.duplicate"));
    }

    [Test]
    public void Validator_EmptyNameIsOnlyWarning()
    {
        RobotPackageManifest manifest = GoodManifest();
        manifest.Name = null;

        RobotPackageValidationResult validation = RobotPackageValidator.Validate(manifest);

        Assert.Multiple(() =>
        {
            Assert.That(validation.IsValid, Is.True, "пустое имя — замечание, а не отказ");
            Assert.That(validation.Issues.Select(i => i.Code), Does.Contain("name.empty"));
        });
    }

    [Test]
    public void Layout_ReadsReferenceRobotProject()
    {
        if (!PackageFixtures.ReferenceRobotBuilt())
        {
            Assert.Ignore("эталон Net472Robot не собран: тест читает его csproj и Main.xaml");
        }

        string project = PackageFixtures.ReferenceRobotProject();

        Assert.That(RobotProject.TryLoad(project, out RobotProject robot, out string error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(robot.AssemblyName, Is.EqualTo("Net472Robot"));
            Assert.That(robot.TargetFramework, Is.EqualTo("net472"));
            Assert.That(robot.EntryType, Is.EqualTo("Net472Robot.Main"));
            Assert.That(robot.Version, Is.Not.Empty);
            Assert.That(robot.Arguments.Select(a => a.Name), Is.EqualTo(new[] { "InputText", "Result" }));
            Assert.That(robot.Arguments[0].Type, Is.EqualTo("System.String"));
            Assert.That(robot.Arguments[0].Direction, Is.EqualTo("In"));
            Assert.That(robot.Arguments[0].Required, Is.True, "входной аргумент без значения по умолчанию обязателен");
            Assert.That(robot.Arguments[1].Direction, Is.EqualTo("Out"));
            Assert.That(robot.WorkflowFiles, Is.Not.Empty);
        });
    }

    [Test]
    public void Layout_CandidateDirectoriesCoverStudioAndSdkLayouts()
    {
        List<string> directories = RobotProjectLayout.CandidateDirectories(@"C:\Robot", new[] { "net472" });

        Assert.Multiple(() =>
        {
            Assert.That(directories, Does.Contain(IOPath.Combine(@"C:\Robot", "bin", "Debug")));
            Assert.That(directories, Does.Contain(IOPath.Combine(@"C:\Robot", "bin", "Debug", "net472")));
            Assert.That(directories, Does.Contain(IOPath.Combine(@"C:\Robot", "bin", "x64", "Debug", "net472")));
            Assert.That(directories.IndexOf(IOPath.Combine(@"C:\Robot", "bin", "Debug")),
                Is.LessThan(directories.IndexOf(IOPath.Combine(@"C:\Robot", "bin", "Release"))),
                "Debug идёт до Release — так же ищет раннер");
        });
    }

    [Test]
    public void Cache_LayoutsPackageAndExtractionPaths()
    {
        string root = @"C:\cache";

        Assert.Multiple(() =>
        {
            Assert.That(
                RobotPackageCache.PackageFilePath(root, "DemoRobot", "1.0.0"),
                Is.EqualTo(IOPath.Combine(root, "DemoRobot", "1.0.0", "DemoRobot-1.0.0.zip")));
            Assert.That(
                RobotPackageCache.ExtractionPath(root, "DemoRobot", "1.0.0", "abc123"),
                Is.EqualTo(IOPath.Combine(root, "DemoRobot", "1.0.0", "abc123")));
        });
    }

    [Test]
    public void Cache_ExtractionIsReadyOnlyWithMatchingMarker()
    {
        string directory = PackageFixtures.TempDirectory();

        Assert.Multiple(() =>
        {
            Assert.That(RobotPackageCache.IsExtracted(directory, "hash-1"), Is.False, "без маркера распаковка не готова");

            File.WriteAllText(IOPath.Combine(directory, RobotPackageFormat.ExtractionMarkerFileName), "hash-1");
            Assert.That(RobotPackageCache.IsExtracted(directory, "hash-1"), Is.True);

            File.WriteAllText(IOPath.Combine(directory, RobotPackageFormat.ExtractionMarkerFileName), "hash-2");
            Assert.That(RobotPackageCache.IsExtracted(directory, "hash-1"), Is.False, "другой хэш — другая распаковка");
        });
    }

    private static RobotPackageManifest GoodManifest()
    {
        return PackageFixtures.CreateManifest(
            files: new[]
            {
                new RobotPackageFile { Path = "lib/DemoRobot.dll", Size = 10, Sha256 = new string('a', 64) },
                new RobotPackageFile { Path = "robot/Main.xaml", Size = 20, Sha256 = new string('b', 64) }
            });
    }

    private static RobotPackageManifest RobotPackageFixturesWithoutAssembly()
    {
        RobotPackageManifest manifest = PackageFixtures.CreateManifest(
            files: new[]
            {
                new RobotPackageFile { Path = "lib/Other.dll", Size = 10, Sha256 = new string('a', 64) },
                new RobotPackageFile { Path = "robot/Main.xaml", Size = 20, Sha256 = new string('b', 64) }
            });

        return manifest;
    }

    private static string[] Codes(RobotPackageManifest manifest)
    {
        return RobotPackageValidator.Validate(manifest).Issues.Select(i => i.Code).ToArray();
    }
}
