using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.Core.Generation;
using RPA.Core.Workflow;
using AiRpaStudio.Tests.Workflow;

namespace AiRpaStudio.Tests;

/// <summary>
/// Синхронизация пакетных активностей палитры при изменении NuGet-пакетов: повторное сканирование
/// без новых типов обязано сбрасывать кэши каталога, удаление пакета — убирать его активности
/// из каталога агента и палитры, а созданные агентом активности — не удалять.
/// </summary>
[TestFixture]
public class NuGetPaletteSyncTests
{
    [TearDown]
    public void TearDown()
    {
        StudioActivityPalette.ResetUserLibraries();
        StudioActivityCatalog.InvalidateDefault();
        ActivityPaletteRegistry.InvalidateDefault();
    }

    [Test]
    public void LoadFromProject_SecondScanWithSameTypes_InvalidatesCatalogCache()
    {
        string directory = MakeTempDir();
        try
        {
            string csproj = WriteProject(directory, "Fake.Activities", "1.0.0");

            var loader = new NuGetActivityPaletteLoader();
            var first = loader.LoadFromProject(csproj);
            Assert.That(first.Added, Is.GreaterThanOrEqualTo(1), "первое сканирование не добавило активность пакета");

            StudioActivityCatalog catalogAfterFirst = StudioActivityCatalog.Default;

            var second = loader.LoadFromProject(csproj);

            Assert.Multiple(() =>
            {
                Assert.That(second.Added, Is.EqualTo(0), "повторное сканирование не должно добавлять тот же тип");
                Assert.That(second.Removed, Is.EqualTo(0));
                Assert.That(StudioActivityCatalog.Default, Is.Not.SameAs(catalogAfterFirst),
                    "кэш каталога не сброшен при повторном сканировании без новых типов");
            });
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Test]
    public void LoadFromProject_AfterPackageRemoved_ClearsPackageActivityFromCatalog()
    {
        string directory = MakeTempDir();
        try
        {
            string csproj = WriteProject(directory, "Fake.Activities", "1.0.0");
            var loader = new NuGetActivityPaletteLoader();
            loader.LoadFromProject(csproj);

            Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Not.Null,
                "до удаления пакета активность должна быть в каталоге");

            File.WriteAllText(Path.Combine(directory, "packages.config"), PackagesConfig(null, null));

            var result = loader.LoadFromProject(csproj);

            Assert.Multiple(() =>
            {
                Assert.That(result.Removed, Is.GreaterThanOrEqualTo(1), "удаление пакета не засчитано");
                Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Null,
                    "удалённая пакетная активность осталась в каталоге агента");
                Assert.That(StudioActivityPalette.Categories().SelectMany(category => category.Types),
                    Does.Not.Contain(typeof(PaletteRefreshProbeActivity)),
                    "удалённая пакетная активность осталась в палитре");
            });
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Test]
    public void LoadFromProject_SwitchToProjectWithoutPackages_ClearsPreviousPackageActivity()
    {
        string directoryA = MakeTempDir();
        string directoryB = MakeTempDir();
        try
        {
            string csprojA = WriteProject(directoryA, "Fake.Activities", "1.0.0");
            var loader = new NuGetActivityPaletteLoader();
            loader.LoadFromProject(csprojA);
            Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Not.Null);

            string csprojB = WriteProject(directoryB, null, null);
            var result = loader.LoadFromProject(csprojB);

            Assert.Multiple(() =>
            {
                Assert.That(result.Removed, Is.GreaterThanOrEqualTo(1));
                Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Null,
                    "активность из предыдущего проекта осталась после смены проекта");
            });
        }
        finally
        {
            TryDeleteDirectory(directoryA);
            TryDeleteDirectory(directoryB);
        }
    }

    [Test]
    public void LoadFromProject_EmptyPackages_DoesNotRemoveCreatedActivity()
    {
        StudioActivityPalette.RegisterUserLibrary(
            StudioActivityPalette.FlowCategory,
            new[] { typeof(PaletteRefreshProbeActivity) });

        string directory = MakeTempDir();
        try
        {
            string csproj = WriteProject(directory, null, null);
            new NuGetActivityPaletteLoader().LoadFromProject(csproj);

            Assert.That(StudioActivityCatalog.Default.Describe(nameof(PaletteRefreshProbeActivity)), Is.Not.Null,
                "созданная агентом активность удалена при пересинхронизации пакетов");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string WriteProject(string directory, string packageId, string version)
    {
        if (!string.IsNullOrWhiteSpace(packageId))
        {
            string packageDir = Path.Combine(directory, "packages", packageId + "." + version, "lib", "net472");
            Directory.CreateDirectory(packageDir);
            File.Copy(typeof(PaletteRefreshProbeActivity).Assembly.Location, Path.Combine(packageDir, packageId + ".dll"));
        }

        string csproj = Path.Combine(directory, "Legacy.csproj");
        File.WriteAllText(csproj, LegacyCsproj());
        File.WriteAllText(Path.Combine(directory, "packages.config"), PackagesConfig(packageId, version));
        return csproj;
    }

    private static string LegacyCsproj() =>
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
        "<PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>";

    private static string PackagesConfig(string packageId, string version)
    {
        string package = string.IsNullOrWhiteSpace(packageId)
            ? string.Empty
            : "<package id=\"" + packageId + "\" version=\"" + version + "\" targetFramework=\"net472\" />";
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages>" + package + "</packages>";
    }

    private static string MakeTempDir()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nugpal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, true); }
        catch (Exception) { /* загруженная DLL может оставаться заблокированной */ }
    }
}