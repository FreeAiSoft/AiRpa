using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RPA.Entities;
using RPA.Enums;
using RPA.Models;
using RpaCommon;

namespace AiRpaStudio.Tests;

/// <summary>
/// Дерево проекта Visual Studio: добавление активности в дерево (.csproj-корень и .sln-корень),
/// регистрация файла в проекте и переименование файла проекта.
/// </summary>
[TestFixture]
public class VsProjectTreeFileTests
{
    private const string LegacyCsproj =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Project ToolsVersion=\"15.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n" +
        "  <PropertyGroup>\r\n" +
        "    <AssemblyName>MyLib</AssemblyName>\r\n" +
        "    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>\r\n" +
        "  </PropertyGroup>\r\n" +
        "  <ItemGroup>\r\n" +
        "    <XamlAppDef Include=\"Activity1.xaml\"><SubType>Designer</SubType></XamlAppDef>\r\n" +
        "  </ItemGroup>\r\n" +
        "</Project>\r\n";

    private ProjectManager _manager;
    private string _root;
    private ObservableCollection<FSNode> _previousNodes;
    private string _previousProjectFolder;
    private string _previousLastLoadedProjectPath;

    [SetUp]
    public void SetUp()
    {
        _manager = ProjectManager.GetInstance();
        _previousNodes = _manager.FSNodes;
        _previousProjectFolder = _manager.ProjectFolder;
        _previousLastLoadedProjectPath = RPA.Properties.Settings.Default.LastLoadedProjectPath;

        _root = Path.Combine(Path.GetTempPath(), "vs_tree_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        _manager.FSNodes = _previousNodes;
        _manager.ProjectFolder = _previousProjectFolder;
        RPA.Properties.Settings.Default.LastLoadedProjectPath = _previousLastLoadedProjectPath;
        RPA.Properties.Settings.Default.Save();

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
    public void AddFile_ToProjectRoot_AddsFileNode()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);

        _manager.AddFile(WriteActivity(_root, "Activity2.xaml"));

        FSNode root = _manager.FSNodes[0];
        Assert.Multiple(() =>
        {
            Assert.That(root.FSNodeType, Is.EqualTo(FSNodeType.VsProject));
            Assert.That(Path.GetFileName(root.RuntimeFullPath), Is.EqualTo("MyLib.csproj"));
            Assert.That(root.Childs.Any(c => c.FSNodeType == FSNodeType.File && c.Name == "Activity2.xaml"), Is.True,
                "новая активность не появилась в дереве проекта");
        });
    }

    [Test]
    public void AddFile_ToSubFolder_AddsNodeToThatFolder()
    {
        string csproj = CreateProjectFiles(_root);
        string subFolder = Path.Combine(_root, "Sub");
        Directory.CreateDirectory(subFolder);
        LoadProject(csproj);

        _manager.AddFile(WriteActivity(subFolder, "Activity3.xaml"));

        FSNode subNode = _manager.FSNodes[0].Childs.Single(c => c.FSNodeType == FSNodeType.Dir && c.Name == "Sub");
        Assert.That(subNode.Childs.Any(c => c.Name == "Activity3.xaml"), Is.True);
    }

