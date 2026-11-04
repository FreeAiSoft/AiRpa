using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using RPA.VsProject;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-05` — форматы проектов: legacy (ToolsVersion + TargetFrameworkVersion + packages.config)
/// и SDK-style (Sdk-атрибут, &lt;Sdk Name&gt;, &lt;Import Sdk&gt;), включая мультитаргетинг
/// и восстановление пакетов перед сборкой.
/// </summary>
[TestFixture]
public class VsProjectFormatsTests
{
    private const string LegacyProject =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <AssemblyName>LegacyLib</AssemblyName>\r\n" +
        "    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>\r\n" +
        "    <ProjectTypeGuids>{32f31d43-81cc-4c15-9de6-3fc5453562b6};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>\r\n" +
        "  </PropertyGroup>\r\n" +
        "</Project>";

    private const string PackagesConfig =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<packages>\r\n" +
        "  <package id=\"Newtonsoft.Json\" version=\"13.0.3\" targetFramework=\"net472\" />\r\n" +
        "  <package id=\"NLog\" version=\"4.7.15\" targetFramework=\"net472\" />\r\n" +
        "</packages>";

    [Test]
    public void LegacyProject_ReadsPackagesConfigAndNormalizesTfm()
    {
        string directory = CreateTempDirectory();

        try
        {
            string csproj = Path.Combine(directory, "LegacyLib.csproj");
            File.WriteAllText(csproj, LegacyProject);
            File.WriteAllText(Path.Combine(directory, "packages.config"), PackagesConfig);

            VsProjectInfo info = VsProjectParser.Parse(csproj);

            Assert.That(info.IsSdkStyle, Is.False, "legacy-проект не должен считаться SDK-style");
            Assert.That(info.TargetFrameworks, Is.EqualTo(new[] { "net472" }));
            Assert.That(info.PrimaryTargetFramework, Is.EqualTo("net472"));
            Assert.That(info.IsWorkflowProject, Is.True, "ProjectTypeGuids содержит WF GUID");
            Assert.That(info.NeedsRestore, Is.True);
            Assert.That(info.Packages.Count, Is.EqualTo(2));

            VsProjectPackage json = info.Packages.Single(p => p.Id == "Newtonsoft.Json");
            Assert.That(json.Version, Is.EqualTo("13.0.3"));
            Assert.That(json.TargetFramework, Is.EqualTo("net472"));
            Assert.That(json.Source, Is.EqualTo(VsPackageSource.PackagesConfig));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void LegacyProject_WithoutPackagesConfig_HasNoPackages()
    {
        string directory = CreateTempDirectory();

        try
        {
            string csproj = Path.Combine(directory, "PlainLib.csproj");
            File.WriteAllText(csproj, LegacyProject);

            VsProjectInfo info = VsProjectParser.Parse(csproj);

            Assert.That(info.Packages, Is.Empty);
            Assert.That(info.NeedsRestore, Is.False);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SdkAttributeProject_ReadsPackageReferenceBothShapes()
    {
        string directory = CreateTempDirectory();

        try
        {
            string csproj = Path.Combine(directory, "SdkLib.csproj");
            File.WriteAllText(csproj,
                "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
                "  <PropertyGroup>\r\n" +
                "    <TargetFramework>net48</TargetFramework>\r\n" +
                "  </PropertyGroup>\r\n" +
                "  <ItemGroup>\r\n" +
                "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\r\n" +
                "    <PackageReference Include=\"NUnit\">\r\n" +
                "      <Version>3.14.0</Version>\r\n" +
                "    </PackageReference>\r\n" +
                "  </ItemGroup>\r\n" +
                "</Project>");

            VsProjectInfo info = VsProjectParser.Parse(csproj);

            Assert.That(info.IsSdkStyle, Is.True);
            Assert.That(info.TargetFrameworks, Is.EqualTo(new[] { "net48" }));
            Assert.That(info.IsWorkflowProject, Is.False);
            Assert.That(info.Packages.Count, Is.EqualTo(2));
            Assert.That(info.Packages.Single(p => p.Id == "Newtonsoft.Json").Version, Is.EqualTo("13.0.3"));
            Assert.That(info.Packages.Single(p => p.Id == "NUnit").Version, Is.EqualTo("3.14.0"));
            Assert.That(info.Packages.All(p => p.Source == VsPackageSource.PackageReference), Is.True);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SdkElementProject_IsDetectedAsSdkStyle()
    {
        string directory = CreateTempDirectory();

        try
        {
            string csproj = Path.Combine(directory, "SdkElement.csproj");
            File.WriteAllText(csproj,
                "<Project>\r\n" +
                "  <Sdk Name=\"Microsoft.NET.Sdk\" />\r\n" +
                "  <PropertyGroup>\r\n" +
                "    <TargetFramework>net472</TargetFramework>\r\n" +
                "  </PropertyGroup>\r\n" +
                "</Project>");

            Assert.That(VsProjectParser.Parse(csproj).IsSdkStyle, Is.True);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SdkImportProject_IsDetectedAsSdkStyle()
    {
        string directory = CreateTempDirectory();

        try
        {
            string csproj = Path.Combine(directory, "SdkImport.csproj");
            File.WriteAllText(csproj,
                "<Project ToolsVersion=\"15.0\">\r\n" +
                "  <Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\" />\r\n" +
                "  <PropertyGroup>\r\n" +
                "    <TargetFramework>net472</TargetFramework>\r\n" +
                "  </PropertyGroup>\r\n" +
                "</Project>");

            Assert.That(VsProjectParser.Parse(csproj).IsSdkStyle, Is.True);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void MultiTargetingProject_KeepsAllFrameworks()
    {
        string directory = CreateTempDirectory();

        try
        {
            string csproj = Path.Combine(directory, "Multi.csproj");
            File.WriteAllText(csproj,
                "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
                "  <PropertyGroup>\r\n" +
                "    <TargetFrameworks>net472;net48</TargetFrameworks>\r\n" +
                "  </PropertyGroup>\r\n" +
                "</Project>");

            VsProjectInfo info = VsProjectParser.Parse(csproj);

            Assert.That(info.IsSdkStyle, Is.True);
            Assert.That(info.TargetFrameworks, Is.EqualTo(new[] { "net472", "net48" }));
            Assert.That(info.PrimaryTargetFramework, Is.Null, "у мультитаргета нет одного TFM");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void PackageWithoutVersion_ResolvesCentralPackageVersion()
    {
        string directory = CreateTempDirectory();

        try
        {
            File.WriteAllText(Path.Combine(directory, "Directory.Packages.props"),
                "<Project>\r\n" +
                "  <ItemGroup>\r\n" +
                "    <PackageVersion Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\r\n" +
                "  </ItemGroup>\r\n" +
                "</Project>");

            string csproj = Path.Combine(directory, "CentralLib.csproj");
            File.WriteAllText(csproj,
                "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
                "  <PropertyGroup>\r\n" +
                "    <TargetFramework>net48</TargetFramework>\r\n" +
                "  </PropertyGroup>\r\n" +
                "  <ItemGroup>\r\n" +
                "    <PackageReference Include=\"Newtonsoft.Json\" />\r\n" +
                "  </ItemGroup>\r\n" +
                "</Project>");

            VsProjectInfo info = VsProjectParser.Parse(csproj);
            VsProjectPackage package = info.Packages.Single();

            Assert.That(package.Version, Is.EqualTo("13.0.3"));
            Assert.That(package.IsCentralVersion, Is.True);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void RestoreArguments_RestoresPackagesConfigAndBuilds()
    {
        string restoreArgs = VsProjectBuilder.RestoreArguments(@"C:\dir\Legacy.csproj", "Release");
        string buildArgs = VsProjectBuilder.BuildArguments(@"C:\dir\Legacy.csproj", "Release");

        Assert.That(restoreArgs, Does.Contain("/t:Restore"));
        Assert.That(restoreArgs, Does.Contain("/p:RestorePackagesConfig=true"));
        Assert.That(restoreArgs, Does.Contain("/p:Configuration=Release"));

        // Обычная сборка остаётся без restore: восстановление — отдельный, освещённый в логе шаг.
        Assert.That(buildArgs, Does.Not.Contain("/t:Restore"));
    }
    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vs_formats_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

}
