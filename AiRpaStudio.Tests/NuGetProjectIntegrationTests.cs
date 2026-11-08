using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.Core.Generation;
using RPA.Core.Workflow;
using RPA.Entities;
using RPA.Enums;
using RPA.Models;
using RPA.NuGet.Models;
using RPA.NuGet.Services;
using RPA.NuGet.ViewModels;
using AiRpaStudio.Tests.Workflow;

namespace AiRpaStudio.Tests;

/// <summary>
/// Интеграция установки NuGet-пакета с деревом проекта и палитрой «Активности»: после изменения
/// пакетов References обновляются, а активности из пакета появляются в палитре без перезапуска.
/// </summary>
[TestFixture]
public class NuGetProjectIntegrationTests
{
    [TearDown]
    public void TearDown()
    {
        StudioActivityPalette.ResetUserLibraries();
        StudioActivityCatalog.InvalidateDefault();
        ActivityPaletteRegistry.InvalidateDefault();
        ProjectManager.GetInstance().FSNodes = null;
    }

    [Test]
    public void RefreshVsProject_UpdatesReferencesNode()
    {
        string directory = MakeTempDir();
        try
        {
            string csproj = Path.Combine(directory, "Demo.csproj");
            File.WriteAllText(csproj, SdkCsproj("Newtonsoft.Json", "13.0.3"));

            ProjectManager manager = ProjectManager.GetInstance();
            Assert.That(manager.InitVsProjectTree(csproj).Error, Is.Null);

            FSNode references = ReferencesOf(manager.FSNodes[0]);
            Assert.That(references, Is.Not.Null, "у VS-проекта должен быть узел References");
            Assert.That(ReferenceNames(references), Does.Contain("Newtonsoft.Json (13.0.3)"));

            File.WriteAllText(csproj, SdkCsproj("Serilog", "3.1.1"));

            manager.RefreshVsProject(csproj);

            FSNode updated = ReferencesOf(manager.FSNodes[0]);
            Assert.That(updated, Is.Not.Null, "References исчез после обновления проекта");
            Assert.Multiple(() =>
            {
                Assert.That(ReferenceNames(updated), Does.Contain("Serilog (3.1.1)"),
                    "новый пакет не появился в References после установки");
                Assert.That(ReferenceNames(updated), Does.Not.Contain("Newtonsoft.Json (13.0.3)"),
                    "старый пакет не должен оставаться после перезаписи csproj");
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void NuGetActivityPaletteLoader_LoadsActivitiesFromPackagesConfig()
    {
        string directory = MakeTempDir();
        try
        {
            string packageDir = Path.Combine(directory, "packages", "Fake.Activities.1.0.0", "lib", "net472");
            Directory.CreateDirectory(packageDir);
            File.Copy(typeof(PaletteRefreshProbeActivity).Assembly.Location, Path.Combine(packageDir, "Fake.Activities.dll"));

            string csproj = Path.Combine(directory, "Legacy.csproj");
            File.WriteAllText(csproj,
                "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
                "<PropertyGroup><TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(directory, "packages.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages>" +
                "<package id=\"Fake.Activities\" version=\"1.0.0\" targetFramework=\"net472\" /></packages>");

            NuGetActivityPaletteLoader.NuGetPaletteScanResult result = new NuGetActivityPaletteLoader().LoadFromProject(csproj);

            Assert.Multiple(() =>
            {
                Assert.That(result.Added, Is.GreaterThanOrEqualTo(1), "activity not registered");
                var packageTypes = StudioActivityPalette.Categories()
                    .FirstOrDefault(category => category.Key == "Fake.Activities")?.Types;
                Assert.That(packageTypes.Select(type => type.FullName),
                    Does.Contain(typeof(PaletteRefreshProbeActivity).FullName), "activity not in package category");
            });
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Test]
    public async Task NuGetPanelViewModel_RaisesProjectChangedAfterPackageChange()
    {
        string directory = MakeTempDir();
        try
        {
            string csproj = Path.Combine(directory, "Demo.csproj");
            File.WriteAllText(csproj, SdkCsproj());

            var model = new NuGetPanelViewModel();
            Assert.That(model.LoadProject(csproj), Is.True);

            string raised = null;
            model.ProjectChanged += (sender, path) => raised = path;

            var item = new NuGetPackageItemViewModel(new NuGetPackageInfo { Id = "X", Version = "1.0.0" });
            await model.UninstallItemAsync(item);

            Assert.That(raised, Is.EqualTo(csproj), "событие ProjectChanged не поднято после изменения пакетов");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ResolveAssembly_FindsColocatedDependency()
    {
        string directory = MakeTempDir();
        try
        {
            string source = typeof(NuGetProjectIntegrationTests).Assembly.Location;
            string target = Path.Combine(directory, "DepProbe.dll");
            File.Copy(source, target);

            Assembly resolved = NuGetActivityPaletteLoader.ResolveAssembly(
                "DepProbe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                new List<string> { directory },
                null);

            Assert.That(resolved, Is.Not.Null, "зависимость, лежащая рядом с пакетом, не найдена");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Test]
    public void ProjectManagerControl_CollectionChangedHandler_DoesNotThrow()
    {
        string path = Path.Combine(FindRepositoryRoot(), "AiRpaStudio", "Controls", "ProjectManagerControl.xaml.cs");
        string code = File.ReadAllText(path);

        Assert.That(code, Does.Not.Contain("throw new NotImplementedException()"),
            "обработчик CollectionChanged обязан не бросать исключение — иначе обновление References ломает дерево");
    }

    private static FSNode ReferencesOf(FSNode project)
    {
        return project?.Childs?.FirstOrDefault(node => node.FSNodeType == FSNodeType.References);
    }

    private static string[] ReferenceNames(FSNode references)
    {
        return references.Childs.Select(node => node.Name).ToArray();
    }

    private static string SdkCsproj()
    {
        return SdkCsproj(null, null);
    }

    private static string SdkCsproj(string packageId, string packageVersion)
    {
        string packageRef = string.IsNullOrWhiteSpace(packageId)
            ? string.Empty
            : "<PackageReference Include=\"" + packageId + "\" Version=\"" + packageVersion + "\" />";

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
               "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
               "  <PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup>\r\n" +
               "  <ItemGroup>" + packageRef + "</ItemGroup>\r\n" +
               "</Project>\r\n";
    }

    private static string MakeTempDir()
    {
        string directory = Path.Combine(Path.GetTempPath(), "nugetint_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { Directory.Delete(directory, true); }
        catch (Exception) { /* loaded DLL may stay locked */ }
    }
}