    [Test]
    public void AddFile_ToFolderCreatedAfterLoad_BuildsFolderChain()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);

        string deepFolder = Path.Combine(_root, "New", "Nested");
        Directory.CreateDirectory(deepFolder);
        _manager.AddFile(WriteActivity(deepFolder, "Activity4.xaml"));

        FSNode newFolder = _manager.FSNodes[0].Childs.Single(c => c.Name == "New");
        FSNode nestedFolder = newFolder.Childs.Single(c => c.Name == "Nested");
        Assert.That(nestedFolder.Childs.Any(c => c.Name == "Activity4.xaml"), Is.True);
    }

    [Test]
    public void AddFile_ForSolutionTree_AddsNodeToOwningProject()
    {
        string projectFolder = Path.Combine(_root, "MyLib");
        Directory.CreateDirectory(projectFolder);
        _ = CreateProjectFiles(projectFolder);
        File.WriteAllText(Path.Combine(_root, "Sample.sln"),
            "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"MyLib\", \"MyLib\\MyLib.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\n" +
            "EndProject\r\n");
        LoadProject(Path.Combine(_root, "Sample.sln"));

        _manager.AddFile(WriteActivity(projectFolder, "Activity5.xaml"));

        FSNode solution = _manager.FSNodes[0];
        FSNode project = solution.Childs.Single(c => c.FSNodeType == FSNodeType.VsProject);
        Assert.Multiple(() =>
        {
            Assert.That(solution.FSNodeType, Is.EqualTo(FSNodeType.Solution));
            Assert.That(project.Childs.Any(c => c.Name == "Activity5.xaml"), Is.True);
        });
    }

    [Test]
    public void AddFile_OutsideProject_IsRejected()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        List<string> messages = new List<string>();
        _manager.SendMessage += (_, message) => messages.Add(message);

        string outsideFolder = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideFolder);
        try
        {
            _manager.AddFile(WriteActivity(outsideFolder, "Activity6.xaml"));

            Assert.Multiple(() =>
            {
                Assert.That(_manager.FSNodes[0].Childs.Any(c => c.Name == "Activity6.xaml"), Is.False);
                Assert.That(messages.Any(m => m.Contains("вне папки проекта")), Is.True);
            });
        }
        finally
        {
            Directory.Delete(outsideFolder, true);
        }
    }

    [Test]
    public void RegisterFileInVsProject_AddsXamlAppDefToCsproj()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        string activity = WriteActivity(_root, "Activity7.xaml");

        CallResult result = _manager.RegisterFileInVsProject(activity);

        XDocument document = XDocument.Load(csproj);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(document.Descendants().Any(e => e.Name.LocalName == "XamlAppDef" && (string)e.Attribute("Include") == "Activity7.xaml"), Is.True);
        });
    }

    [Test]
    public void RegisterFileInVsProject_ForStudioProject_IsNoOp()
    {
        File.WriteAllText(Path.Combine(_root, "project.json"), "{ \"Name\": \"Process\" }");
        CallResult initResult = _manager.InitFileTree(_root);
        Assert.That(initResult.Success, Is.True, initResult.Message);

        string activity = WriteActivity(_root, "Activity8.xaml");
        CallResult result = _manager.RegisterFileInVsProject(activity);

        Assert.That(result.Success, Is.True, result.Message);
    }

    [Test]
    public void RenameVsProject_RenamesProjectFileAndNode()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        FSNode projectNode = _manager.FSNodes[0];

        CallResult result = _manager.RenameVsProject(projectNode, "Renamed");

        string expectedPath = Path.Combine(_root, "Renamed.csproj");
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(File.Exists(expectedPath), Is.True, "файл проекта не переименован");
            Assert.That(File.Exists(csproj), Is.False);
            Assert.That(projectNode.RuntimeFullPath, Is.EqualTo(expectedPath));
            Assert.That(projectNode.Name, Is.EqualTo("Renamed"));
        });
    }

    [Test]
    public void RenameVsProject_UpdatesSavedProjectPath()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        RPA.Properties.Settings.Default.LastLoadedProjectPath = csproj;

        CallResult result = _manager.RenameVsProject(_manager.FSNodes[0], "Renamed");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(RPA.Properties.Settings.Default.LastLoadedProjectPath, Is.EqualTo(Path.Combine(_root, "Renamed.csproj")),
                "проект не откроется при следующем запуске студии");
        });
    }

    [Test]
    public void RenameVsProject_ForNonProjectNode_ReturnsError()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        FSNode folderNode = new FSNode(_root, FSNodeType.Dir);

        CallResult result = _manager.RenameVsProject(folderNode, "Renamed");

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Message, Does.Contain("не является проектом"));
        });
    }

    [Test]
    public void ImportFile_CopiesFileAddsNodeAndRegisters()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        string sourceDir = Path.Combine(Path.GetTempPath(), "import_src_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        string source = Path.Combine(sourceDir, "helper.cs");
        File.WriteAllText(source, "class Helper {}");
        try
        {
            CallResult<string> result = _manager.ImportFile(source, _root);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(File.Exists(result.Data), Is.True);
                Assert.That(_manager.FSNodes[0].Childs.Any(c => c.FSNodeType == FSNodeType.File && c.Name == "helper.cs"), Is.True);
                Assert.That(XDocument.Load(csproj).Descendants().Any(e => e.Name.LocalName == "Compile" && (string)e.Attribute("Include") == "helper.cs"), Is.True,
                    "произвольный .cs должен регистрироваться как Compile в .csproj");
            });
        }
        finally
        {
            Directory.Delete(sourceDir, true);
        }
    }

    [Test]
    public void ImportFile_NameConflict_GeneratesUniqueName()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "original");
        string sourceDir = Path.Combine(Path.GetTempPath(), "import_src_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        string source = Path.Combine(sourceDir, "notes.txt");
        File.WriteAllText(source, "copy");
        try
        {
            CallResult<string> result = _manager.ImportFile(source, _root);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Message);
                Assert.That(Path.GetFileName(result.Data), Is.EqualTo("notes1.txt"));
                Assert.That(File.ReadAllText(Path.Combine(_root, "notes.txt")), Is.EqualTo("original"), "исходный файл не должен перезаписываться");
            });
        }
        finally
        {
            Directory.Delete(sourceDir, true);
        }
    }

    [Test]
    public void ImportFile_OutsideProject_ReturnsError()
    {
        string csproj = CreateProjectFiles(_root);
        LoadProject(csproj);
        string outside = Path.Combine(Path.GetTempPath(), "import_out_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        string sourceDir = Path.Combine(Path.GetTempPath(), "import_src_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sourceDir);
        string source = Path.Combine(sourceDir, "readme.md");
        File.WriteAllText(source, "x");
        try
        {
            CallResult<string> result = _manager.ImportFile(source, outside);

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error.Message, Does.Contain("внутри папки проекта"));
            });
        }
        finally
        {
            Directory.Delete(outside, true);
            Directory.Delete(sourceDir, true);
        }
    }



    private string CreateProjectFiles(string folder)
    {
        string csproj = Path.Combine(folder, "MyLib.csproj");
        File.WriteAllText(csproj, LegacyCsproj);
        File.WriteAllText(Path.Combine(folder, "Activity1.xaml"), "<Activity />");
        return csproj;
    }

    private void LoadProject(string projectPath)
    {
        CallResult result = _manager.InitVsProjectTree(projectPath);
        Assert.That(result.Success, Is.True, result.Message);
    }

    private static string WriteActivity(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        File.WriteAllText(path, "<Activity />");
        return path;
    }
}
