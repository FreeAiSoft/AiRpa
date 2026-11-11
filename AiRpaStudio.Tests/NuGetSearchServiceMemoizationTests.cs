using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Мемоизация локального источника в <see cref="NuGetSearchService"/> (п. 3.2 плана покрытия):
/// повторный вызов не перестраивает список/версии/карточку, а отдаёт тот же объект.
/// </summary>
[TestFixture]
public class NuGetSearchServiceMemoizationTests
{
    [Test]
    public async Task SearchAsync_SecondCall_ReturnsSameListInstance()
    {
        string directory = CreateFeedWithOnePackage();

        try
        {
            var search = new NuGetSearchService(directory);

            List<NuGetPackageInfo> first = await search.SearchAsync(string.Empty, 20, false);
            List<NuGetPackageInfo> second = await search.SearchAsync(string.Empty, 20, false);

            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.SameAs(first),
                "повторный поиск обязан брать результат из мемо, а не перестраивать список");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task GetVersionsAsync_SecondCall_ReturnsSameInstance()
    {
        string directory = CreateFeedWithOnePackage();

        try
        {
            var search = new NuGetSearchService(directory);

            IReadOnlyList<string> first = await search.GetVersionsAsync("Pkg", false);
            IReadOnlyList<string> second = await search.GetVersionsAsync("Pkg", false);

            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.SameAs(first),
                "повторный запрос версий обязан возвращать тот же список из мемо");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task GetPackageInfoAsync_SecondCall_ReturnsSameInstance()
    {
        string directory = CreateFeedWithOnePackage();

        try
        {
            var search = new NuGetSearchService(directory);

            NuGetPackageInfo first = await search.GetPackageInfoAsync("Pkg", false);
            NuGetPackageInfo second = await search.GetPackageInfoAsync("Pkg", false);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first),
                "повторная карточка пакета обязана браться из мемо");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task DifferentQuery_UsesDifferentMemoKey()
    {
        string directory = CreateFeedWithOnePackage();

        try
        {
            var search = new NuGetSearchService(directory);

            List<NuGetPackageInfo> all = await search.SearchAsync(string.Empty, 20, false);
            List<NuGetPackageInfo> filtered = await search.SearchAsync("Pkg", 20, false);

            Assert.That(filtered, Is.Not.SameAs(all),
                "разные запросы обязаны иметь разные ключи мемо и разные списки");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public async Task ClearMemoization_ForcesRebuild()
    {
        string directory = CreateFeedWithOnePackage();

        try
        {
            var search = new NuGetSearchService(directory);

            List<NuGetPackageInfo> first = await search.SearchAsync(string.Empty, 20, false);
            search.ClearMemoization();
            List<NuGetPackageInfo> second = await search.SearchAsync(string.Empty, 20, false);

            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.Not.SameAs(first),
                "после ClearMemoization повторный поиск обязан перестроить список");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateFeedWithOnePackage()
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), "nuget-memo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(IOPath.Combine(directory, "Pkg.1.0.0.nupkg"),
            BuildPackage("Pkg", "1.0.0", "Description"));
        return directory;
    }

    private static byte[] BuildPackage(string id, string version, string description)
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
                + "<description>" + description + "</description>"
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
