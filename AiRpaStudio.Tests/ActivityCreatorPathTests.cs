using System;
using System.IO;
using RPA.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Регресс по ошибке «Папка ...ActivityLibrary5.csproj не существует. Укажи существующую папку проекта»:
/// диалог создания активности получал файл проекта вместо папки.
/// </summary>
[TestFixture]
public class ActivityCreatorPathTests
{
    private string _root;
    private string _previousLastLoadedProjectPath;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "activity_creator_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _previousLastLoadedProjectPath = RPA.Properties.Settings.Default.LastLoadedProjectPath;
    }

    [TearDown]
    public void TearDown()
    {
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
    public void Constructor_WithCsprojFile_UsesProjectFolder()
    {
        string projectFile = Path.Combine(_root, "ActivityLibrary5.csproj");
        File.WriteAllText(projectFile, "<Project />");

        ActivityCreator creator = new ActivityCreator(projectFile);

        Assert.That(creator.SelectedPath, Is.EqualTo(_root));
    }

    [Test]
    public void Constructor_WithoutArgument_UsesFolderOfLastLoadedProjectFile()
    {
        string projectFile = Path.Combine(_root, "ActivityLibrary5.csproj");
        File.WriteAllText(projectFile, "<Project />");
        RPA.Properties.Settings.Default.LastLoadedProjectPath = projectFile;

        ActivityCreator creator = new ActivityCreator();

        Assert.That(creator.SelectedPath, Is.EqualTo(_root));
    }

    [Test]
    public void Constructor_WithoutArgumentAndWithoutProject_LeavesPathEmpty()
    {
        RPA.Properties.Settings.Default.LastLoadedProjectPath = string.Empty;

        ActivityCreator creator = new ActivityCreator();

        Assert.That(creator.SelectedPath, Is.Null.Or.Empty);
    }

    [Test]
    public void Constructor_WithFolder_KeepsFolder()
    {
        ActivityCreator creator = new ActivityCreator(_root);

        Assert.That(creator.SelectedPath, Is.EqualTo(_root));
    }

    [Test]
    public void Constructor_SuggestsActivityNameThatDoesNotExist()
    {
        File.WriteAllText(Path.Combine(_root, "Activity.xaml"), "<Activity />");
        RPA.Properties.Settings.Default.LastLoadedProjectPath = Path.Combine(_root, "ActivityLibrary5.csproj");
        File.WriteAllText(RPA.Properties.Settings.Default.LastLoadedProjectPath, "<Project />");

        ActivityCreator creator = new ActivityCreator();

        Assert.That(creator.ActivityName, Is.EqualTo("Activity1.xaml"));
    }
}
