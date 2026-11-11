using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RPA.Core.Generation;
using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Дисковый кэш палитры (<c>PaletteDiskCache</c>, п. 3.1
/// <c>StartupPerformance_Fix_Plan.md</c>): ключ, roundtrip, инвалидация и холодный старт реестра.
/// </summary>
[TestFixture]
public class PaletteDiskCacheTests
{
    [Test]
    public void ComputeKey_IsDeterministic()
    {
        ActivityPaletteCategory[] categories =
        {
            new ActivityPaletteCategory("Flow", new[] { typeof(Sequence), typeof(If) })
        };

        Assert.That(
            PaletteDiskCache.ComputeKey(categories),
            Is.EqualTo(PaletteDiskCache.ComputeKey(categories)),
            "одинаковый состав типов обязан давать одинаковый ключ");
    }

    [Test]
    public void ComputeKey_ChangesWhenTypeSetChanges()
    {
        string single = PaletteDiskCache.ComputeKey(new[]
        {
            new ActivityPaletteCategory("Flow", new[] { typeof(Sequence) })
        });

        string extended = PaletteDiskCache.ComputeKey(new[]
        {
            new ActivityPaletteCategory("Flow", new[] { typeof(Sequence), typeof(If) })
        });

        Assert.That(extended, Is.Not.EqualTo(single),
            "добавление типа обязано менять ключ и инвалидировать старый файл");
    }

    [Test]
    public void SaveLoad_RoundtripPreservesDescriptor()
    {
        string key = "roundtrip-" + Guid.NewGuid().ToString("N");
        try
        {
            var argument = new PaletteArgumentDescriptor(
                "Text",
                typeof(string),
                PaletteArgumentDirection.Property,
                typeof(string),
                required: false,
                defaultValue: null,
                description: "The text");

            var container = new PaletteContainerDescriptor(
                "Activities",
                PaletteContainerKind.ActivityCollection,
                typeof(Activity),
                "children");

            var descriptor = new PaletteActivityDescriptor(
                "Sequence",
                "Sequence",
                "Flow",
                typeof(Sequence),
                "description",
                new[] { argument },
                new[] { container });

            PaletteDiskCache.Save(key, new[] { descriptor });

            Dictionary<Type, PaletteActivityDescriptor> loaded = PaletteDiskCache.Load(key);

            Assert.That(loaded, Is.Not.Null, "записанный кэш обязан читаться");
            Assert.That(loaded.Count, Is.EqualTo(1));

            PaletteActivityDescriptor actual = loaded[typeof(Sequence)];

            Assert.Multiple(() =>
            {
                Assert.That(actual.Key, Is.EqualTo("Sequence"));
                Assert.That(actual.DisplayName, Is.EqualTo("Sequence"));
                Assert.That(actual.Category, Is.EqualTo("Flow"));
                Assert.That(actual.Type, Is.EqualTo(typeof(Sequence)));
                Assert.That(actual.Description, Is.EqualTo("description"));
                Assert.That(actual.Arguments, Has.Count.EqualTo(1));
                Assert.That(actual.Arguments[0].Name, Is.EqualTo("Text"));
                Assert.That(actual.Arguments[0].Direction, Is.EqualTo(PaletteArgumentDirection.Property));
                Assert.That(actual.Arguments[0].ValueType, Is.EqualTo(typeof(string)));
                Assert.That(actual.Containers, Has.Count.EqualTo(1));
                Assert.That(actual.Containers[0].Name, Is.EqualTo("Activities"));
                Assert.That(actual.Containers[0].Kind, Is.EqualTo(PaletteContainerKind.ActivityCollection));
            });
        }
        finally
        {
            DeleteCacheFile(key);
        }
    }

    [Test]
    public void Load_MissingKey_ReturnsNull()
    {
        Assert.That(PaletteDiskCache.Load("missing-" + Guid.NewGuid().ToString("N")), Is.Null);
    }

    [Test]
    public void Load_CorruptedJson_ReturnsNull()
    {
        string key = "corrupted-" + Guid.NewGuid().ToString("N");
        string path = IOPath.Combine(PaletteDiskCache.RootDirectory, key + ".json");
        Directory.CreateDirectory(PaletteDiskCache.RootDirectory);
        File.WriteAllText(path, "{ это не json");

        try
        {
            Assert.That(PaletteDiskCache.Load(key), Is.Null,
                "битый JSON обязан давать null без исключения");
        }
        finally
        {
            DeleteCacheFile(key);
        }
    }

    [Test]
    public void Invalidate_RemovesCacheFiles()
    {
        string key = "invalidate-" + Guid.NewGuid().ToString("N");
        PaletteDiskCache.Save(key, new[] { BuildSequenceDescriptor() });

        string path = IOPath.Combine(PaletteDiskCache.RootDirectory, key + ".json");
        Assert.That(File.Exists(path), Is.True, "после Save файл кэша обязан существовать");

        PaletteDiskCache.Invalidate();

        Assert.That(File.Exists(path), Is.False, "Invalidate обязана удалять файлы кэша");
    }

    [Test]
    public void Registry_ColdStart_ReadsDiskCache()
    {
        ActivityPaletteCategory[] categories =
        {
            new ActivityPaletteCategory("ColdStart", new[] { typeof(CacheProbeActivity) })
        };

        string key = PaletteDiskCache.ComputeKey(categories);
        DeleteCacheFile(key);

        try
        {
            var warm = new ActivityPaletteRegistry(categories);
            warm.PersistDiskCache();

            Assert.That(
                File.Exists(IOPath.Combine(PaletteDiskCache.RootDirectory, key + ".json")),
                Is.True,
                "тёплый реестр обязан записать дисковый кэш");

            var cold = new ActivityPaletteRegistry(categories);

            Assert.Multiple(() =>
            {
                Assert.That(cold.DiskCacheLoaded, Is.True, "второй реестр обязан прочитать дисковый кэш");
                Assert.That(cold.DiskCacheMisses, Is.EqualTo(0),
                    "при полном попадании в кэш рефлексия не выполняется");
            });
        }
        finally
        {
            DeleteCacheFile(key);
        }
    }

    private static PaletteActivityDescriptor BuildSequenceDescriptor()
    {
        var argument = new PaletteArgumentDescriptor(
            "Text",
            typeof(string),
            PaletteArgumentDirection.Property,
            typeof(string),
            required: false,
            defaultValue: null,
            description: "The text");

        var container = new PaletteContainerDescriptor(
            "Activities",
            PaletteContainerKind.ActivityCollection,
            typeof(Activity),
            "children");

        return new PaletteActivityDescriptor(
            "Sequence",
            "Sequence",
            "Flow",
            typeof(Sequence),
            "description",
            new[] { argument },
            new[] { container });
    }

    private static void DeleteCacheFile(string key)
    {
        try
        {
            string path = IOPath.Combine(PaletteDiskCache.RootDirectory, key + ".json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Занятый файл кэша не должен ронять тест.
        }
    }

    /// <summary>Уникальный тип активности: MVID тестовой сборки делает ключ кэша непересекающимся со студией.</summary>
    public sealed class CacheProbeActivity : CodeActivity
    {
        [RequiredArgument]
        public InArgument<string> Text { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
        }
    }
}
