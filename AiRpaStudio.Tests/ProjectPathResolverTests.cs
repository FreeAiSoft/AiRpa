using System;
using System.IO;
using RPA.Entities;
using RPA.Enums;
using RPA.Helpers;

namespace AiRpaStudio.Tests;

[TestFixture]
public class ProjectPathResolverTests
{
    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "path_resolver_" + Guid.NewGuid().ToString("N"));
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
    public void GetProjectFolder_ForCsprojFile_ReturnsParentFolder()
    {
        string projectFile = Path.Combine(_root, "ActivityLibrary5.csproj");
        File.WriteAllText(projectFile, "<Project />");

        Assert.That(ProjectPathResolver.GetProjectFolder(projectFile), Is.EqualTo(_root));
    }

    [Test]
    public void GetProjectFolder_ForSolutionFile_ReturnsParentFolder()
    {
        string solutionFile = Path.Combine(_root, "Solution.sln");
        File.WriteAllText(solutionFile, "Microsoft Visual Studio Solution File");

        Assert.That(ProjectPathResolver.GetProjectFolder(solutionFile), Is.EqualTo(_root));
    }

    [Test]
    public void GetProjectFolder_ForFolderWithTrailingSeparator_ReturnsFolderWithoutSeparator()
    {
        string path = _root + Path.DirectorySeparatorChar;

        Assert.That(ProjectPathResolver.GetProjectFolder(path), Is.EqualTo(_root));
    }

    [Test]
    public void GetProjectFolder_WithQuotesAndSpaces_ReturnsFolder()
    {
        Assert.That(ProjectPathResolver.GetProjectFolder("  \"" + _root + "\"  "), Is.EqualTo(_root));
    }

    [Test]
    public void GetProjectFolder_EmptyOrWhiteSpace_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectPathResolver.GetProjectFolder(null), Is.Null);
            Assert.That(ProjectPathResolver.GetProjectFolder(string.Empty), Is.Null);
            Assert.That(ProjectPathResolver.GetProjectFolder("   "), Is.Null);
        });
    }

    [Test]
    public void GetProjectFolder_ForMissingCsprojPath_ReturnsFolder()
    {
        string missing = Path.Combine(_root, "NotCreated.csproj");

        Assert.That(ProjectPathResolver.GetProjectFolder(missing), Is.EqualTo(_root));
    }

    [Test]
    public void IsInsideProject_FilePathInsideProject_IsTrue()
    {
        string projectFile = Path.Combine(_root, "My.csproj");

        Assert.That(ProjectPathResolver.IsInsideProject(Path.Combine(_root, "Activity1.xaml"), projectFile), Is.True);
    }

    [Test]
    public void IsInsideProject_NeighbourFolderWithSamePrefix_IsFalse()
    {
        Directory.CreateDirectory(Path.Combine(_root, "proj"));
        Directory.CreateDirectory(Path.Combine(_root, "proj2"));

        string projectFolder = Path.Combine(_root, "proj");

        Assert.Multiple(() =>
        {
            Assert.That(ProjectPathResolver.IsInsideProject(Path.Combine(_root, "proj2", "Activity1.xaml"), projectFolder), Is.False);
            Assert.That(ProjectPathResolver.IsInsideProject(Path.Combine(projectFolder, "Activity1.xaml"), projectFolder), Is.True);
        });
    }

    [Test]
    public void IsInsideProject_PathOutsideProject_IsFalse()
    {
        string outside = Path.Combine(Path.GetTempPath(), "elsewhere", "Activity1.xaml");

        Assert.That(ProjectPathResolver.IsInsideProject(outside, Path.Combine(_root, "My.csproj")), Is.False);
    }

    [Test]
    public void IsInsideProject_EmptyPath_IsFalse()
    {
        Assert.That(ProjectPathResolver.IsInsideProject(null, Path.Combine(_root, "My.csproj")), Is.False);
    }

    [Test]
    public void IsInsideProject_UnknownProject_DoesNotRestrict()
    {
        Assert.That(ProjectPathResolver.IsInsideProject(Path.Combine(_root, "Activity1.xaml"), null), Is.True);
    }

    [Test]
    public void GetNodeFolder_ForContainerNodes_ReturnsTheirPath()
    {
        FSNode dir = new FSNode(_root, FSNodeType.Dir);
        FSNode project = new FSNode(_root, FSNodeType.Project);

        Assert.Multiple(() =>
        {
            Assert.That(ProjectPathResolver.GetNodeFolder(dir), Is.EqualTo(_root));
            Assert.That(ProjectPathResolver.GetNodeFolder(project), Is.EqualTo(_root));
        });
    }

    [Test]
    public void GetNodeFolder_ForProjectFileNodes_ReturnsParentFolder()
    {
        FSNode vsProject = new FSNode(Path.Combine(_root, "My.csproj"), FSNodeType.VsProject);
        FSNode solution = new FSNode(Path.Combine(_root, "My.sln"), FSNodeType.Solution);
        FSNode file = new FSNode(Path.Combine(_root, "Activity1.xaml"), FSNodeType.File);

        Assert.Multiple(() =>
        {
            Assert.That(ProjectPathResolver.GetNodeFolder(vsProject), Is.EqualTo(_root));
            Assert.That(ProjectPathResolver.GetNodeFolder(solution), Is.EqualTo(_root));
            Assert.That(ProjectPathResolver.GetNodeFolder(file), Is.EqualTo(_root));
        });
    }

    [Test]
    public void GetNodeFolder_NullNodeOrEmptyPath_ReturnsNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectPathResolver.GetNodeFolder(null), Is.Null);
            Assert.That(ProjectPathResolver.GetNodeFolder(new FSNode(null, FSNodeType.File)), Is.Null);
        });
    }

    [Test]
    public void IsContainerNode_OnlyFoldersAndProjectFiles()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectPathResolver.IsContainerNode(new FSNode(_root, FSNodeType.Dir)), Is.True);
            Assert.That(ProjectPathResolver.IsContainerNode(new FSNode(_root, FSNodeType.Project)), Is.True);
            Assert.That(ProjectPathResolver.IsContainerNode(new FSNode(_root + ".csproj", FSNodeType.VsProject)), Is.True);
            Assert.That(ProjectPathResolver.IsContainerNode(new FSNode(_root, FSNodeType.File)), Is.False);
            Assert.That(ProjectPathResolver.IsContainerNode(null), Is.False);
        });
    }

    [Test]
    public void AreSamePath_IsCaseInsensitive()
    {
        string path = Path.Combine(_root, "Activity1.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(ProjectPathResolver.AreSamePath(path, path.ToUpperInvariant()), Is.True);
            Assert.That(ProjectPathResolver.AreSamePath(path, path + ".bak"), Is.False);
            Assert.That(ProjectPathResolver.AreSamePath(null, path), Is.False);
        });
    }
}
