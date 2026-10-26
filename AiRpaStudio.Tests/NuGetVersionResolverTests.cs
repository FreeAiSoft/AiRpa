using NUnit.Framework;
using RPA.NuGet.Services;

namespace AiRpaStudio.Tests;

public class NuGetVersionResolverTests
{
    [Test]
    public void IsValidVersion_AcceptsAndRejects()
    {
        Assert.That(NuGetVersionResolver.IsValidVersion("1.2.3"), Is.True);
        Assert.That(NuGetVersionResolver.IsValidVersion("1.2.3-beta.1"), Is.True);
        Assert.That(NuGetVersionResolver.IsValidVersion("not-a-version"), Is.False);
        Assert.That(NuGetVersionResolver.IsValidVersion(""), Is.False);
        Assert.That(NuGetVersionResolver.IsValidVersion(null), Is.False);
    }

    [Test]
    public void SelectBestVersion_PicksHighestStable()
    {
        string best = NuGetVersionResolver.SelectBestVersion(new[] { "1.0.0", "2.0.0", "1.5.0" });

        Assert.That(best, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void SelectBestVersion_ExcludesPrereleaseByDefault()
    {
        string best = NuGetVersionResolver.SelectBestVersion(new[] { "1.0.0", "2.0.0-beta.1", "1.9.0" });

        Assert.That(best, Is.EqualTo("1.9.0"));
    }

    [Test]
    public void SelectBestVersion_IncludesPrereleaseWhenAllowed()
    {
        string best = NuGetVersionResolver.SelectBestVersion(new[] { "1.0.0", "2.0.0-beta.1" }, null, includePrerelease: true);

        Assert.That(best, Is.EqualTo("2.0.0-beta.1"));
    }

    [Test]
    public void SelectBestVersion_AllPrereleaseWithoutFlag_ReturnsNull()
    {
        string best = NuGetVersionResolver.SelectBestVersion(new[] { "2.0.0-beta.1" });

        Assert.That(best, Is.Null);
    }

    [Test]
    public void SelectBestVersion_CurrentVersionReturnsNextHigher()
    {
        string best = NuGetVersionResolver.SelectBestVersion(new[] { "1.0.0", "1.5.0", "2.0.0" }, "1.0.0");

        Assert.That(best, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void SelectBestVersion_NoHigherThanCurrentReturnsCurrent()
    {
        string best = NuGetVersionResolver.SelectBestVersion(new[] { "1.0.0", "1.5.0" }, "2.0.0");

        Assert.That(best, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void SelectBestVersion_EmptyOrInvalid_ReturnsNull()
    {
        Assert.That(NuGetVersionResolver.SelectBestVersion(new string[0]), Is.Null);
        Assert.That(NuGetVersionResolver.SelectBestVersion(new[] { "abc", "def" }), Is.Null);
        Assert.That(NuGetVersionResolver.SelectBestVersion(null), Is.Null);
    }

    [Test]
    public void GetLatestStable_ReturnsHighestStable()
    {
        Assert.That(NuGetVersionResolver.GetLatestStable(new[] { "1.0.0", "1.1.0", "1.0.5" }), Is.EqualTo("1.1.0"));
    }

    [Test]
    public void IsUpdateAvailable_TrueWhenNewerExists()
    {
        Assert.That(NuGetVersionResolver.IsUpdateAvailable(new[] { "1.0.0", "2.0.0" }, "1.0.0"), Is.True);
    }

    [Test]
    public void IsUpdateAvailable_FalseWhenNoNewer()
    {
        Assert.That(NuGetVersionResolver.IsUpdateAvailable(new[] { "1.0.0" }, "1.0.0"), Is.False);
        Assert.That(NuGetVersionResolver.IsUpdateAvailable(new[] { "1.0.0" }, "2.0.0"), Is.False);
    }
}
