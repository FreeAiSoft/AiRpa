using System.Text.RegularExpressions;
using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Персист выбранного источника NuGet на воркспейс: строковый JSON-словарь
/// <c>workspaceFolder → sourceUrl</c>, которым управляет <see cref="NuGetSourceSelectionStore"/>.
/// </summary>
public class NuGetSourceSelectionStoreTests
{
    [Test]
    public void Write_ThenRead_ReturnsSourceUrl()
    {
        string json = NuGetSourceSelectionStore.Write(null, @"C:\Workspace\Proj", "https://corp.example.org/v3/index.json");

        Assert.That(NuGetSourceSelectionStore.Read(json, @"C:\Workspace\Proj"),
            Is.EqualTo("https://corp.example.org/v3/index.json"));
    }

    [Test]
    public void Read_MatchesWorkspaceKeyCaseInsensitively()
    {
        string json = NuGetSourceSelectionStore.Write(null, @"C:\Workspace\Proj", "https://corp.example.org/v3/index.json");

        Assert.That(NuGetSourceSelectionStore.Read(json, @"c:\workspace\proj"),
            Is.EqualTo("https://corp.example.org/v3/index.json"));
    }

    [Test]
    public void Read_BrokenJson_ReturnsNull()
    {
        Assert.That(NuGetSourceSelectionStore.Read("{not json", @"C:\Workspace\Proj"), Is.Null);
    }

    [Test]
    public void Write_OverwritesSameWorkspaceWithoutDuplicates()
    {
        string json = NuGetSourceSelectionStore.Write(null, @"C:\Workspace\Proj", "first");
        json = NuGetSourceSelectionStore.Write(json, @"C:\Workspace\Proj", "second");

        Assert.Multiple(() =>
        {
            Assert.That(NuGetSourceSelectionStore.Read(json, @"C:\Workspace\Proj"), Is.EqualTo("second"));
            Assert.That(Regex.Matches(json, "second").Count, Is.EqualTo(1),
                "повторная запись того же воркспейса не должна плодить дубли ключей");
        });
    }

    [Test]
    public void Write_EmptySourceUrl_RemovesWorkspaceEntry()
    {
        string json = NuGetSourceSelectionStore.Write(null, @"C:\Workspace\Proj", "first");
        json = NuGetSourceSelectionStore.Write(json, @"C:\Workspace\Proj", null);

        Assert.That(NuGetSourceSelectionStore.Read(json, @"C:\Workspace\Proj"), Is.Null);
    }
}
