using System;
using System.Activities;
using System.Activities.Statements;
using System.ComponentModel;
using System.Reflection;
using NUnit.Framework;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Кэши рефлексии <see cref="ActivityPaletteRegistry"/> (п. 3.5 плана покрытия): мемоизация
/// дескрипторов между реестрами и безопасное чтение атрибутов без резолва дизайнерских типов.
/// </summary>
[TestFixture]
public class ActivityPaletteRegistryCacheTests
{
    [Test]
    public void Describe_SameTypeAcrossRegistries_ReturnsSameDescriptor()
    {
        ActivityPaletteCategory[] categories =
        {
            new ActivityPaletteCategory("DescriptorCache", new[] { typeof(DescriptorCacheProbeActivity) })
        };

        string key = PaletteDiskCache.ComputeKey(categories);
        DeleteCacheFile(key);

        try
        {
            var first = new ActivityPaletteRegistry(categories);
            var second = new ActivityPaletteRegistry(categories);

            PaletteActivityDescriptor firstDescriptor =
                first.Describe(typeof(DescriptorCacheProbeActivity), "DescriptorCache");
            PaletteActivityDescriptor secondDescriptor =
                second.Describe(typeof(DescriptorCacheProbeActivity), "DescriptorCache");

            Assert.That(firstDescriptor, Is.Not.Null);
            Assert.That(secondDescriptor, Is.SameAs(firstDescriptor),
                "повторный Describe обязан брать дескриптор из DescriptorCache, а не рефлексировать заново");
        }
        finally
        {
            DeleteCacheFile(key);
        }
    }

    [Test]
    public void GetAttributeSafe_DoesNotThrowOnNonResolvableAttributes()
    {
        PropertyInfo text = typeof(AttributeProbeActivity).GetProperty("Text");

        Assert.Multiple(() =>
        {
            // WF-типы несут дизайнерские атрибуты из сборок, которых нет рядом; GetAttributeSafe
            // читает сырые CustomAttributeData и не резолвит их, поэтому не падает.
            Assert.DoesNotThrow(() => ActivityPaletteRegistry.GetAttributeSafe<System.ComponentModel.DescriptionAttribute>(typeof(Sequence), true));
            Assert.DoesNotThrow(() => ActivityPaletteRegistry.HasAttributeSafe(typeof(Sequence), typeof(RequiredArgumentAttribute), true));

            Assert.That(ActivityPaletteRegistry.GetAttributeSafe<ObsoleteAttribute>(typeof(Sequence), true), Is.Null,
                "отсутствующий атрибут возвращается как null");

            Assert.That(ActivityPaletteRegistry.GetAttributeSafe<RequiredArgumentAttribute>(text, true), Is.Not.Null,
                "обычный атрибут читается корректно");
            Assert.That(ActivityPaletteRegistry.GetAttributeSafe<System.ComponentModel.DescriptionAttribute>(typeof(AttributeProbeActivity), true)?.Description,
                Is.EqualTo("probe description"));
        });
    }

    private static void DeleteCacheFile(string key)
    {
        try
        {
            string path = System.IO.Path.Combine(PaletteDiskCache.RootDirectory, key + ".json");
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch
        {
            // Занятый файл кэша не должен ронять тест.
        }
    }

    /// <summary>Уникальный тип активности для проверки мемоизации между реестрами.</summary>
    public sealed class DescriptorCacheProbeActivity : CodeActivity
    {
        [RequiredArgument]
        public InArgument<string> Text { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
        }
    }

    [System.ComponentModel.Description("probe description")]
    public sealed class AttributeProbeActivity : CodeActivity
    {
        [RequiredArgument]
        public InArgument<string> Text { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
        }
    }
}
