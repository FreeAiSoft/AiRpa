using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using RPA.VsProject;
using RPA.VsTemplate;

using Agent.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-04` — explicit MSBuild toolchain selection.
/// <para>
/// The locator must be the single source of truth for "which MSBuild.exe builds this": an explicit
/// Visual Studio generation (2022 / 2026) is honoured or reported as missing, a fallback is never silent,
/// and the resolved path/version is available for the build log. The tests below cover the lookup logic
/// with an injected environment (no dependency on the developer machine) plus a real end-to-end build with
/// each installed toolchain.
/// </para>
/// </summary>
public class MsBuildLocatorTests
{
    private const string ProgramFilesRoot = @"C:\Program Files";
    private const string ProgramFilesX86Root = @"C:\Program Files (x86)";
    private const string VswhereFile = @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";

    [Test]
    public void Resolve_Auto_VswhereHit_ReturnsPathAndToolchain()
    {
        var environment = new FakeEnvironment();
        string expected = MsBuildExe("18");
        environment.Files.Add(expected);
        environment.Versions[expected] = "18.10.1.42706";
        environment.VswhereHandler = _ => expected + Environment.NewLine;

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Auto, allowFallback: true);

        Assert.That(location.Found, Is.True, location.Describe());
        Assert.That(location.Path, Is.EqualTo(expected));
        Assert.That(location.Toolchain, Is.EqualTo(MsBuildToolchain.Vs2026));
        Assert.That(location.Source, Is.EqualTo("vswhere"));
        Assert.That(location.Version, Is.EqualTo("18.10.1.42706"));
        Assert.That(location.IsFallback, Is.False);
        Assert.That(location.Describe(), Does.Contain("Visual Studio 2026"));
    }

    [Test]
    public void Resolve_ExplicitVs2022_AsksVswhereFor17Range()
    {
        var environment = new FakeEnvironment();
        string expected = MsBuildExe("2022");
        environment.Files.Add(expected);
        environment.Versions[expected] = "17.11.35312.102";
        environment.VswhereHandler = _ => expected + Environment.NewLine;

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Vs2022, allowFallback: true);

        Assert.That(location.Found, Is.True, location.Describe());
        Assert.That(location.Toolchain, Is.EqualTo(MsBuildToolchain.Vs2022));
        Assert.That(environment.VswhereCalls, Has.Count.EqualTo(1));
        Assert.That(environment.VswhereCalls[0], Does.Contain("-version \"[17.0,18.0)\""));
        Assert.That(environment.VswhereCalls[0], Does.Contain(MsBuildLocator.MsBuildComponent));
    }

    [Test]
    public void Resolve_ExplicitVs2026_AsksVswhereFor18RangeWithPrerelease()
    {
        var environment = new FakeEnvironment();
        string expected = MsBuildExe("18");
        environment.Files.Add(expected);
        environment.VswhereHandler = _ => expected + Environment.NewLine;

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Vs2026, allowFallback: true);

        Assert.That(location.Toolchain, Is.EqualTo(MsBuildToolchain.Vs2026));
        Assert.That(environment.VswhereCalls[0], Does.Contain("-version \"[18.0,19.0)\""));
        Assert.That(environment.VswhereCalls[0], Does.Contain("-prerelease"));
    }

    [Test]
    public void Resolve_ExplicitToolchainNotInstalled_FallsBackAndSaysSo()
    {
        var environment = new FakeEnvironment();
        string newest = MsBuildExe("18");
        environment.Files.Add(newest);
        environment.Versions[newest] = "18.10.1.42706";

        // Only the 2026 range yields a path; the 2022 range yields nothing.
        environment.VswhereHandler = arguments =>
            arguments.Contains("[17.0,18.0)") ? string.Empty : newest + Environment.NewLine;

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Vs2022, allowFallback: true);

        Assert.That(location.Found, Is.True, location.Describe());
        Assert.That(location.Toolchain, Is.EqualTo(MsBuildToolchain.Vs2026));
        Assert.That(location.Requested, Is.EqualTo(MsBuildToolchain.Vs2022));
        Assert.That(location.IsFallback, Is.True);
        Assert.That(location.Diagnostic, Does.Contain("Visual Studio 2022"));
        Assert.That(location.Describe(), Does.Contain("requested Visual Studio 2022"));
    }

    [Test]
    public void Resolve_ExplicitToolchainNotInstalled_NoFallback_ReturnsNotFound()
    {
        var environment = new FakeEnvironment();
        string newest = MsBuildExe("18");
        environment.Files.Add(newest);
        environment.VswhereHandler = arguments =>
            arguments.Contains("[17.0,18.0)") ? string.Empty : newest + Environment.NewLine;

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Vs2022, allowFallback: false);

        Assert.That(location.Found, Is.False);
        Assert.That(location.Path, Is.Null);
        Assert.That(location.Diagnostic, Does.Contain("Visual Studio 2022"));
        Assert.That(location.Describe(), Does.StartWith("MSBuild not found"));
    }

    [Test]
    public void Resolve_Auto_VswhereReturnsNothing_FallsBackToWellKnownFolder()
    {
        var environment = new FakeEnvironment();
        string expected = MsBuildExe("2022");
        environment.Files.Add(expected);
        environment.Versions[expected] = "17.11.35312.102";

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Auto, allowFallback: true);

        Assert.That(location.Found, Is.True, location.Describe());
        Assert.That(location.Path, Is.EqualTo(expected));
        Assert.That(location.Source, Is.EqualTo("well-known"));
        Assert.That(environment.VswhereCalls, Has.Count.EqualTo(1));
    }

    [Test]
    public void Resolve_Auto_NoVswhereFile_FallsBackToWellKnownFolderAndSaysSo()
    {
        var environment = new FakeEnvironment();
        environment.Files.Remove(VswhereFile);
        string expected = MsBuildExe("18");
        environment.Files.Add(expected);

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Auto, allowFallback: true);

        Assert.That(location.Found, Is.True, location.Describe());
        Assert.That(location.Source, Is.EqualTo("well-known"));
        Assert.That(environment.VswhereCalls, Is.Empty);
        Assert.That(location.Diagnostic, Does.Contain("vswhere.exe not found"));
    }


    [Test]
    public void Resolve_NoVisualStudioAtAll_UsesFrameworkEngineAndWarnsAboutSdkStyle()
    {
        var environment = new FakeEnvironment();
        environment.Variables["WINDIR"] = @"C:\Windows";
        string framework = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe";
        environment.Files.Add(framework);
        environment.Versions[framework] = "4.8.9037.0";

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Auto, allowFallback: true);

        Assert.That(location.Found, Is.True);
        Assert.That(location.Source, Is.EqualTo("framework"));
        Assert.That(location.DisplayName, Does.Contain("SDK-style projects unsupported"));
    }

    [Test]
    public void Resolve_NothingInstalled_ReturnsNotFoundWithTrace()
    {
        var environment = new FakeEnvironment();

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Auto, allowFallback: true);

        Assert.That(location.Found, Is.False);
        Assert.That(location.Path, Is.Null);
        Assert.That(location.Diagnostic, Does.Contain("returned no path"));
        Assert.That(location.Diagnostic, Does.Contain("Well-known"));
        Assert.That(location.Describe(), Does.Contain("MSBuild not found"));
    }

    [Test]
    public void Resolve_VswhereReturnsMissingFile_KeepsLookingAndReportsTrace()
    {
        var environment = new FakeEnvironment();
        string ghost = MsBuildExe("18");
        environment.VswhereHandler = _ => ghost + Environment.NewLine;

        var location = MsBuildLocator.Resolve(environment, MsBuildToolchain.Auto, allowFallback: true);

        Assert.That(location.Found, Is.False);
        Assert.That(location.Diagnostic, Does.Contain("does not exist"));
    }

    [TestCase(@"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe", null, MsBuildToolchain.Vs2022)]
    [TestCase(@"C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe", null, MsBuildToolchain.Vs2026)]
    [TestCase(@"C:\Tools\MSBuild.exe", "17.11.35312.102", MsBuildToolchain.Vs2022)]
    [TestCase(@"C:\Tools\MSBuild.exe", "18.10.1.42706", MsBuildToolchain.Vs2026)]
    [TestCase(@"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe", "4.8.9037.0", MsBuildToolchain.Auto)]
    [TestCase(null, null, MsBuildToolchain.Auto)]
    public void Classify_PathOrVersion_ReturnsToolchain(string path, string version, MsBuildToolchain expected)
    {
        Assert.That(MsBuildLocator.Classify(path, version), Is.EqualTo(expected));
    }

    [TestCase("auto", MsBuildToolchain.Auto)]
    [TestCase("", MsBuildToolchain.Auto)]
    [TestCase(null, MsBuildToolchain.Auto)]
    [TestCase("Vs2022", MsBuildToolchain.Vs2022)]
    [TestCase("vs2022", MsBuildToolchain.Vs2022)]
    [TestCase("2022", MsBuildToolchain.Vs2022)]
    [TestCase("17", MsBuildToolchain.Vs2022)]
    [TestCase("Vs2026", MsBuildToolchain.Vs2026)]
    [TestCase("vs-2026", MsBuildToolchain.Vs2026)]
    [TestCase("18", MsBuildToolchain.Vs2026)]
    public void ParseToolchain_Name_MapsToEnum(string value, MsBuildToolchain expected)
    {
        Assert.That(MsBuildLocator.ParseToolchain(value), Is.EqualTo(expected));
    }

    [Test]
    public void BuildDotNetSdkOverrides_SdkNotOnPath_AddsRootAndPrependsPath()
    {
        var overrides = MsBuildLocator.BuildDotNetSdkOverrides(
            @"C:\Program Files\dotnet",
            @"C:\Windows\system32",
            null);

        Assert.That(overrides["DOTNET_ROOT"], Is.EqualTo(@"C:\Program Files\dotnet"));
        Assert.That(overrides["PATH"], Does.StartWith(@"C:\Program Files\dotnet;"));
        Assert.That(overrides["PATH"], Does.Contain(@"C:\Windows\system32"));
    }

    [Test]
    public void BuildDotNetSdkOverrides_SdkAlreadyOnPath_DoesNotTouchPath()
    {
        var overrides = MsBuildLocator.BuildDotNetSdkOverrides(
            @"C:\Program Files\dotnet\",
            @"C:\Windows\system32;C:\Program Files\dotnet",
            null);

        Assert.That(overrides.ContainsKey("PATH"), Is.False);
        Assert.That(overrides["DOTNET_ROOT"], Is.EqualTo(@"C:\Program Files\dotnet"));
    }

    [Test]
    public void BuildDotNetSdkOverrides_ExistingDotNetRoot_IsRespected()
    {
        var overrides = MsBuildLocator.BuildDotNetSdkOverrides(
            @"C:\Program Files\dotnet",
            @"C:\Windows\system32",
            @"D:\custom\dotnet");

        Assert.That(overrides.ContainsKey("DOTNET_ROOT"), Is.False);
        Assert.That(overrides["PATH"], Does.StartWith(@"C:\Program Files\dotnet;"));
    }

    [Test]
    public void BuildDotNetSdkOverrides_NoSdkFolder_ReturnsNothing()
    {
        Assert.That(MsBuildLocator.BuildDotNetSdkOverrides(null, @"C:\Windows", null), Is.Empty);
        Assert.That(MsBuildLocator.BuildDotNetSdkOverrides("   ", @"C:\Windows", null), Is.Empty);
    }

    [Test]
    public void GetEnvironmentOverrides_DetectsDotNetSdkInProgramFiles()
    {
        var environment = new FakeEnvironment();
        environment.Variables["PATH"] = @"C:\Windows\system32";
        environment.Files.Add(@"C:\Program Files\dotnet\dotnet.exe");

        var overrides = MsBuildLocator.GetEnvironmentOverrides(
            new MsBuildLocation { Found = true, Path = MsBuildExe("18") },
            environment);

        Assert.That(overrides["DOTNET_ROOT"], Is.EqualTo(@"C:\Program Files\dotnet"));
        Assert.That(overrides["PATH"], Does.StartWith(@"C:\Program Files\dotnet;"));
    }

    [Test]
    public void Resolve_RealMachine_FindsExistingMsBuildExe()
    {
        var location = MsBuildLocator.Resolve();

        Assert.That(location.Found, Is.True, location.Describe());
        Assert.That(Path.GetFileName(location.Path), Is.EqualTo("MSBuild.exe").IgnoreCase);
        Assert.That(File.Exists(location.Path), Is.True);
        Assert.That(location.Version, Is.Not.Null.And.Not.Empty);
        Assert.That(location.Describe(), Does.Contain(location.Path));
    }

    [TestCase(MsBuildToolchain.Vs2022)]
    [TestCase(MsBuildToolchain.Vs2026)]
    public void Resolve_RealMachine_ExplicitToolchainIsHonoured(MsBuildToolchain requested)
    {
        var location = MsBuildLocator.Resolve(requested);

        if (!location.Found)
        {
            Assert.Ignore(MsBuildLocator.DescribeToolchain(requested) + " is not installed on this machine: " + location.Diagnostic);
        }

        Assert.That(location.Toolchain, Is.EqualTo(requested), location.Describe());
        Assert.That(location.IsFallback, Is.False, location.Describe());
        Assert.That(File.Exists(location.Path), Is.True);
        Assert.That(MsBuildLocator.Classify(location.Path, location.Version), Is.EqualTo(requested));
    }

    [TestCase(MsBuildToolchain.Vs2022)]
    [TestCase(MsBuildToolchain.Vs2026)]
    public void BuildWithToolchain_InstalledToolchain_BuildsGeneratedProject(MsBuildToolchain requested)
    {
        var location = MsBuildLocator.Resolve(requested);
        if (!location.Found)
        {
            Assert.Ignore(MsBuildLocator.DescribeToolchain(requested) + " is not installed on this machine.");
        }

        string output = Path.Combine(Path.GetTempPath(), "vs_toolchain_" + Guid.NewGuid().ToString("N"));
        try
        {
            var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
            VsTemplateInstantiator.Instantiate(template, new VsTemplateInstantiationRequest
            {
                ProjectName = "ToolchainTest",
                SafeProjectName = "ToolchainTest",
                RootNamespace = "ToolchainTest",
                TargetFrameworkVersion = "4.8",
                OutputDirectory = output
            });

            var result = VsProjectBuilder.BuildWithToolchain(Path.Combine(output, "ToolchainTest.csproj"), "Debug", requested);

            Assert.That(result.Success, Is.True, result.Output);
            Assert.That(result.MsBuildPath, Is.EqualTo(location.Path));
            Assert.That(result.ToolchainFallback, Is.False);
            Assert.That(result.ToolchainVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(File.Exists(Path.Combine(output, "bin", "Debug", "ToolchainTest.dll")), Is.True);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [Test]
    public void BuildWithToolchain_ReportsMissingProjectBeforeStartingMsBuild()
    {
        var result = VsProjectBuilder.BuildWithToolchain(@"C:\no\such\project.csproj", "Debug", MsBuildToolchain.Auto);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Errors, Has.Some.Contain("not found"));
    }

    [Test]
    public void FindMSBuild_ExplicitToolchain_ReturnsPathThatExists()
    {
        var location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Vs2026);

        Assert.That(location, Is.Not.Null);
        Assert.That(location.Path, Is.Not.Null.And.Not.Empty);
        if (location.Found)
        {
            Assert.That(File.Exists(location.Path), Is.True);
        }
    }

    [Test]
    public void BuildWithToolchain_LogsResolvedMsBuildPath()
    {
        string output = Path.Combine(Path.GetTempPath(), "vs_log_" + Guid.NewGuid().ToString("N"));
        try
        {
            var template = VsTemplateParser.Parse(Path.Combine(TemplateRoot, "ActivityLibrary2010", "ActivityLibrary.vstemplate"));
            VsTemplateInstantiator.Instantiate(template, new VsTemplateInstantiationRequest
            {
                ProjectName = "LogTest",
                SafeProjectName = "LogTest",
                RootNamespace = "LogTest",
                TargetFrameworkVersion = "4.8",
                OutputDirectory = output
            });

            var log = new List<string>();
            var result = VsProjectBuilder.BuildWithToolchain(Path.Combine(output, "LogTest.csproj"), "Debug", MsBuildToolchain.Auto, log.Add);

            Assert.That(result.Success, Is.True, result.Output);
            Assert.That(log, Is.Not.Empty);
            Assert.That(log[0], Does.Contain("MSBuild"));
            Assert.That(log[0], Does.Contain(result.MsBuildPath));
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    private static string MsBuildExe(string versionFolder)
    {
        return Path.Combine(ProgramFilesRoot, "Microsoft Visual Studio", versionFolder, "Enterprise", "MSBuild", "Current", "Bin", "MSBuild.exe");
    }

    private static string TemplateRoot
    {
        get
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "AiRpaStudio", "VSTemplates", "ActivityLibrary2010", "ActivityLibrary.vstemplate");
                if (File.Exists(candidate))
                    return Path.Combine(dir, "AiRpaStudio", "VSTemplates");

                dir = Path.GetDirectoryName(dir);
            }

            Assert.Fail("Could not locate the VSTemplates source folder.");
            return null;
        }
    }

    /// <summary>In-memory machine: vswhere output, files and environment are fully controlled by the test.</summary>
    private sealed class FakeEnvironment : IMsBuildEnvironment
    {
        public string ProgramFiles { get; set; } = ProgramFilesRoot;

        public string ProgramFilesX86 { get; set; } = ProgramFilesX86Root;

        public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Versions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> VswhereCalls { get; } = new();

        /// <summary>vswhere output as a function of its arguments (empty = nothing found).</summary>
        public Func<string, string> VswhereHandler { get; set; } = _ => string.Empty;

        public FakeEnvironment()
        {
            Files.Add(VswhereFile);
        }

        public string GetEnvironmentVariable(string name)
        {
            return Variables.TryGetValue(name, out string value) ? value : null;
        }

        public string RunVswhere(string arguments)
        {
            VswhereCalls.Add(arguments);
            return VswhereHandler(arguments);
        }

        public bool FileExists(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Files.Contains(path);
        }

        public string GetFileVersion(string path)
        {
            return Versions.TryGetValue(path, out string value) ? value : null;
        }
    }
}
