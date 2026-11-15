using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Чистая логика <see cref="ProjectSessionStore"/>: per-project персист JSON-сессий вкладок
/// без WPF и без <c>Properties.Settings</c>.
/// </summary>
public class ProjectSessionStoreTests
{
    private const string ProjectA = @"C:\Projects\A";
    private const string ProjectB = @"C:\Projects\B";

    [Test]
    public void Parse_NullEmptyBroken_ReturnsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProjectSessionStore.Parse(null), Is.Empty);
            Assert.That(ProjectSessionStore.Parse(string.Empty), Is.Empty);
            Assert.That(ProjectSessionStore.Parse("   "), Is.Empty);
            Assert.That(ProjectSessionStore.Parse("{not json"), Is.Empty);
        });
    }

    [Test]
    public void Set_TryGet_RoundTrip()
    {
        string json = ProjectSessionStore.Set(null, ProjectA, "{\"v\":1}");

        Assert.Multiple(() =>
        {
            Assert.That(ProjectSessionStore.TryGet(json, ProjectA, out string session), Is.True);
            Assert.That(session, Is.EqualTo("{\"v\":1}"));
        });
    }

    [Test]
    public void Set_KeyIsCaseInsensitive()
    {
        string json = ProjectSessionStore.Set(null, ProjectA, "a");

        Assert.That(ProjectSessionStore.TryGet(json, ProjectA.ToLowerInvariant(), out string session), Is.True,
            "путь проекта сравнивается без учёта регистра");
        Assert.That(session, Is.EqualTo("a"));
    }

    [Test]
    public void TryGet_Missing_ReturnsFalse()
    {
        string json = ProjectSessionStore.Set(null, ProjectA, "a");

        Assert.That(ProjectSessionStore.TryGet(json, ProjectB, out _), Is.False);
    }

    [Test]
    public void Set_EmptySession_RemovesEntry()
    {
        string json = ProjectSessionStore.Set(null, ProjectA, "a");
        json = ProjectSessionStore.Set(json, ProjectA, string.Empty);

        Assert.That(ProjectSessionStore.TryGet(json, ProjectA, out _), Is.False,
            "пустая сессия не хранится");
    }

    [Test]
    public void Remove_RemovesEntry()
    {
        string json = ProjectSessionStore.Set(null, ProjectA, "a");
        json = ProjectSessionStore.Remove(json, ProjectA);

        Assert.That(ProjectSessionStore.TryGet(json, ProjectA, out _), Is.False);
    }
}
