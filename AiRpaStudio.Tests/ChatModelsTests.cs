using System.Collections.Generic;
using RPA.Chat;

namespace AiRpaStudio.Tests;

public class ChatModelsTests
{
    [Test]
    public void TextPayload_Defaults()
    {
        var p = new TextPayload();
        Assert.That(p.Text, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TextPayload_Ctor_SetsText()
    {
        var p = new TextPayload("hi");
        Assert.That(p.Text, Is.EqualTo("hi"));
    }

    [Test]
    public void ToolCallPayload_Defaults()
    {
        var p = new ToolCallPayload();
        Assert.That(p.ToolName, Is.EqualTo(string.Empty));
        Assert.That(p.Result, Is.EqualTo(string.Empty));
        Assert.That(p.Blocked, Is.False);
    }

    [Test]
    public void QuestionPayload_Defaults_Unanswered()
    {
        var q = new QuestionPayload();
        Assert.That(q.IsAnswered, Is.False);
        Assert.That(q.IsSkipped, Is.False);
        Assert.That(q.Answer, Is.EqualTo(string.Empty));
    }

    [Test]
    public void QuestionPayload_Answer_RaisesPropertyChanged()
    {
        var q = new QuestionPayload();
        var events = new List<string>();
        q.PropertyChanged += (s, e) => events.Add(e.PropertyName);
        q.Answer = "Yes";
        Assert.That(events, Does.Contain("Answer"));
        Assert.That(events, Does.Contain("IsAnswered"));
        Assert.That(q.IsAnswered, Is.True);
    }

    [Test]
    public void ChatAttachment_ToImagePart_SetsFields()
    {
        var a = new ChatAttachment { FileName = "x.png", IsImage = true, MediaType = "image/png", DataBase64 = "AAA" };
        var part = a.ToImagePart();
        Assert.That(part.MediaType, Is.EqualTo("image/png"));
        Assert.That(part.DataBase64, Is.EqualTo("AAA"));
    }

    [Test]
    public void ChatAttachment_DisplayText()
    {
        Assert.That(new ChatAttachment { FileName = "x.png", IsImage = true }.DisplayText, Is.EqualTo("Image: x.png"));
        Assert.That(new ChatAttachment { FileName = "a.txt" }.DisplayText, Is.EqualTo("File: a.txt"));
    }

    [Test]
    public void ChatMessage_Text_SetsTextPayload()
    {
        var m = new ChatMessage();
        m.Text = "hello";
        Assert.That(m.Text, Is.EqualTo("hello"));
        Assert.That(m.Payload, Is.InstanceOf<TextPayload>());
    }

    [Test]
    public void ChatMessage_Text_RaisesPropertyChanged()
    {
        var m = new ChatMessage();
        var events = new List<string>();
        m.PropertyChanged += (s, e) => events.Add(e.PropertyName);
        m.Text = "hi";
        Assert.That(events, Does.Contain("Text"));
    }

    [Test]
    public void ChatMessage_Reasoning_Defaults()
    {
        var m = new ChatMessage();
        Assert.That(m.HasReasoning, Is.False);
        Assert.That(m.ShowReasoning, Is.False);
        Assert.That(m.IsThinking, Is.False);
    }
}
