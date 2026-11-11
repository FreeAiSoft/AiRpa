using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RPA.NuGet.Services;
using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Дисковый кэш локального NuGet-источника (<c>LocalPackageIndexCache</c>, п. 3.1 плана покрытия):
/// построение индекса из <c>.nupkg</c>, сортировка, переиспользование кэша, инвалидация.
/// </summary>
[TestFixture]
public class NuGetLocalPackageIndexCacheTests
{
    [Test]
    public void Entries_BuildsIndexFromNupkg()
    {
        string directory = CreateTempDirectory();
        try
        {
            string feed = CreateFeed(directory);
            File.WriteAllBytes(IOPath.Combine(feed, "Pkg.1.0.0.nupkg"),
                BuildPackage("Pkg", "1.0.0", "Description text", "Author"));
            File.WriteAllBytes(IOPath.Combine(feed, "Pre.2.0.0-beta.nupkg"),
                BuildPackage("Pre", "2.0.0-beta", "Prerelease"));

            var cache = new LocalPackageIndexCache(feed);
            LocalPackageIndexEntry pkg = cache.Entries.Single(entry => entry.Id == "Pkg");
            LocalPackageIndexEntry pre = cache.Entries.Single(entry => entry.Id == "Pre");

            Assert.Multiple(() =>
            {
                Assert.That(pkg.Version, Is.EqualTo("1.0.0"));
                Assert.That(pkg.Description, Is.EqualTo("Description text"));
                Assert.That(pkg.Authors, Is.EqualTo("Author"));
                Assert.That(pkg.IsPrerelease, Is.False);
                Assert.That(pre.Version, Is.EqualTo("2.0.0-beta"));
                Assert.That(pre.IsPrerelease, Is.True, "пререлиз распознаётся по версии");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Entries_SortsByIdAndVersionDesc()
    {
        string directory = CreateTempDirectory();
        try
        {
            string feed = CreateFeed(directory);
            File.WriteAllBytes(IOPath.Combine(feed, "Alpha.Package.1.0.0.nupkg"),
                BuildPackage("Alpha.Package", "1.0.0"));
            File.WriteAllBytes(IOPath.Combine(feed, "Alpha.Package.2.0.0.nupkg"),
                BuildPackage("Alpha.Package", "2.0.0"));
            File.WriteAllBytes(IOPath.Combine(feed, "Beta.Package.1.0.0.nupkg"),
                BuildPackage("Beta.Package", "1.0.0"));

            var cache = new LocalPackageIndexCache(feed);

            Assert.That(
                cache.Entries.Select(entry => entry.Id + "@" + entry.Version).ToArray(),
                Is.EqualTo(new[]
                {
                    "Alpha.Package@2.0.0",
                    "Alpha.Package@1.0.0",
                    "Beta.Package@1.0.0"
                }),
                "записи сортируются по Id, а версии одного пакета — по убыванию");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Entries_WritesCacheFile_AndSecondInstanceReusesIt()
    {
        string directory = CreateTempDirectory();
        try
        {
            string feed = CreateFeed(directory);
            File.WriteAllBytes(IOPath.Combine(feed, "Pkg.1.0.0.nupkg"),
                BuildPackage("Pkg", "1.0.0", "Description"));

            var first = new LocalPackageIndexCache(feed);
            int firstCount = first.Entries.Count;

            string cachePath = LocalPackageIndexCache.GetCachePath(feed);
            Assert.That(File.Exists(cachePath), Is.True, "первое чтение обязано записать файл кэша");
            DateTime writeTime = File.GetLastWriteTimeUtc(cachePath);

            var second = new LocalPackageIndexCache(feed);
            int secondCount = second.Entries.Count;

            Assert.Multiple(() =>
            {
                Assert.That(secondCount, Is.EqualTo(firstCount));
                Assert.That(File.GetLastWriteTimeUtc(cachePath), Is.EqualTo(writeTime),
                    "второй экземпляр обязан переиспользовать файл кэша без перезаписи");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void CorruptedCacheFile_IsRebuilt()
    {
        string directory = CreateTempDirectory();
        try
        {
            string feed = CreateFeed(directory);
            File.WriteAllBytes(IOPath.Combine(feed, "Pkg.1.0.0.nupkg"),
                BuildPackage("Pkg", "1.0.0", "Description"));

            _ = new LocalPackageIndexCache(feed).Entries;

            string cachePath = LocalPackageIndexCache.GetCachePath(feed);
            File.WriteAllText(cachePath, "{ это не json");

            var rebuilt = new LocalPackageIndexCache(feed);

            Assert.Multiple(() =>
            {
                Assert.That(rebuilt.Entries.Count, Is.EqualTo(1),
                    "битый кэш обязан быть пересобран, а не отдан как пустой");
                Assert.That(File.ReadAllText(cachePath), Does.Contain("Pkg"),
                    "после пересборки файл кэша записывается заново");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void FingerprintChanges_InvalidatesCache()
    {
        string directory = CreateTempDirectory();
        try
        {
            string feed = CreateFeed(directory);
            File.WriteAllBytes(IOPath.Combine(feed, "Pkg.1.0.0.nupkg"),
                BuildPackage("Pkg", "1.0.0"));

            var first = new LocalPackageIndexCache(feed);
            Assert.That(first.Entries.Count, Is.EqualTo(1));
            string fingerprintBefore = LocalPackageIndexCache.ComputeFingerprint(feed);
            string cachePath = LocalPackageIndexCache.GetCachePath(feed);

            File.WriteAllBytes(IOPath.Combine(feed, "Pkg.2.0.0.nupkg"),
                BuildPackage("Pkg", "2.0.0"));

            var second = new LocalPackageIndexCache(feed);

            Assert.Multiple(() =>
            {
                Assert.That(LocalPackageIndexCache.ComputeFingerprint(feed), Is.Not.EqualTo(fingerprintBefore),
                    "добавление пакета обязано менять отпечаток");
                Assert.That(second.Entries.Count, Is.EqualTo(2),
                    "изменившийся отпечаток обязан вызвать пересборку индекса");
                Assert.That(File.ReadAllText(cachePath), Does.Contain("2.0.0"),
                    "пересобранный индекс записан в файл кэша");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void InvalidateAll_RemovesCacheFiles()
    {
        string directory = CreateTempDirectory();
        try
        {
            string feed = CreateFeed(directory);
            File.WriteAllBytes(IOPath.Combine(feed, "Pkg.1.0.0.nupkg"),
                BuildPackage("Pkg", "1.0.0", "Description"));

            _ = new LocalPackageIndexCache(feed).Entries;
            string cachePath = LocalPackageIndexCache.GetCachePath(feed);
            Assert.That(File.Exists(cachePath), Is.True, "первое чтение обязано записать файл кэша");

            NuGetOptimizationCache.InvalidateAll();

            Assert.That(File.Exists(cachePath), Is.False,
                "InvalidateAll обязана удалять файлы индекса локального источника");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }


    [Test]
    public void MissingDirectory_ReturnsEmpty()
    {
        string missing = IOPath.Combine(IOPath.GetTempPath(), "no-such-feed-" + Guid.NewGuid().ToString("N"));

        var cache = new LocalPackageIndexCache(missing);

        Assert.That(cache.Entries, Is.Empty,
            "несуществующий каталог должен давать пустой индекс без исключений");
    }

    private static string CreateTempDirectory()
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), "nuget-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateFeed(string directory)
    {
        string feed = IOPath.Combine(directory, "feed");
        Directory.CreateDirectory(feed);
        return feed;
    }

    private static byte[] BuildPackage(string id, string version, string description = null, string authors = null)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            string nuspec =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
                + "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">"
                + "<metadata>"
                + "<id>" + id + "</id>"
                + "<version>" + version + "</version>"
                + (authors == null ? string.Empty : "<authors>" + authors + "</authors>")
                + (description == null ? string.Empty : "<description>" + description + "</description>")
                + "</metadata></package>";

            WriteEntry(archive, id + ".nuspec", nuspec);
        }

        return buffer.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using (Stream stream = entry.Open())
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(content);
        }
    }
}
