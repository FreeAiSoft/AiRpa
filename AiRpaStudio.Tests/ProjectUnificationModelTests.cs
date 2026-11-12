using Newtonsoft.Json;
using RPA.Entities;
using RPA.Enums;

namespace AiRpaStudio.Tests;

/// <summary>
/// Единый объект проекта: расширенный <see cref="Project"/> представляет и встроенный
/// (project.json), и VS (.csproj) формат, а старые project.json читаются без миграции.
/// </summary>
public class ProjectUnificationModelTests
{
    [Test]
    public void Project_NewInstance_DefaultsToNativeFormat()
    {
        var p = new Project(ProjectType.Process);

        Assert.Multiple(() =>
        {
            Assert.That(p.Format, Is.EqualTo(ProjectFormat.Native));
            Assert.That(p.ProjectFilePath, Is.Null);
            Assert.That(p.SolutionFilePath, Is.Null);
            Assert.That(p.Packages, Is.Not.Null);
            Assert.That(p.Packages, Is.Empty);
            Assert.That(p.Items, Is.Not.Null);
            Assert.That(p.Items, Is.Empty);
        });
    }

    [Test]
    public void Project_DeserializesLegacyJson_WithoutMigration()
    {
        const string json = "{\"Name\":\"CustomName\",\"ProjectType\":0}";

        var p = JsonConvert.DeserializeObject<Project>(json);

        Assert.Multiple(() =>
        {
            Assert.That(p.Name, Is.EqualTo("CustomName"));
            Assert.That(p.ProjectType, Is.EqualTo(ProjectType.Process));
            Assert.That(p.Format, Is.EqualTo(ProjectFormat.Native));
        });
    }

    [Test]
    public void Project_JsonRoundTrip_PreservesTargetFrameworkVersion()
    {
        var p = new Project(ProjectType.Library)
        {
            Name = "MyLib",
            MainActivity = "Main.xaml",
            TargetFrameworkVersion = "4.7.2"
        };

        string json = JsonConvert.SerializeObject(p);
        var round = JsonConvert.DeserializeObject<Project>(json);

        Assert.Multiple(() =>
        {
            Assert.That(round.Name, Is.EqualTo("MyLib"));
            Assert.That(round.MainActivity, Is.EqualTo("Main.xaml"));
            Assert.That(round.TargetFrameworkVersion, Is.EqualTo("4.7.2"));
        });
    }

    [Test]
    public void Project_JsonDoesNotSerializeTransientVsFields()
    {
        var p = new Project(ProjectType.Library)
        {
            Name = "MyLib",
            Format = ProjectFormat.VsProject,
            ProjectFilePath = @"C:\work\MyLib.csproj",
            SolutionFilePath = @"C:\work\MyLib.sln",
            AssemblyName = "MyLib",
            RootNamespace = "MyLib",
            ProjectGuid = "{11111111-1111-1111-1111-111111111111}",
            IsSdkStyle = true,
            IsWorkflowProject = true
        };

        string json = JsonConvert.SerializeObject(p);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("ProjectFilePath"));
            Assert.That(json, Does.Not.Contain("SolutionFilePath"));
            Assert.That(json, Does.Not.Contain("AssemblyName"));
            Assert.That(json, Does.Not.Contain("\"Format\""));
        });
    }

    [Test]
    public void ProjectFormat_Values()
    {
        Assert.That((int)ProjectFormat.Native, Is.EqualTo(0));
        Assert.That((int)ProjectFormat.VsProject, Is.EqualTo(1));
    }
}
