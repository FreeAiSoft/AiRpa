using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RPA.Core;

namespace AiRpaStudio.Tests;

/// <summary>
/// Чистая логика списка недавних проектов (<see cref="RecentProjectsStore"/>): парсинг,
/// дедупликация, лимит записей, имена проектов и фильтрация несуществующих путей.
/// </summary>
public class RecentProjectsStoreTests
{
    [Test]
    public void Parse_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RecentProjectsStore.Parse(null), Is.Empty);
            Assert.That(RecentProjectsStore.Parse(""), Is.Empty);
            Assert.That(RecentProjectsStore.Parse("   "), Is.Empty);
        });
    }

    [Test]
    public void Parse_BrokenJson_ReturnsEmpty()
    {
        Assert.That(RecentProjectsStore.Parse("{not json"), Is.Empty);
    }

    [Test]
    public void Parse_ValidJson_PreservesOrder()
    {
        string json = RecentProjectsStore.Serialize(new[]
        {
            new RecentProjectEntry { Name = "First", Path = @"C:\a" },
            new RecentProjectEntry { Name = "Second", Path = @"C:\b" }
        });

        var parsed = RecentProjectsStore.Parse(json).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Count, Is.EqualTo(2));
            Assert.That(parsed[0].Name, Is.EqualTo("First"));
            Assert.That(parsed[1].Path, Is.EqualTo(@"C:\b"));
        });
    }

    [Test]
    public void Add_Csproj_UsesFileNameWithoutExtension()
    {
        string file = CreateTempFile("MyProject.csproj");
        try
        {
            string json = RecentProjectsStore.Add(null, file);

            Assert.That(RecentProjectsStore.Parse(json).First().Name, Is.EqualTo("MyProject"));
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Test]
    public void Add_Sln_UsesFileNameWithoutExtension()
    {
        string file = CreateTempFile("Solution.sln");
        try
        {
            string json = RecentProjectsStore.Add(null, file);

            Assert.That(RecentProjectsStore.Parse(json).First().Name, Is.EqualTo("Solution"));
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Test]
    public void Add_Folder_UsesFolderName()
    {
        string dir = CreateTempDir("Project Folder");
        try
        {
            string json = RecentProjectsStore.Add(null, dir);

            Assert.That(RecentProjectsStore.Parse(json).First().Name, Is.EqualTo("Project Folder"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Test]
    public void Add_DuplicatePathCaseInsensitive_MovesToTopWithoutDuplicates()
    {
        string file = CreateTempFile("Dup.csproj");
        try
        {
            string upperDirPath = Path.Combine(
                Path.GetDirectoryName(file).ToUpperInvariant(),
                Path.GetFileName(file));

            string json = RecentProjectsStore.Add(null, file);
            json = RecentProjectsStore.Add(json, upperDirPath);

            var list = RecentProjectsStore.Parse(json).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(list.Count, Is.EqualTo(1), "повторный путь не должен плодить дубли");
                Assert.That(list[0].Name, Is.EqualTo("Dup"));
            });
        }
        finally
        {
            Cleanup(file);
        }
    }

    [Test]
    public void Add_ExceedingMaxEntries_DropsOldest()
    {
        string json = null;
        for (int i = 0; i < RecentProjectsStore.MaxEntries + 2; i++)
        {
            string file = CreateTempFile("P" + i + ".csproj");
            try
            {
                json = RecentProjectsStore.Add(json, file);
            }
            finally
            {
                Cleanup(file);
            }
        }

        var list = RecentProjectsStore.Parse(json).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(list.Count, Is.EqualTo(RecentProjectsStore.MaxEntries));
            Assert.That(list[0].Name, Is.EqualTo("P" + (RecentProjectsStore.MaxEntries + 1)), "самый новый проект обязан быть первым");
            Assert.That(list.Any(entry => entry.Name == "P0"), Is.False, "самый старый проект обязан выпасть из списка");
        });
    }

    [Test]
    public void RemoveMissing_KeepsOnlyExistingPaths()
    {
        string file = CreateTempFile("Keep.csproj");
        try
        {
            string json = RecentProjectsStore.Serialize(new[]
            {
                new RecentProjectEntry { Name = "Keep", Path = file },
                new RecentProjectEntry { Name = "Gone", Path = @"C:\definitely\missing\project.csproj" }
            });

            var list = RecentProjectsStore.Parse(RecentProjectsStore.RemoveMissing(json)).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(list.Count, Is.EqualTo(1));
                Assert.That(list[0].Name, Is.EqualTo("Keep"));
            });
        }
        finally
        {
            Cleanup(file);
        }
    }

    private static string CreateTempFile(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "RecentProjectsStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, name);
        File.WriteAllText(file, "<Project />");
        return file;
    }

    private static string CreateTempDir(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "RecentProjectsStoreTests", Guid.NewGuid().ToString("N"));
        string inner = Path.Combine(dir, name);
        Directory.CreateDirectory(inner);
        return inner;
    }

    private static void Cleanup(string path)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
