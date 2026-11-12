using RPA.ProjectModel;
using RPA.VsProject;

namespace AiRpaStudio.Tests;

public class VsProjectModelsTests
{
    [Test]
    public void VsSolutionProject_Defaults()
    {
        var p = new VsSolutionProject();
        Assert.That(p.TypeGuid, Is.Null);
        Assert.That(p.Name, Is.Null);
        Assert.That(p.RelativePath, Is.Null);
        Assert.That(p.ProjectGuid, Is.Null);
    }

    [Test]
    public void VsSolution_Defaults()
    {
        var s = new VsSolution();
        Assert.That(s.SolutionPath, Is.Null);
        Assert.That(s.Name, Is.Null);
        Assert.That(s.Projects, Is.Not.Null);
        Assert.That(s.Projects, Is.Empty);
    }

    [Test]
    public void VsProjectItem_Defaults()
    {
        var i = new VsProjectItem();
        Assert.That(i.ItemType, Is.Null);
        Assert.That(i.Include, Is.Null);
        Assert.That(i.HintPath, Is.Null);
    }

    [Test]
    public void VsProjectInfo_Defaults()
    {
        var p = new VsProjectInfo();
        Assert.That(p.ProjectPath, Is.Null);
        Assert.That(p.Name, Is.Null);
        Assert.That(p.RootNamespace, Is.Null);
        Assert.That(p.AssemblyName, Is.Null);
        Assert.That(p.TargetFrameworkVersion, Is.Null);
        Assert.That(p.TargetFramework, Is.Null);
        Assert.That(p.ProjectGuid, Is.Null);
        Assert.That(p.OutputType, Is.Null);
        Assert.That(p.IsSdkStyle, Is.False);
        Assert.That(p.Items, Is.Not.Null);
        Assert.That(p.Items, Is.Empty);
    }

    [Test]
    public void VsBuildResult_Defaults()
    {
        var r = new VsBuildResult();
        Assert.That(r.Success, Is.False);
        Assert.That(r.ExitCode, Is.EqualTo(0));
        Assert.That(r.ProjectPath, Is.Null);
        Assert.That(r.Output, Is.Null);
        Assert.That(r.Errors, Is.Not.Null);
        Assert.That(r.Errors, Is.Empty);
    }
}
