using System;
using System.IO;
using System.Linq;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

public class VsProjectParserTests
{
    private const string LegacyCsproj =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <RootNamespace>MyLib</RootNamespace>\r\n" +
        "    <AssemblyName>MyLib</AssemblyName>\r\n" +
        "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>\r\n" +
        "    <ProjectGuid>{11111111-1111-1111-1111-111111111111}</ProjectGuid>\r\n" +
        "    <OutputType>Library</OutputType>\r\n" +
        "  </PropertyGroup>\r\n" +
        "  <ItemGroup>\r\n" +
        "    <Compile Include=\"Properties\\AssemblyInfo.cs\" />\r\n" +
        "    <XamlAppDef Include=\"Activity1.xaml\" />\r\n" +
        "    <Reference Include=\"System\" />\r\n" +
        "  </ItemGroup>\r\n" +
        "</Project>";

    private const string SdkCsproj =
        "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <TargetFramework>net48</TargetFramework>\r\n" +
        "    <RootNamespace>MySdkLib</RootNamespace>\r\n" +
        "  </PropertyGroup>\r\n" +
        "  <ItemGroup>\r\n" +
        "    <ProjectReference Include=\"..\\Other\\Other.csproj\" />\r\n" +
        "  </ItemGroup>\r\n" +
        "</Project>";

    [Test]
    public void Parse_LegacyProject_ReadsPropertiesAndItems()
    {
        string path = WriteTemp(LegacyCsproj, "MyLib.csproj");
        try
        {
            var info = VsProjectParser.Parse(path);

            Assert.That(info.IsSdkStyle, Is.False);
            Assert.That(info.Name, Is.EqualTo("MyLib"));
            Assert.That(info.RootNamespace, Is.EqualTo("MyLib"));
            Assert.That(info.AssemblyName, Is.EqualTo("MyLib"));
            Assert.That(info.TargetFrameworkVersion, Is.EqualTo("v4.8"));
            Assert.That(info.ProjectGuid, Is.EqualTo("{11111111-1111-1111-1111-111111111111}"));
            Assert.That(info.OutputType, Is.EqualTo("Library"));

            Assert.That(info.Items.Count, Is.EqualTo(3));
            Assert.That(info.Items.Any(i => i.ItemType == "Compile" && i.Include == "Properties\\AssemblyInfo.cs"), Is.True);
            Assert.That(info.Items.Any(i => i.ItemType == "XamlAppDef" && i.Include == "Activity1.xaml"), Is.True);
            Assert.That(info.Items.Any(i => i.ItemType == "Reference" && i.Include == "System"), Is.True);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    [Test]
    public void Parse_SdkStyleProject_ReadsTargetFramework()
    {
        string path = WriteTemp(SdkCsproj, "MySdkLib.csproj");
        try
        {
            var info = VsProjectParser.Parse(path);

            Assert.That(info.IsSdkStyle, Is.True);
            Assert.That(info.TargetFramework, Is.EqualTo("net48"));
            Assert.That(info.RootNamespace, Is.EqualTo("MySdkLib"));
            Assert.That(info.Items.Count, Is.EqualTo(1));
            Assert.That(info.Items[0].ItemType, Is.EqualTo("ProjectReference"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path), true);
        }
    }

    private static string WriteTemp(string content, string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "vs_proj_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }
}
