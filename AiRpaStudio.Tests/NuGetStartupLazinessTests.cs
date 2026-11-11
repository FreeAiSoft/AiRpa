using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;
using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Ленивость NuGet-панели при старте (п. 3.3 плана покрытия): загрузка проекта не ходит в источник,
/// а первичный поиск откладывается до <c>NotifyPanelShown</c>.
/// </summary>
[TestFixture]
public class NuGetStartupLazinessTests
{
    private const string RemoteSource = "https://127.0.0.1:1/index.json";
    private const string LocalSource = "C:\\offline-feed";

    [Test]
    public void LoadProject_DoesNotContactSource()
    {
        string directory = CreateTempDirectory();
        try
        {
            string project = CreateProjectWithPackage(directory);
            var search = new CountingSearchService(RemoteSource);
            var model = new NuGetPanelViewModel(search);

            Assert.That(model.LoadProject(project), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(search.SearchCount, Is.EqualTo(0),
                    "загрузка проекта не должна запускать поиск");
                Assert.That(search.GetVersionsCount, Is.EqualTo(0),
                    "загрузка проекта не должна тянуть версии установленных пакетов");
                Assert.That(search.GetPackageInfoCount, Is.EqualTo(0),
                    "загрузка проекта не должна тянуть метаданные установленных пакетов");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SourceChange_BeforePanelShown_DoesNotSearch()
    {
        string directory = CreateTempDirectory();
        try
        {
            string project = CreateProjectWithPackage(directory);
            var search = new CountingSearchService(RemoteSource);
            var model = new NuGetPanelViewModel(search);
            Assert.That(model.LoadProject(project), Is.True);

            model.SelectedSource = new NuGetSourceDefinition { Name = "other", Url = RemoteSource };

            Assert.Multiple(() =>
            {
                Assert.That(search.SearchCount, Is.EqualTo(0),
                    "до NotifyPanelShown смена источника не запускает поиск");
                Assert.That(search.GetVersionsCount, Is.EqualTo(0));
                Assert.That(search.GetPackageInfoCount, Is.EqualTo(0));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void NotifyPanelShown_FiresDeferredSearch_Once()
    {
        string directory = CreateTempDirectory();
        try
        {
            string project = CreateProject(directory);
            var search = new CountingSearchService(LocalSource);
            var model = new NuGetPanelViewModel(search);
            Assert.That(model.LoadProject(project), Is.True);

            model.NotifyPanelShown();
            model.NotifyPanelShown();

            Assert.That(search.SearchCount, Is.EqualTo(1),
                "первичный поиск уходит ровно один раз при первом показе панели");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void NotifyPanelShown_RemoteEmptyQuery_DoesNotSearch()
    {
        string directory = CreateTempDirectory();
        try
        {
            string project = CreateProject(directory);
            var search = new CountingSearchService(RemoteSource);
            var model = new NuGetPanelViewModel(search);
            Assert.That(model.LoadProject(project), Is.True);

            model.NotifyPanelShown();

            Assert.That(search.SearchCount, Is.EqualTo(0),
                "удалённый источник с пустым запросом не ищет при показе панели");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), "nuget-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateProject(string directory)
    {
        string project = IOPath.Combine(directory, "Demo.csproj");
        File.WriteAllText(project,
            "<Project Sdk=\"Microsoft.NET.Sdk\">"
            + "<PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup>"
            + "</Project>");
        return project;
    }

    private static string CreateProjectWithPackage(string directory)
    {
        string project = IOPath.Combine(directory, "Demo.csproj");
        File.WriteAllText(project,
            "<Project Sdk=\"Microsoft.NET.Sdk\">"
            + "<PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup>"
            + "<ItemGroup><PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.4\" /></ItemGroup>"
            + "</Project>");
        return project;
    }

    /// <summary>Сервис поиска без сети: считает вызовы всех трёх методов источника.</summary>
    private sealed class CountingSearchService : NuGetSearchService
    {
        public CountingSearchService(string sourceUrl)
            : base(sourceUrl)
        {
        }

        public int SearchCount { get; private set; }

        public int GetVersionsCount { get; private set; }

        public int GetPackageInfoCount { get; private set; }

        public override Task<List<NuGetPackageInfo>> SearchAsync(
            string query,
            int take = 20,
            bool includePrerelease = false,
            int skip = 0)
        {
            SearchCount++;
            return Task.FromResult(new List<NuGetPackageInfo>());
        }

        public override Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, bool includePrerelease = false)
        {
            GetVersionsCount++;
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        public override Task<NuGetPackageInfo> GetPackageInfoAsync(string packageId, bool includePrerelease = false)
        {
            GetPackageInfoCount++;
            return Task.FromResult<NuGetPackageInfo>(null);
        }
    }
}
