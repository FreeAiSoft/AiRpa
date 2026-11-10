using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

/// <summary>
/// Tests for the Tools -> "Check Activity Library Compatibility" feature:
/// <see cref="ActivityCompatibilityChecker"/> and <see cref="ActivityCompatibilityFixer"/>.
/// </summary>
[TestFixture]
public class ActivityCompatibilityCheckerTests
{
    private const string WorkflowGuid = "{32f31d43-81cc-4c15-9de6-3fc5453562b6}";

    [Test]
    public void Check_CompatibleNet472WorkflowProject_HasNoErrors()
    {
        string dir = MakeTempDir();
        try
        {
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            Assert.That(report.IsCompatible, Is.True);
            Assert.That(report.Issues, Is.Empty);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_Net48Project_ReportsTfmError()
    {
        string dir = MakeTempDir();
        try
        {
            string csproj = WriteProject(dir, tfm: "v4.8", workflow: true);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            Assert.That(report.Errors.Any(i => i.Code == "TFM_WRONG"), Is.True);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_NonWorkflowProject_ReportsTypeError()
    {
        string dir = MakeTempDir();
        try
        {
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: false);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            Assert.That(report.Errors.Any(i => i.Code == "NOT_WORKFLOW_PROJECT"), Is.True);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_PackageVersionMismatch_ReportsErrorWithPinnedHint()
    {
        string dir = MakeTempDir();
        try
        {
            string packagesConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages>" +
                                    "<package id=\"System.Text.Json\" version=\"8.0.5\" targetFramework=\"net472\" /></packages>";
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true, packagesConfig: packagesConfig);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            ActivityCompatibilityIssue issue = report.Errors.SingleOrDefault(i => i.Code == "PACKAGE_VERSION");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.PinnedVersion, Is.EqualTo("8.0.6"));
            Assert.That(issue.CanFix, Is.True);
            Assert.That(issue.FixTarget, Is.EqualTo(ActivityCompatibilityFixTarget.PackagesConfigVersion));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_HintPathVersionMismatch_ReportsError()
    {
        string dir = MakeTempDir();
        try
        {
            string reference = "<Reference Include=\"System.Text.Json\"><HintPath>..\\packages\\System.Text.Json.8.0.5\\lib\\net462\\System.Text.Json.dll</HintPath></Reference>";
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true, referenceXml: reference);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            Assert.That(report.Errors.Any(i => i.Code == "HINTPATH_VERSION"), Is.True);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_HintPathWithSharedIdPrefix_DoesNotFalsePositive()
    {
        string dir = MakeTempDir();
        try
        {
            // Пакет Microsoft.Extensions.Logging.Abstractions не должен матчиться как
            // Microsoft.Extensions.Logging (префикс id): версия пакета — сегмент после
            // последней точки перед \lib, а не всё, что идёт после id-префикса.
            string reference = "<Reference Include=\"Microsoft.Extensions.Logging.Abstractions\"><HintPath>..\\packages\\Microsoft.Extensions.Logging.Abstractions.8.0.0\\lib\\net462\\Microsoft.Extensions.Logging.Abstractions.dll</HintPath></Reference>";
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true, referenceXml: reference);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            Assert.That(report.Errors.Any(i => i.Code == "HINTPATH_VERSION"), Is.False);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_HintPathVersionMismatchWithDottedId_ReportsCorrectPackage()
    {
        string dir = MakeTempDir();
        try
        {
            string reference = "<Reference Include=\"Microsoft.Extensions.Logging.Abstractions\"><HintPath>..\\packages\\Microsoft.Extensions.Logging.Abstractions.8.0.1\\lib\\net462\\Microsoft.Extensions.Logging.Abstractions.dll</HintPath></Reference>";
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true, referenceXml: reference);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            ActivityCompatibilityIssue issue = report.Errors.SingleOrDefault(i => i.Code == "HINTPATH_VERSION");
            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.PackageId, Is.EqualTo("Microsoft.Extensions.Logging.Abstractions"));
            Assert.That(issue.CurrentVersion, Is.EqualTo("8.0.1"));
            Assert.That(issue.PinnedVersion, Is.EqualTo("8.0.0"));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_DesignerAssemblyReference_ReportsError()
    {
        string dir = MakeTempDir();
        try
        {
            string reference = "<Reference Include=\"Microsoft.Windows.Design.Interaction\" />";
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true, referenceXml: reference);
            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);

            Assert.That(report.Errors.Any(i => i.Code == "DESIGNER_ASSEMBLY_REFERENCE"), Is.True);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_ToolboxBrowsableAttribute_ReportsError()
    {
        string dir = MakeTempDir();
        try
        {
            WriteProject(dir, tfm: "v4.7.2", workflow: true);
            File.WriteAllText(Path.Combine(dir, "BadActivity.cs"), "public class BadActivity { [ToolboxBrowsable] public int X; }");

            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(Path.Combine(dir, "TestLib.csproj"));
            Assert.That(report.Errors.Any(i => i.Code == "TOOLBOX_BROWSABLE_ATTRIBUTE"), Is.True);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Check_NuspecVersionMismatchAndRange_ReportErrors()
    {
        string dir = MakeTempDir();
        try
        {
            WriteProject(dir, tfm: "v4.7.2", workflow: true);
            string nuspec = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                            "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">" +
                            "<metadata><dependencies>" +
                            "<dependency id=\"System.Text.Json\" version=\"8.0.5\" />" +
                            "<dependency id=\"System.Memory\" version=\"[4.0.0,)\" />" +
                            "</dependencies></metadata></package>";
            File.WriteAllText(Path.Combine(dir, "TestLib.nuspec"), nuspec);

            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(Path.Combine(dir, "TestLib.csproj"));

            Assert.That(report.Errors.Any(i => i.Code == "NUSPEC_VERSION"), Is.True);
            Assert.That(report.Errors.Any(i => i.Code == "NUSPEC_VERSION_RANGE"), Is.True);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Fixer_UpdatesPackagesConfigHintPathAndNuspec()
    {
        string dir = MakeTempDir();
        try
        {
            string packagesConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages>" +
                                    "<package id=\"System.Text.Json\" version=\"8.0.5\" targetFramework=\"net472\" /></packages>";
            string reference = "<Reference Include=\"System.Text.Json\"><HintPath>..\\packages\\System.Text.Json.8.0.5\\lib\\net462\\System.Text.Json.dll</HintPath></Reference>";
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true, referenceXml: reference, packagesConfig: packagesConfig);
            string nuspecPath = Path.Combine(dir, "TestLib.nuspec");
            File.WriteAllText(nuspecPath, "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\">" +
                "<metadata><dependencies><dependency id=\"System.Text.Json\" version=\"8.0.5\" /></dependencies></metadata></package>");

            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);
            ActivityCompatibilityFixResult result = new ActivityCompatibilityFixer().ApplyFixes(report);

            Assert.That(result.FixedCount, Is.EqualTo(3));
            Assert.That(result.ChangedFiles, Does.Contain(Path.Combine(dir, "packages.config")));
            Assert.That(result.ChangedFiles, Does.Contain(csproj));
            Assert.That(result.ChangedFiles, Does.Contain(nuspecPath));

            Assert.That(File.ReadAllText(Path.Combine(dir, "packages.config")), Does.Contain("version=\"8.0.6\""));
            Assert.That(File.ReadAllText(csproj), Does.Contain("System.Text.Json.8.0.6"));
            Assert.That(File.ReadAllText(nuspecPath), Does.Contain("version=\"8.0.6\""));

            ActivityCompatibilityReport refreshed = new ActivityCompatibilityChecker().Check(csproj);
            Assert.That(refreshed.IsCompatible, Is.True);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Test]
    public void Fixer_PreservesSingleUtf8Bom()
    {
        string dir = MakeTempDir();
        try
        {
            string reference = "<Reference Include=\"System.Text.Json\"><HintPath>..\\packages\\System.Text.Json.8.0.5\\lib\\net462\\System.Text.Json.dll</HintPath></Reference>";
            string csproj = WriteProject(dir, tfm: "v4.7.2", workflow: true, referenceXml: reference);
            // Save the original project with a single UTF-8 BOM, as Visual Studio does for legacy .csproj.
            File.WriteAllText(csproj, File.ReadAllText(csproj), new System.Text.UTF8Encoding(true));

            ActivityCompatibilityReport report = new ActivityCompatibilityChecker().Check(csproj);
            ActivityCompatibilityFixResult result = new ActivityCompatibilityFixer().ApplyFixes(report);

            Assert.That(result.FixedCount, Is.EqualTo(1));

            byte[] bytes = File.ReadAllBytes(csproj);
            Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(3));
            Assert.That(bytes[0], Is.EqualTo(0xEF));
            Assert.That(bytes[1], Is.EqualTo(0xBB));
            Assert.That(bytes[2], Is.EqualTo(0xBF));
            // A single BOM must be immediately followed by '<': a second BOM (or U+FEFF) would be 0xEF here.
            Assert.That(bytes[3], Is.EqualTo((byte)'<'));
            // Decoded without the preamble, the text must not start with U+FEFF and must keep a valid XML root.
            string fixedText = new System.Text.UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            Assert.That(fixedText, Does.StartWith("<Project"));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static string WriteProject(
        string dir,
        string tfm,
        bool workflow,
        string referenceXml = "",
        string packagesConfig = null)
    {
        string csproj = Path.Combine(dir, "TestLib.csproj");
        string guids = workflow
            ? "<ProjectTypeGuids>" + WorkflowGuid + "</ProjectTypeGuids>"
            : string.Empty;

        string xml = "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">" +
                     "<PropertyGroup>" + guids +
                     "<TargetFrameworkVersion>" + tfm + "</TargetFrameworkVersion>" +
                     "<AssemblyName>TestLib</AssemblyName></PropertyGroup>" +
                     "<ItemGroup>" + referenceXml + "</ItemGroup></Project>";

        File.WriteAllText(csproj, xml);

        if (packagesConfig != null)
        {
            File.WriteAllText(Path.Combine(dir, "packages.config"), packagesConfig);
        }

        return csproj;
    }

    private static string MakeTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "compat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (Exception)
        {
            // Best-effort cleanup.
        }
    }
}
