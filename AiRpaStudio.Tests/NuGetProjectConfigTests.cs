using System;
using System.IO;
using NUnit.Framework;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace AiRpaStudio.Tests;

public class NuGetProjectConfigTests
{
    private const string SdkCsproj =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <TargetFramework>net48</TargetFramework>\r\n" +
        "  </PropertyGroup>\r\n" +
        "  <ItemGroup>\r\n" +
        "    <PackageReference Include=\"Markdig\" Version=\"1.4.2\" />\r\n" +
        "    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />\r\n" +
        "  </ItemGroup>\r\n" +
        "  <ItemGroup>\r\n" +
        "    <Compile Include=\"Program.cs\" />\r\n" +
        "  </ItemGroup>\r\n" +
        "</Project>\r\n";

    private const string LegacyCsproj =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>\r\n" +
        "  </PropertyGroup>\r\n" +
        "</Project>\r\n";

    private const string PackagesConfig =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<packages>\r\n" +
        "  <package id=\"Markdig\" version=\"1.4.2\" targetFramework=\"net48\" />\r\n" +
        "  <package id=\"Newtonsoft.Json\" version=\"13.0.2\" targetFramework=\"net48\" />\r\n" +
        "</packages>\r\n";

    [Test]
    public void Load_SdkProject_ReadsPackageReferences()
    {
        string path = WriteTemp(SdkCsproj, "Sdk.csproj");
        try
        {
            var project = NuGetProjectConfig.Load(path);

            Assert.That(project.IsSdkStyle, Is.True);
            Assert.That(project.PackagesConfigPath, Is.Null);
            Assert.That(project.Packages.Count, Is.EqualTo(2));
            Assert.That(NuGetProjectConfig.Contains(project, "Markdig"), Is.True);
            Assert.That(NuGetProjectConfig.Find(project, "Newtonsoft.Json").Version, Is.EqualTo("13.0.3"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    [Test]
    public void Load_LegacyProject_ReadsPackagesConfig()
    {
        string dir = MakeTempDir();
        try
        {
            string csproj = Path.Combine(dir, "Legacy.csproj");
            File.WriteAllText(csproj, LegacyCsproj);
            File.WriteAllText(Path.Combine(dir, "packages.config"), PackagesConfig);

            var project = NuGetProjectConfig.Load(csproj);

            Assert.That(project.IsSdkStyle, Is.False);
            Assert.That(project.PackagesConfigPath, Is.EqualTo(Path.Combine(dir, "packages.config")));
            Assert.That(project.Packages.Count, Is.EqualTo(2));
            Assert.That(NuGetProjectConfig.Find(project, "Markdig").TargetFramework, Is.EqualTo("net48"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void Load_LegacyProject_WithoutPackagesConfig_ReturnsEmpty()
    {
        string path = WriteTemp(LegacyCsproj, "Legacy.csproj");
        try
        {
            var project = NuGetProjectConfig.Load(path);

            Assert.That(project.IsSdkStyle, Is.False);
            Assert.That(project.Packages, Is.Empty);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    [Test]
    public void Load_MissingProject_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => NuGetProjectConfig.Load(Path.Combine(Path.GetTempPath(), "does-not-exist.csproj")));
    }

    [Test]
    public void Save_SdkProject_AddsNewReference()
    {
        string path = WriteTemp(SdkCsproj, "Sdk.csproj");
        try
        {
            var project = NuGetProjectConfig.Load(path);
            NuGetProjectConfig.Add(project, "Serilog", "3.1.1");
            NuGetProjectConfig.Save(project);

            var reloaded = NuGetProjectConfig.Load(path);
            Assert.That(NuGetProjectConfig.Contains(reloaded, "Serilog"), Is.True);
            Assert.That(NuGetProjectConfig.Find(reloaded, "Serilog").Version, Is.EqualTo("3.1.1"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    [Test]
    public void Save_SdkProject_UpdatesVersion()
    {
        string path = WriteTemp(SdkCsproj, "Sdk.csproj");
        try
        {
            var project = NuGetProjectConfig.Load(path);
            NuGetProjectConfig.Update(project, "Markdig", "2.0.0");
            NuGetProjectConfig.Save(project);

            var reloaded = NuGetProjectConfig.Load(path);
            Assert.That(NuGetProjectConfig.Find(reloaded, "Markdig").Version, Is.EqualTo("2.0.0"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    [Test]
    public void Save_SdkProject_RemovesReference()
    {
        string path = WriteTemp(SdkCsproj, "Sdk.csproj");
        try
        {
            var project = NuGetProjectConfig.Load(path);
            NuGetProjectConfig.Remove(project, "Markdig");
            NuGetProjectConfig.Save(project);

            var reloaded = NuGetProjectConfig.Load(path);
            Assert.That(NuGetProjectConfig.Contains(reloaded, "Markdig"), Is.False);
            Assert.That(NuGetProjectConfig.Contains(reloaded, "Newtonsoft.Json"), Is.True);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    [Test]
    public void Save_SdkProject_PreservesOtherElements()
    {
        string path = WriteTemp(SdkCsproj, "Sdk.csproj");
        try
        {
            var project = NuGetProjectConfig.Load(path);
            NuGetProjectConfig.Add(project, "Serilog", "3.1.1");
            NuGetProjectConfig.Save(project);

            string xml = File.ReadAllText(path);
            Assert.That(xml, Does.Contain("<Compile Include=\"Program.cs\""));
            Assert.That(xml, Does.Contain("<TargetFramework>net48</TargetFramework>"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    [Test]
    public void Save_LegacyProject_CreatesPackagesConfig()
    {
        string dir = MakeTempDir();
        try
        {
            string csproj = Path.Combine(dir, "Legacy.csproj");
            File.WriteAllText(csproj, LegacyCsproj);

            var project = NuGetProjectConfig.Load(csproj);
            NuGetProjectConfig.Add(project, "Serilog", "3.1.1");
            NuGetProjectConfig.Save(project);

            string configPath = Path.Combine(dir, "packages.config");
            Assert.That(File.Exists(configPath), Is.True);

            var reloaded = NuGetProjectConfig.Load(csproj);
            Assert.That(reloaded.Packages.Count, Is.EqualTo(1));
            Assert.That(NuGetProjectConfig.Find(reloaded, "Serilog").Version, Is.EqualTo("3.1.1"));
            Assert.That(NuGetProjectConfig.Find(reloaded, "Serilog").TargetFramework, Is.EqualTo("net48"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void Save_LegacyProject_UpdatesAndRemoves()
    {
        string dir = MakeTempDir();
        try
        {
            string csproj = Path.Combine(dir, "Legacy.csproj");
            File.WriteAllText(csproj, LegacyCsproj);
            File.WriteAllText(Path.Combine(dir, "packages.config"), PackagesConfig);

            var project = NuGetProjectConfig.Load(csproj);
            NuGetProjectConfig.Update(project, "Markdig", "2.0.0");
            NuGetProjectConfig.Remove(project, "Newtonsoft.Json");
            NuGetProjectConfig.Save(project);

            var reloaded = NuGetProjectConfig.Load(csproj);
            Assert.That(reloaded.Packages.Count, Is.EqualTo(1));
            Assert.That(NuGetProjectConfig.Find(reloaded, "Markdig").Version, Is.EqualTo("2.0.0"));
            Assert.That(NuGetProjectConfig.Contains(reloaded, "Newtonsoft.Json"), Is.False);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void Add_ExistingPackage_UpdatesVersionInMemory()
    {
        var project = new NuGetProjectPackages { IsSdkStyle = true };
        NuGetProjectConfig.Add(project, "X", "1.0.0");
        NuGetProjectConfig.Add(project, "X", "2.0.0");

        Assert.That(project.Packages.Count, Is.EqualTo(1));
        Assert.That(project.Packages[0].Version, Is.EqualTo("2.0.0"));
    }

    private static string MakeTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nuget_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteTemp(string content, string name)
    {
        string dir = MakeTempDir();
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}

