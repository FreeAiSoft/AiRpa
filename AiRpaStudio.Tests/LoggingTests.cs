using RPA.Logging;
using RpaCommon.Enums;

namespace AiRpaStudio.Tests;

public class LoggingTests
{
    [Test]
    public void LogItem_Default_InfoLevelAndNullMessage()
    {
        var item = new LogItem();
        Assert.That(item.LogLevel, Is.EqualTo(LogLevel.Info));
        Assert.That(item.Message, Is.Null);
    }

    [Test]
    public void LogItem_CtorMessage_SetsMessageAndKeepsDefaultLevel()
    {
        var item = new LogItem("hello");
        Assert.That(item.Message, Is.EqualTo("hello"));
        Assert.That(item.LogLevel, Is.EqualTo(LogLevel.Info));
    }

    [Test]
    public void LogItem_ToString_FormatsLevelAndMessage()
    {
        var item = new LogItem("hello") { LogLevel = LogLevel.Warn };
        Assert.That(item.ToString(), Is.EqualTo("Warn: hello"));
    }
}
