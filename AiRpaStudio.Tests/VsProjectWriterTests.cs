using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

[TestFixture]
public class VsProjectWriterTests
{
    private const string LegacyCsproj =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Project ToolsVersion=\"15.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "    <PropertyGroup>\r\n" +
        "        <ProjectGuid>{11111111-1111-1111-1111-111111111111}</ProjectGuid>\r\n" +
        "        <OutputType>Library</OutputType>\r\n" +
        "        <RootNamespace>MyLib</RootNamespace>\r\n" +
        "        <AssemblyName>MyLib</AssemblyName>\r\n" +
        "        <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>\r\n" +
        "    </PropertyGroup>\r\n" +
        "    <ItemGroup>\r\n" +
        "        <Reference Include=\"System\" />\r\n" +
        "    </ItemGroup>\r\n" +
        "    <ItemGroup>\r\n" +
        "        <Compile Include=\"Properties\\AssemblyInfo.cs\" />\r\n" +
        "    </ItemGroup>\r\n" +
        "    <ItemGroup>\r\n" +
        "        <XamlAppDef Include=\"Activity1.xaml\">\r\n" +
        "            <SubType>Designer</SubType>\r\n" +
        "            <Generator>MSBuild:Compile</Generator>\r\n" +
        "        </XamlAppDef>\r\n" +
        "    </ItemGroup>\r\n" +
        "    <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />\r\n" +
        "</Project>\r\n";

    private const string SdkCsproj =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <TargetFramework>net48</TargetFramework>\r\n" +
        "  </PropertyGroup>\r\n" +
        "</Project>\r\n";

    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "vs_writer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Test]
    public void AddXamlItem_LegacyProject_AddsDesignerItem()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        bool changed = VsProjectWriter.AddXamlItem(csproj, @"Sub\Activity2.xaml");

        Assert.That(changed, Is.True);

        XDocument document = XDocument.Load(csproj);
        XElement item = document
            .Descendants()
            .Single(e => e.Name.LocalName == "XamlAppDef" && (string)e.Attribute("Include") == @"Sub\Activity2.xaml");

