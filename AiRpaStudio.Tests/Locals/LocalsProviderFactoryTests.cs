using System;
using System.Linq;
using ActivitiesTracker.Core;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Locals;

public class LocalsProviderFactoryTests
{
    [Test]
    public void Enum_HasThreeModes()
    {
        LocalsProviderMode[] values = Enum.GetValues(typeof(LocalsProviderMode))
            .Cast<LocalsProviderMode>()
            .ToArray();

        Assert.That(values, Is.EquivalentTo(new[]
        {
            LocalsProviderMode.Tracking,
            LocalsProviderMode.ActivityInstance,
            LocalsProviderMode.Mixed
        }));
    }

    [Test]
    public void Create_Tracking_ReturnsTrackingLocalsProvider()
    {
        Assert.That(
            LocalsProviderFactory.Create(LocalsProviderMode.Tracking),
            Is.InstanceOf<TrackingLocalsProvider>());
    }

    [Test]
    public void Create_ActivityInstance_ReturnsActivityInstanceLocalsProvider()
    {
        Assert.That(
            LocalsProviderFactory.Create(LocalsProviderMode.ActivityInstance),
            Is.InstanceOf<ActivityInstanceLocalsProvider>());
    }

    [Test]
    public void Create_Mixed_ReturnsMixedLocalsProvider()
    {
        Assert.That(
            LocalsProviderFactory.Create(LocalsProviderMode.Mixed),
            Is.InstanceOf<MixedLocalsProvider>());
    }

    [Test]
    public void Create_InvalidMode_FallsBackToTracking()
    {
        Assert.That(
            LocalsProviderFactory.Create((LocalsProviderMode)999),
            Is.InstanceOf<TrackingLocalsProvider>());
    }
}
