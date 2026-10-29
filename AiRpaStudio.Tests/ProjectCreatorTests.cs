using System;
using System.IO;
using RPA.Enums;
using RPA.Models;

namespace AiRpaStudio.Tests;

[NonParallelizable]
public class ProjectCreatorTests
{
    [Test]
    public void CreateProject_CreatesFiles_And_SetsIsCreate()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "pc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        // The creator copies Consts.defaultMainActivityFileName ("Main.xaml") relative to the current directory.
        File.WriteAllText(Path.Combine(baseDir, "Main.xaml"), "<Activity />");

        string previousCwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(baseDir);

            var creator = new ProjectCreator(ProjectType.Process);
            creator.SelectedPath = baseDir;
            creator.Project.Name = "MyProcess";

            creator.CreateProject();

            Assert.That(creator.IsCreate, Is.True);
            string root = Path.Combine(baseDir, "MyProcess");
            Assert.That(Directory.Exists(root), Is.True);
            Assert.That(File.Exists(Path.Combine(root, "project.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(root, "Main.xaml")), Is.True);
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCwd);
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, true);
            }
        }
    }

    [Test]
    public void CreateProject_MissingFolder_DoesNotSetIsCreate()
    {
        string missing = Path.Combine(Path.GetTempPath(), "pc_missing_" + Guid.NewGuid().ToString("N"));

        var creator = new ProjectCreator(ProjectType.Process);
        creator.SelectedPath = missing;
        creator.Project.Name = "MyProcess";

        creator.CreateProject();

        Assert.That(creator.IsCreate, Is.False);
    }

    [Test]
    public void CreateProject_BlankName_DoesNotSetIsCreate()
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "pc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        try
        {
            var creator = new ProjectCreator(ProjectType.Process);
            creator.SelectedPath = baseDir;
            creator.Project.Name = "   ";

            creator.CreateProject();

            Assert.That(creator.IsCreate, Is.False);
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, true);
            }
        }
    }
}
