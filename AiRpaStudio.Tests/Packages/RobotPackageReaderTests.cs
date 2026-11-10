using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

using NUnit.Framework;

using RPA.Orchestrator.Contracts.Packages;
using RPA.Packaging;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Packages;

/// <summary>
/// `OR-03` — чтение пакета, проверка целостности, распаковка и локальный кэш.
/// <para>
/// Здесь проверяется обещание «пакет неизменяем и самодостаточен»: подменённый файл, лишний файл,
/// отсутствующий манифест и недописанная распаковка обязаны быть замечены <b>до</b> запуска робота.
/// Иначе раннер падал бы внутри загрузки workflow, и причину искали бы в XAML вместо пакета.
/// </para>
/// </summary>
[TestFixture]
public class RobotPackageReaderTests
{
    [Test]
    public void Reader_OpensPackageWrittenByWriter()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = PackageFixtures.CreatePackage(directory);

        Assert.That(RobotPackageReader.TryOpen(packagePath, out RobotPackage package, out string error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(package.Id, Is.EqualTo("DemoRobot"));
            Assert.That(package.Version, Is.EqualTo("1.0.0"));
            Assert.That(package.EntryType, Is.EqualTo("DemoRobot.Main"));
            Assert.That(package.Manifest.Files, Has.Count.EqualTo(2));
            Assert.That(package.PackageSha256, Is.Not.Empty);
            Assert.That(package.Describe(), Does.Contain("DemoRobot 1.0.0"));
        });
    }

    [Test]
    public void Reader_RejectsTamperedFile()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = PackageFixtures.CreatePackage(directory);

        ReplaceEntry(packagePath, "robot/Main.xaml", "<Activity x:Class=\"DemoRobot.Evil\" />");

        Assert.That(RobotPackageReader.TryOpen(packagePath, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("не совпал с манифестом"));
    }

    [Test]
    public void Reader_RejectsExtraEntry()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = PackageFixtures.CreatePackage(directory);

        ReplaceEntry(packagePath, "lib/Injected.dll", "чужак");

        Assert.That(RobotPackageReader.TryOpen(packagePath, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("которого нет в манифесте"));
    }

    [Test]
    public void Reader_RejectsPackageWithoutManifest()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = IOPath.Combine(directory, "no-manifest.zip");

        using (var stream = new FileStream(packagePath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("lib/DemoRobot.dll").Open(), Encoding.UTF8))
        {
            writer.Write("только сборка, без манифеста");
        }

        Assert.That(RobotPackageReader.TryOpen(packagePath, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("не пакет робота"));
    }

    [Test]
    public void Reader_RejectsPackageWithoutDeclaredFile()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = PackageFixtures.CreatePackage(directory);

        RemoveEntry(packagePath, "lib/DemoRobot.dll");

        Assert.That(RobotPackageReader.TryOpen(packagePath, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("нет файла"));
    }

    [Test]
    public void Reader_RejectsNonZipFile()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = IOPath.Combine(directory, "broken.zip");
        File.WriteAllText(packagePath, "это не zip");

        Assert.That(RobotPackageReader.TryOpen(packagePath, out _, out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void Extract_WritesFilesAndManifest()
    {
        string directory = PackageFixtures.TempDirectory();
        string packagePath = PackageFixtures.CreatePackage(directory);
        string target = IOPath.Combine(directory, "extracted");

        Assert.That(RobotPackageReader.TryOpen(packagePath, out RobotPackage package, out string openError), Is.True, openError);
        Assert.That(RobotPackageReader.TryExtract(package, target, out string error), Is.True, error);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(IOPath.Combine(target, "lib", "DemoRobot.dll")), Is.True);
            Assert.That(File.Exists(IOPath.Combine(target, "robot", "Main.xaml")), Is.True);
            Assert.That(File.Exists(IOPath.Combine(target, RobotPackageFormat.ManifestFileName)), Is.True);
            Assert.That(
                RobotPackageManifestJson.TryReadFile(IOPath.Combine(target, RobotPackageFormat.ManifestFileName), out RobotPackageManifest restored, out _),
                Is.True);
            Assert.That(restored?.ContentHash, Is.EqualTo(package.Manifest.ContentHash));
        });
    }

    [Test]
    public void Cache_EnsureExtracted_WritesMarkerAndReusesDirectory()
    {
        string directory = PackageFixtures.TempDirectory();
        string root = IOPath.Combine(directory, "cache");
        string packagePath = PackageFixtures.CreatePackage(directory);

        Assert.That(RobotPackageReader.TryOpen(packagePath, out RobotPackage package, out string openError), Is.True, openError);

        Assert.That(RobotPackageCache.TryEnsureExtracted(root, package, out string first, out string firstError), Is.True, firstError);
        Assert.That(RobotPackageCache.TryEnsureExtracted(root, package, out string second, out string secondError), Is.True, secondError);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first), "повторный вызов обязан вернуть тот же каталог");
            Assert.That(RobotPackageCache.IsExtracted(first, package.Manifest.ContentHash), Is.True);
            Assert.That(File.Exists(IOPath.Combine(first, "lib", "DemoRobot.dll")), Is.True);
            Assert.That(
                Directory.GetDirectories(root, "*.tmp-*", SearchOption.AllDirectories),
                Is.Empty,
                "временные каталоги распаковки не должны оставаться");
        });
    }

    [Test]
    public void Cache_Place_RefusesSameVersionWithDifferentContent()
    {
        string directory = PackageFixtures.TempDirectory();
        string root = IOPath.Combine(directory, "cache");

        string first = PackageFixtures.CreatePackage(directory, payload: "v1", fileName: "first.zip");
        string second = PackageFixtures.CreatePackage(directory, payload: "v2", fileName: "second.zip");

        Assert.That(RobotPackageReader.TryOpen(first, out RobotPackage packageOne, out string errorOne), Is.True, errorOne);
        Assert.That(RobotPackageReader.TryOpen(second, out RobotPackage packageTwo, out string errorTwo), Is.True, errorTwo);

        Assert.That(RobotPackageCache.TryPlace(root, packageOne, out string placed, out string placeError), Is.True, placeError);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(placed), Is.True);
            Assert.That(
                RobotPackageCache.TryPlace(root, packageOne, out string reused, out string reuseError),
                Is.True,
                reuseError);
            Assert.That(reused, Is.EqualTo(placed), "тот же пакет переиспользуется");

            Assert.That(
                RobotPackageCache.TryPlace(root, packageTwo, out _, out string conflictError),
                Is.False,
                "другое содержимое под той же версией обязано быть отвергнуто");
            Assert.That(conflictError, Does.Contain("неизменяем"));
        });
    }

    private static void ReplaceEntry(string packagePath, string entryPath, string content)
    {
        using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            ZipArchiveEntry existing = archive.GetEntry(entryPath);

            existing?.Delete();

            ZipArchiveEntry entry = archive.CreateEntry(entryPath);

            using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
            {
                writer.Write(content);
            }
        }
    }

    private static void RemoveEntry(string packagePath, string entryPath)
    {
        using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            archive.GetEntry(entryPath)?.Delete();
        }
    }
}
