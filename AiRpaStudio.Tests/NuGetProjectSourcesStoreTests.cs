using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RPA.Core;
using RPA.NuGet.Models;

namespace AiRpaStudio.Tests;

/// <summary>
/// Персист per-project списка источников NuGet: строковый JSON-словарь
/// <c>workspaceFolder → [sources]</c>, которым управляет <see cref="NuGetProjectSourcesStore"/>.
/// </summary>
public class NuGetProjectSourcesStoreTests
{
    [Test]
    public void Write_ThenRead_ReturnsSources()
    {
        string json = NuGetProjectSourcesStore.Write(null, @"C:\Workspace\Proj", new[]
        {
            new NuGetSourceDefinition { Name = "nuget.org", Url = "https://api.nuget.org/v3/index.json", IsEnabled = true },
            new NuGetSourceDefinition { Name = "local", Url = @"C:\Feed", IsEnabled = false },
        });

        IReadOnlyList<NuGetSourceDefinition> sources = NuGetProjectSourcesStore.Read(json, @"C:\Workspace\Proj");

        Assert.Multiple(() =>
        {
            Assert.That(sources.Count, Is.EqualTo(2));
            Assert.That(sources[0].Name, Is.EqualTo("nuget.org"));
            Assert.That(sources[0].Url, Is.EqualTo("https://api.nuget.org/v3/index.json"));
            Assert.That(sources[0].IsEnabled, Is.True);
            Assert.That(sources[1].IsEnabled, Is.False);
        });
    }

    [Test]
    public void Read_MatchesWorkspaceKeyCaseInsensitively()
    {
        string json = NuGetProjectSourcesStore.Write(null, @"C:\Workspace\Proj",
            new[] { new NuGetSourceDefinition { Name = "corp", Url = "https://corp.example.org/v3/index.json" } });

        Assert.That(NuGetProjectSourcesStore.Read(json, @"c:\workspace\proj").Single().Url,
            Is.EqualTo("https://corp.example.org/v3/index.json"));
    }

    [Test]
    public void Read_BrokenJson_ReturnsEmpty()
    {
        Assert.That(NuGetProjectSourcesStore.Read("{not json", @"C:\Workspace\Proj"), Is.Empty);
    }

    [Test]
    public void Write_OverwritesSameWorkspaceWithoutDuplicates()
    {
        string json = NuGetProjectSourcesStore.Write(null, @"C:\Workspace\Proj",
            new[] { new NuGetSourceDefinition { Name = "first", Url = "https://first" } });
        json = NuGetProjectSourcesStore.Write(json, @"C:\Workspace\Proj",
            new[] { new NuGetSourceDefinition { Name = "second", Url = "https://second" } });

        Assert.Multiple(() =>
        {
            Assert.That(NuGetProjectSourcesStore.Read(json, @"C:\Workspace\Proj").Single().Name, Is.EqualTo("second"));
            Assert.That(json, Does.Not.Contain("first"),
                "повторная запись перезаписывает прежний список воркспейса, не оставляя дублей");
        });
    }

    [Test]
    public void Write_EmptySources_RemovesWorkspaceEntry()
    {
        string json = NuGetProjectSourcesStore.Write(null, @"C:\Workspace\Proj",
            new[] { new NuGetSourceDefinition { Name = "first", Url = "https://first" } });
        json = NuGetProjectSourcesStore.Write(json, @"C:\Workspace\Proj", Enumerable.Empty<NuGetSourceDefinition>());

        Assert.That(NuGetProjectSourcesStore.Read(json, @"C:\Workspace\Proj"), Is.Empty);
    }

    [Test]
    public void Read_OtherWorkspace_ReturnsEmpty()
    {
        string json = NuGetProjectSourcesStore.Write(null, @"C:\Workspace\A",
            new[] { new NuGetSourceDefinition { Name = "corp", Url = "https://corp" } });

        Assert.That(NuGetProjectSourcesStore.Read(json, @"C:\Workspace\B"), Is.Empty);
    }
}
