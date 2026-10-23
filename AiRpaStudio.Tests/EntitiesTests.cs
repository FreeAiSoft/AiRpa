using System;
using System.IO;
using RPA.Entities;
using RPA.Enums;

namespace AiRpaStudio.Tests;

public class EntitiesTests
{
    [Test]
    public void FSNode_File_Ctor_SetsNameFromFile()
    {
        var node = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        Assert.That(node.Name, Is.EqualTo("file.xaml"));
        Assert.That(node.FSNodeType, Is.EqualTo(FSNodeType.File));
    }

    [Test]
    public void FSNode_Dir_Ctor_SetsNameFromFolder()
    {
        var node = new FSNode(@"C:\dir\sub", FSNodeType.Dir);
        Assert.That(node.Name, Is.EqualTo("sub"));
        Assert.That(node.FSNodeType, Is.EqualTo(FSNodeType.Dir));
    }

    [Test]
    public void FSNode_Project_Ctor_ReadsNameFromProjectJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fsnode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "project.json"), "{\"Name\":\"CustomName\",\"ProjectType\":0}");
            var node = new FSNode(dir, FSNodeType.Project);
            Assert.That(node.Name, Is.EqualTo("CustomName"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void FSNode_Project_Ctor_WithoutProjectJson_UsesFolderName()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fsnode_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var node = new FSNode(dir, FSNodeType.Project);
            Assert.That(node.Name, Is.EqualTo(Path.GetFileName(dir)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void FSNode_ToString_ReturnsTypeAndPath()
    {
        var node = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        Assert.That(node.ToString(), Is.EqualTo("File C:\\dir\\file.xaml"));
    }

    [Test]
    public void FSNode_Name_RaisesPropertyChanged()
    {
        var node = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        string changed = null;
        node.PropertyChanged += (s, e) => changed = e.PropertyName;
        node.Name = "new.xaml";
        Assert.That(changed, Is.EqualTo("Name"));
    }

    [Test]
    public void FSNode_IsEditing_StoresOriginalName()
    {
        var node = new FSNode(@"C:\dir\file.xaml", FSNodeType.File);
        Assert.That(node.IsEditing, Is.False);
        node.IsEditing = true;
        Assert.That(node.IsEditing, Is.True);
        Assert.That(node.OriginalName, Is.EqualTo("file.xaml"));
    }

    [Test]
    public void Project_Ctor_SetsProjectType()
    {
        var p = new Project(ProjectType.Library);
        Assert.That(p.ProjectType, Is.EqualTo(ProjectType.Library));
        Assert.That(p.Name, Is.Null);
        Assert.That(p.MainActivity, Is.Null);
        Assert.That(p.Description, Is.Null);
    }

    [Test]
    public void Project_SetProjectDir_SetsDirPath()
    {
        var p = new Project(ProjectType.Process);
        p.SetProjectDir(@"C:\some\dir");
        Assert.That(p.ProjectDirPath, Is.EqualTo(@"C:\some\dir"));
    }
}
