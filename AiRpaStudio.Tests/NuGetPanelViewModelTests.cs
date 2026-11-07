using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;

namespace AiRpaStudio.Tests;

/// <summary>
/// Загрузка проекта панелью NuGet и чтение источников из <c>NuGet.Config</c>.
/// <para>
/// Тесты намеренно работают без сети: сервис поиска создаётся на заведомо недоступный адрес
/// (127.0.0.1:1), поэтому проверяются только файловые операции — заполнение вкладки Installed из
/// <c>PackageReference</c> и разбор конфигурации источников. Сетевые вызовы панели (проверка
/// обновлений) падают быстро и не влияют на результат.
/// </para>
/// </summary>
[TestFixture]
public class NuGetPanelViewModelTests
{
    private const string OfflineSource = "https://127.0.0.1:1/index.json";

    [Test]
    public void LoadProject_FillsInstalledTabFromPackageReferences()
    {
        string directory = CreateTempDirectory();
        try
        {
            string project = Path.Combine(directory, "Demo.csproj");
            File.WriteAllText(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\">"
                + "<PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup>"
                + "<ItemGroup>"
                + "<PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.4\" />"
                + "<PackageReference Include=\"NuGet.Versioning\" Version=\"6.3.4\" />"
                + "</ItemGroup></Project>");

            var model = new NuGetPanelViewModel(new NuGetSearchService(OfflineSource));

            Assert.That(model.LoadProject(project), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(model.HasProject, Is.True);
                Assert.That(model.ProjectHeader, Is.EqualTo("NuGet Package Manager: Demo"));
                Assert.That(model.InstalledItems.Select(item => item.Id),
                    Is.EquivalentTo(new[] { "Newtonsoft.Json", "NuGet.Versioning" }),
                    "вкладка Installed строится из PackageReference проекта");
                Assert.That(model.InstalledItems.All(item => item.IsInstalled), Is.True);
                Assert.That(model.InstalledItems.All(item => item.InstalledVersion != null), Is.True);
                Assert.That(model.Sources, Has.Count.EqualTo(1), "фиксированный источник не заменяется конфигурацией");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LoadProject_DetectsSdkStyleAndLegacyProjects()
    {
        string directory = CreateTempDirectory();
        try
        {
            string legacy = Path.Combine(directory, "Legacy.csproj");
            File.WriteAllText(legacy, "<Project ToolsVersion=\"15.0\"><ItemGroup /></Project>");
            File.WriteAllText(Path.Combine(directory, "packages.config"),
                "<packages><package id=\"Newtonsoft.Json\" version=\"12.0.1\" targetFramework=\"net48\" /></packages>");

            var model = new NuGetPanelViewModel(new NuGetSearchService(OfflineSource));
            Assert.That(model.LoadProject(legacy), Is.True);

            Assert.That(model.InstalledItems.Select(item => item.Id), Is.EquivalentTo(new[] { "Newtonsoft.Json" }));
            Assert.That(model.InstalledItems[0].InstalledVersion, Is.EqualTo("12.0.1"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void TryDetectProject_WithoutProjectFile_ExplainsProblem()
    {
        string directory = CreateTempDirectory();
        try
        {
            var model = new NuGetPanelViewModel(new NuGetSearchService(OfflineSource));

            Assert.That(model.TryDetectProject(directory), Is.False);
            Assert.That(model.HasProject, Is.False);
            Assert.That(model.StatusText, Does.Contain("No .csproj"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SelectTab_SwitchesVisibleItems()
    {
        string directory = CreateTempDirectory();
        try
        {
            string project = Path.Combine(directory, "Demo.csproj");
            File.WriteAllText(project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>"
                + "<PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.4\" />"
                + "</ItemGroup></Project>");

            var model = new NuGetPanelViewModel(new NuGetSearchService(OfflineSource));
            model.LoadProject(project);

            model.SelectTab(NuGetPackageTab.Installed);

            Assert.Multiple(() =>
            {
                Assert.That(model.IsInstalledTab, Is.True);
                Assert.That(model.ShowBrowseToolbar, Is.False, "на вкладке Installed поиск не показывается");
                Assert.That(model.VisibleItems, Is.SameAs(model.InstalledItems));
                Assert.That(model.EmptyText, Does.Contain("No packages installed"));
            });

            model.SelectTab(NuGetPackageTab.Updates);

            Assert.Multiple(() =>
            {
                Assert.That(model.IsUpdatesTab, Is.True);
                Assert.That(model.VisibleItems, Is.SameAs(model.UpdateItems));
                Assert.That(model.EmptyText, Does.Contain("up to date"));
            });

            model.SelectTab(NuGetPackageTab.Browse);

            Assert.Multiple(() =>
            {
                Assert.That(model.IsBrowseTab, Is.True);
                Assert.That(model.ShowBrowseToolbar, Is.True);
                Assert.That(model.VisibleItems, Is.SameAs(model.BrowseItems));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ClearSearch_ResetsQueryAndList()
    {
        var model = new NuGetPanelViewModel(new NuGetSearchService(OfflineSource)) { SearchQuery = "json" };

        model.ClearSearch();

        Assert.Multiple(() =>
        {
            Assert.That(model.SearchQuery, Is.Empty);
            Assert.That(model.HasSearchQuery, Is.False);
            Assert.That(model.BrowseItems, Is.Empty);
            Assert.That(model.SelectedItem, Is.Null);
        });
    }

    [Test]
    public void Sources_ReadCustomNuGetConfig()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "NuGet.Config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><configuration><packageSources><clear />"
                + "<add key=\"studio-test-source\" value=\"https://example.org/v3/index.json\" />"
                + "</packageSources></configuration>");

            var service = new NuGetSourcesService();
            var sources = service.Load(directory);

            Assert.Multiple(() =>
            {
                Assert.That(sources.Select(source => source.Name), Does.Contain("studio-test-source"),
                    "источники читаются из ближайшего NuGet.Config");
                Assert.That(sources.Any(source => source.Url == "https://example.org/v3/index.json"), Is.True);
                Assert.DoesNotThrow(() => service.IsSourceMappingConfigured(directory));
                Assert.DoesNotThrow(() => service.FindConfigPath(directory));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Sources_WithoutConfig_FallBackToNuGetOrg()
    {
        string empty = Path.Combine(Path.GetTempPath(), "nuget-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);

        try
        {
            var sources = new NuGetSourcesService().Load(empty);

            Assert.That(sources, Is.Not.Empty, "панель обязана предложить хотя бы один источник");
            Assert.That(sources.Any(source => source.Url == NuGetSourcesService.NuGetOrgUrl || !source.IsFallback), Is.True);
        }
        finally
        {
            Directory.Delete(empty, true);
        }
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nuget-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