        Assert.Multiple(() =>
        {
            Assert.That((string)item.Elements().Single(e => e.Name.LocalName == "SubType"), Is.EqualTo("Designer"));
            Assert.That((string)item.Elements().Single(e => e.Name.LocalName == "Generator"), Is.EqualTo("MSBuild:Compile"));
            Assert.That(document.Descendants().Any(e => e.Name.LocalName == "ProjectGuid"), Is.True, "GUID проекта потерян");
            Assert.That(document.Descendants().Any(e => e.Name.LocalName == "Import"), Is.True, "импорт целей MSBuild потерян");
            Assert.That(document.Descendants().Count(e => e.Name.LocalName == "XamlAppDef"), Is.EqualTo(2));
        });
    }

    [Test]
    public void AddXamlItem_SecondCall_IsIdempotent()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);
        _ = VsProjectWriter.AddXamlItem(csproj, "Activity2.xaml");

        bool changedAgain = VsProjectWriter.AddXamlItem(csproj, "Activity2.xaml");

        XDocument document = XDocument.Load(csproj);
        Assert.Multiple(() =>
        {
            Assert.That(changedAgain, Is.False);
            Assert.That(document.Descendants().Count(e => e.Name.LocalName == "XamlAppDef"), Is.EqualTo(2));
        });
    }

    [Test]
    public void AddXamlItem_NormalizesForwardSlashes()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        _ = VsProjectWriter.AddXamlItem(csproj, "Sub/Activity2.xaml");

        XDocument document = XDocument.Load(csproj);
        Assert.That(document.Descendants().Any(e => e.Name.LocalName == "XamlAppDef" && (string)e.Attribute("Include") == @"Sub\Activity2.xaml"), Is.True);
    }

    [Test]
    public void AddXamlItem_SdkStyleProject_AddsItemGroup()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, SdkCsproj);

        bool changed = VsProjectWriter.AddXamlItem(csproj, "Activity2.xaml");

        XDocument document = XDocument.Load(csproj);
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(document.Descendants().Any(e => e.Name.LocalName == "XamlAppDef" && (string)e.Attribute("Include") == "Activity2.xaml"), Is.True);
            Assert.That(document.Descendants().Any(e => e.Name.LocalName == "TargetFramework"), Is.True, "свойства проекта потеряны");
        });
    }

    [Test]
    public void AddXamlItem_MissingProject_Throws()
    {
        string csproj = Path.Combine(_root, "Absent.csproj");

        Assert.That(() => VsProjectWriter.AddXamlItem(csproj, "Activity2.xaml"), Throws.InstanceOf<FileNotFoundException>());
    }

    [Test]
    public void AddXamlItem_KeepsFileFormatting()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        _ = VsProjectWriter.AddXamlItem(csproj, "Activity2.xaml");

        string text = File.ReadAllText(csproj);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("<XamlAppDef Include=\"Activity2.xaml\">\r\n"),
                "новый элемент должен быть отформатирован как соседние (дети с новой строки)");
            Assert.That(text, Does.Contain("            <SubType>Designer</SubType>"));
            Assert.That(text, Does.Contain("    <Import Project="), "импорт целей MSBuild остаётся на месте");
        });
    }

    [Test]
    public void RenameProjectFile_RenamesFileAndSolutionReference()
    {
        string projectFolder = Path.Combine(_root, "MyLib");
        Directory.CreateDirectory(projectFolder);
        string csproj = Path.Combine(projectFolder, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        string solution = Path.Combine(_root, "Sample.sln");
        File.WriteAllText(solution,
            "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"MyLib\", \"MyLib\\MyLib.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
            "EndProject\r\n");

        string newPath = VsProjectWriter.RenameProjectFile(csproj, "Renamed");

        string expectedPath = Path.Combine(projectFolder, "Renamed.csproj");
        Assert.Multiple(() =>
        {
            Assert.That(newPath, Is.EqualTo(expectedPath));
            Assert.That(File.Exists(expectedPath), Is.True);
            Assert.That(File.Exists(csproj), Is.False);
            Assert.That(File.ReadAllText(solution), Does.Contain(@"MyLib\Renamed.csproj"));
            Assert.That(File.ReadAllText(expectedPath), Does.Contain("<AssemblyName>MyLib</AssemblyName>"), "содержимое проекта не должно меняться");
        });
    }

    [Test]
    public void RenameProjectFile_KeepsCsprojExtensionOnce()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        string newPath = VsProjectWriter.RenameProjectFile(csproj, "Renamed.csproj");

        Assert.That(Path.GetFileName(newPath), Is.EqualTo("Renamed.csproj"));
    }

    [Test]
    public void RenameProjectFile_TargetExists_Throws()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);
        File.WriteAllText(Path.Combine(_root, "Renamed.csproj"), LegacyCsproj);

        Assert.That(() => VsProjectWriter.RenameProjectFile(csproj, "Renamed"), Throws.InstanceOf<IOException>());
    }

    [Test]
    public void RenameProjectFile_InvalidName_Throws()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        Assert.Multiple(() =>
        {
            Assert.That(() => VsProjectWriter.RenameProjectFile(csproj, "   "), Throws.ArgumentException);
            Assert.That(() => VsProjectWriter.RenameProjectFile(csproj, "bad|name"), Throws.ArgumentException);
            Assert.That(() => VsProjectWriter.RenameProjectFile(csproj, @"..\escape"), Throws.ArgumentException);
        });
    }

    [Test]
    public void RenameProjectFile_SameName_ReturnsSamePathWithoutTouchingFile()
    {
        string csproj = Path.Combine(_root, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);

        string newPath = VsProjectWriter.RenameProjectFile(csproj, "MyLib");

        Assert.Multiple(() =>
        {
            Assert.That(newPath, Is.EqualTo(csproj));
            Assert.That(File.Exists(csproj), Is.True);
        });
    }

    [Test]
    public void GetRelativeIncludePath_UsesBackslashes()
    {
        string projectFolder = Path.Combine(_root, "MyLib");
        Directory.CreateDirectory(Path.Combine(projectFolder, "Sub"));

        string relative = VsProjectWriter.GetRelativeIncludePath(projectFolder, Path.Combine(projectFolder, "Sub", "Activity2.xaml"));

        Assert.That(relative, Is.EqualTo(@"Sub\Activity2.xaml"));
    }
}
