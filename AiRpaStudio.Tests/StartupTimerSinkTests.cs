using System.Collections.Generic;
using NUnit.Framework;
using RPA.Logging;

namespace AiRpaStudio.Tests;

/// <summary>
/// Буферизованный вывод этапов загрузки (<c>[Startup]</c>) в панель «Log»:
/// метки, пришедшие до подключения панели, сливаются в приёмник в порядке поступления.
/// </summary>
[TestFixture]
public class StartupTimerSinkTests
{
    [TearDown]
    public void TearDown()
    {
        StartupTimer.ResetForTests();
    }

    [Test]
    public void Mark_WithoutSink_Buffers()
    {
        StartupTimer.ResetForTests();
        var received = new List<string>();

        StartupTimer.Mark("first");
        StartupTimer.Mark("second");
        StartupTimer.Attach(received.Add);

        Assert.That(received, Has.Count.EqualTo(2),
            "буфер обязан слиться в приёмник при подключении");
    }

    [Test]
    public void Attach_FlushesBufferedMarksInOrder()
    {
        StartupTimer.ResetForTests();
        var received = new List<string>();

        StartupTimer.Mark("first");
        StartupTimer.Mark("second");
        StartupTimer.Attach(received.Add);

        Assert.That(received.Count, Is.EqualTo(2));
        Assert.That(received[0], Does.Contain("[Startup] first"));
        Assert.That(received[1], Does.Contain("[Startup] second"));
    }

    [Test]
    public void Mark_AfterAttach_ForwardsToSink()
    {
        StartupTimer.ResetForTests();
        var received = new List<string>();
        StartupTimer.Attach(received.Add);

        StartupTimer.Mark("after-attach");

        Assert.That(received, Has.Count.EqualTo(1));
        Assert.That(received[0], Does.Contain("[Startup] after-attach"));
    }

    [Test]
    public void Mark_MessageKeepsStartupPrefix()
    {
        StartupTimer.ResetForTests();
        var received = new List<string>();
        StartupTimer.Attach(received.Add);

        StartupTimer.Mark("step");

        Assert.That(received[0], Does.StartWith("[Startup]"));
        Assert.That(received[0], Does.Contain("step"));
    }
}
