using System;
using System.Activities.Statements;
using System.IO;
using NUnit.Framework;
using RPA.Core.Generation;
using RPA.Core.Services;
using RPA.NuGet.Services;
using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Оркестратор <c>StudioCacheReset</c> (кнопка «Сбросить кэши оптимизаций»): удаляет дисковые
/// кэши палитры и NuGet-индекса и очищает in-memory кэш дескрипторов.
/// </summary>
[TestFixture]
public class StudioCacheResetTests
{
    [Test]
    public void ResetAll_RemovesPaletteAndNuGetCacheFiles()
    {
        string paletteKey = "reset-all-palette-" + Guid.NewGuid().ToString("N");
        PaletteDiskCache.Save(paletteKey, new[] { BuildSequenceDescriptor() });
        string palettePath = IOPath.Combine(PaletteDiskCache.RootDirectory, paletteKey + ".json");

        string feed = CreateTempFeed();
        string nugetPath = LocalPackageIndexCache.GetCachePath(feed);
        Directory.CreateDirectory(IOPath.GetDirectoryName(nugetPath));
        File.WriteAllText(nugetPath, "{}");

        try
        {
            Assert.That(File.Exists(palettePath), Is.True);
            Assert.That(File.Exists(nugetPath), Is.True);

            StudioCacheReset.ResetAll();

            Assert.That(File.Exists(palettePath), Is.False,
                "палитровый дисковый кэш должен быть удалён");
            Assert.That(File.Exists(nugetPath), Is.False,
                "индекс локального NuGet-источника должен быть удалён");
        }
        finally
        {
            TryDelete(palettePath);
            TryDelete(nugetPath);
            TryDeleteDirectory(feed);
        }
    }

    [Test]
    public void ResetAll_ClearsRegistryDescriptorCache()
    {
        var registry = new ActivityPaletteRegistry(new[]
        {
            new ActivityPaletteCategory("Flow", new[] { typeof(Sequence) })
        });

        _ = registry.Describe(typeof(Sequence), "Flow");
        Assert.That(ActivityPaletteRegistry.CachedDescriptorCount, Is.GreaterThan(0),
            "перед сбросом дескриптор должен быть закэширован");

        StudioCacheReset.ResetAll();

        Assert.That(ActivityPaletteRegistry.CachedDescriptorCount, Is.EqualTo(0),
            "сброс кэшей обязан очистить DescriptorCache");
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

        return new PaletteActivityDescriptor(
            "Sequence",
            "Sequence",
            "Flow",
            typeof(Sequence),
            "description",
            new[] { argument },
            new PaletteContainerDescriptor[0]);
    }

    private static string CreateTempFeed()
    {
        string directory = IOPath.Combine(IOPath.GetTempPath(), "cache-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void TryDelete(string path)
    {
        try
        {
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Занятый каталог не должен ронять тест.
        }
    }
}
