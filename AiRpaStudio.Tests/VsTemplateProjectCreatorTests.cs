using System;
using System.IO;
using RPA.Models;

namespace AiRpaStudio.Tests;

public class VsTemplateProjectCreatorTests
{
    [Test]
    public void CoerceProjectName_EmptyPath_ReturnsDefault()
    {
        Assert.That(VsTemplateProjectCreator.CoerceProjectName("ActivityLibrary", null), Is.EqualTo("ActivityLibrary"));
        Assert.That(VsTemplateProjectCreator.CoerceProjectName("ActivityLibrary", ""), Is.EqualTo("ActivityLibrary"));
    }

    [Test]
    public void CoerceProjectName_AddsSuffix_WhenFolderExists()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vs_creator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "ActivityLibrary"));
        Directory.CreateDirectory(Path.Combine(dir, "ActivityLibrary1"));
        try
        {
            Assert.That(VsTemplateProjectCreator.CoerceProjectName("ActivityLibrary", dir), Is.EqualTo("ActivityLibrary2"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void Validate_EmptyPath_ReturnsError()
    {
        var creator = new VsTemplateProjectCreator();
        creator.SelectedPath = null;
        creator.ProjectName = "MyProject";

        Assert.That(creator.Validate(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Validate_NonexistentPath_ReturnsError()
    {
        var creator = new VsTemplateProjectCreator();
        creator.SelectedPath = Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N"));
        creator.ProjectName = "MyProject";

        Assert.That(creator.Validate(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Validate_EmptyName_ReturnsError()
    {
        var creator = new VsTemplateProjectCreator();
        creator.SelectedPath = Path.GetTempPath();
        creator.ProjectName = "";

        Assert.That(creator.Validate(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Validate_Valid_ReturnsNull()
    {
        var creator = new VsTemplateProjectCreator();
        creator.SelectedPath = Path.GetTempPath();
        creator.ProjectName = "MyProject";

        Assert.That(creator.Validate(), Is.Null);
    }

    [Test]
    public void BuildRequest_SanitizesName_And_SetsFramework()
    {
        var creator = new VsTemplateProjectCreator();
        creator.ProjectName = "My<Project>:Test";
        creator.TargetFrameworkVersion = "4.8.1";

        var request = creator.BuildRequest(@"C:\out");

        Assert.That(request.SafeProjectName, Is.EqualTo("MyProjectTest"));
        Assert.That(request.RootNamespace, Is.EqualTo("MyProjectTest"));
        Assert.That(request.TargetFrameworkVersion, Is.EqualTo("4.8.1"));
        Assert.That(request.OutputDirectory, Is.EqualTo(@"C:\out"));
    }

    [Test]
    public void CreateProject_InvalidPath_DoesNotSetIsCreate()
    {
        var creator = new VsTemplateProjectCreator();
        creator.SelectedPath = Path.Combine(Path.GetTempPath(), "vspc_missing_" + Guid.NewGuid().ToString("N"));
        creator.ProjectName = "MyLibrary";

        creator.CreateProject();

        Assert.That(creator.IsCreate, Is.False);
    }
}
