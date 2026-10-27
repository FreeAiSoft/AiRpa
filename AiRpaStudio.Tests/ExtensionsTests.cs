using System;
using System.Activities.Debugger;
using ActivitiesTracker.Entities;
using ActivitiesTracker.Enums;
using RPA.Extensions;
using RpaCommon.Enums;

namespace AiRpaStudio.Tests;

public class ExtensionsTests
{
    [Test]
    public void ToActityState_Executing_ReturnsExecuting()
    {
        Assert.That("Executing".ToActityState(), Is.EqualTo(ActivityState.Executing));
    }

    [Test]
    public void ToActityState_Closed_ReturnsClosed()
    {
        Assert.That("Closed".ToActityState(), Is.EqualTo(ActivityState.Closed));
    }

    [Test]
    public void ToActityState_Canceled_ReturnsCanceled()
    {
        Assert.That("Canceled".ToActityState(), Is.EqualTo(ActivityState.Canceled));
    }

    [Test]
    public void ToActityState_Faulted_ReturnsFaulted()
    {
        Assert.That("Faulted".ToActityState(), Is.EqualTo(ActivityState.Faulted));
    }

    [Test]
    public void ToActityState_Unknown_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => "Prestart".ToActityState());
        Assert.That(ex.Message, Does.Contain("Unknow state Prestart"));
    }

    [Test]
    public void ToLogLevel_Trace_ReturnsTrace()
    {
        Assert.That("trace".ToLogLevel(), Is.EqualTo(LogLevel.Trace));
    }

    [Test]
    public void ToLogLevel_Info_CaseInsensitive_ReturnsInfo()
    {
        Assert.That("INFO".ToLogLevel(), Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void ToLogLevel_Warn_ReturnsWarn()
    {
        Assert.That("warn".ToLogLevel(), Is.EqualTo(LogLevel.Warn));
    }

    [Test]
    public void ToLogLevel_Unknown_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => "verbose".ToLogLevel());
    }

    [Test]
    public void SourceLocation_GetLogString_FormatsFields()
    {
        var sl = new SourceLocation("Main.xaml", 10, 20, 30, 40);
        Assert.That(sl.GetLogString(), Is.EqualTo(" LT: 10 20; RB: 30 40; file: MAIN.XAML"));
    }

    [Test]
    public void TrackingEventArgs_GetLogItem_MapsLevelAndMessage()
    {
        var args = new TrackingEventArgs("hello") { Level = "info" };
        var item = args.GetLogItem();
        Assert.That(item.Message, Is.EqualTo("hello"));
        Assert.That(item.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void TrackingEventArgs_GetLogItem_NullLevel_DefaultsToTrace()
    {
        var args = new TrackingEventArgs("hello") { Level = null };
        var item = args.GetLogItem();
        Assert.That(item.LogLevel, Is.EqualTo(LogLevel.Trace));
    }

    [Test]
    public void ToLogLevel_Debug_ReturnsDebug()
    {
        Assert.That("debug".ToLogLevel(), Is.EqualTo(LogLevel.Debug));
    }

    [Test]
    public void ToLogLevel_Error_ReturnsError()
    {
        Assert.That("error".ToLogLevel(), Is.EqualTo(LogLevel.Error));
    }

    [Test]
    public void ToLogLevel_Fatal_ReturnsFatal()
    {
        Assert.That("fatal".ToLogLevel(), Is.EqualTo(LogLevel.Fatal));
    }
}
