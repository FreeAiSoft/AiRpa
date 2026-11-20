using System.Collections.ObjectModel;
using Agent.Core.Models;
using Agent.Core.Services;
using NUnit.Framework;
using RPA.Chat.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Регрессия "reasoning добавляется только в первый блок": маппинг сегментов ассемблера
/// в отдельные ChatMessage. Покрывает ChatReasoningSegmentBinder, который использует
/// StudioChatHost.SendChatAsync для стрима reasoning.
/// </summary>
public class ChatReasoningSegmentBinderTests
{
    private ObservableCollection<ChatMessage> _messages;
    private ChatReasoningSegmentBinder _binder;
    private ChatStreamAssembler _assembler;

    [SetUp]
    public void SetUp()
    {
        _messages = new ObservableCollection<ChatMessage>();
        _binder = new ChatReasoningSegmentBinder(
            append: msg => _messages.Add(msg),
            remove: msg => _messages.Remove(msg),
            mode: "Plan");
        _assembler = new ChatStreamAssembler();
        _assembler.SegmentChanged += (s, e) => _binder.Apply(e);
    }

    private void Feed(params ChatStreamDelta[] deltas)
    {
        foreach (var delta in deltas)
            _assembler.OnDelta(delta);
    }

    [Test]
    public void SingleReasoningSegment_CreatesOneMessageWithReasoning()
    {
        Feed(
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "r1" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "think " },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "more" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningEnd, Text = "think more" });

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Has.Count.EqualTo(1));
            Assert.That(_messages[0].Kind, Is.EqualTo(ChatMessageKind.AgentText));
            Assert.That(_messages[0].Reasoning, Is.EqualTo("think more"));
            Assert.That(_messages[0].IsThinking, Is.False);
            Assert.That(_messages[0].IsPartial, Is.False);
            Assert.That(_messages[0].Text, Is.Empty, "reasoning хранится в Reasoning, а не в Text");
        });
    }

    [Test]
    public void TwoReasoningSegments_CreateTwoDistinctMessages()
    {
        Feed(
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "r1" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "a" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningEnd, Text = "a" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "r2" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "b" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningEnd, Text = "b" });

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Has.Count.EqualTo(2));
            Assert.That(_messages[0].Reasoning, Is.EqualTo("a"));
            Assert.That(_messages[1].Reasoning, Is.EqualTo("b"));
            Assert.That(_messages[0], Is.Not.SameAs(_messages[1]),
                "второй reasoning не должен дописываться в первый блок");
        });
    }

    [Test]
    public void ContentBetweenReasoning_CreatesTwoReasoningBlocksAndIgnoresContent()
    {
        Feed(
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "r1" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "before" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ContentBegin },
            new ChatStreamDelta { Phase = ChatStreamPhase.ContentDelta, Text = "answer" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ContentEnd, Text = "answer" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "r2" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "after" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningEnd, Text = "after" });

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Has.Count.EqualTo(2), "content не создаёт reasoning-сообщения");
            Assert.That(_messages[0].Reasoning, Is.EqualTo("before"));
            Assert.That(_messages[1].Reasoning, Is.EqualTo("after"));
        });
    }

    [Test]
    public void TurnEnd_ClosesDanglingReasoningSegment()
    {
        Feed(
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "r1" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "think" },
            new ChatStreamDelta { Phase = ChatStreamPhase.TurnEnd });

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Has.Count.EqualTo(1));
            Assert.That(_messages[0].Reasoning, Is.EqualTo("think"));
            Assert.That(_messages[0].IsThinking, Is.False);
            Assert.That(_messages[0].IsPartial, Is.False);
        });
    }

    [Test]
    public void EmptyReasoningSegment_IsRemovedFromMessages()
    {
        Feed(
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "empty" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningEnd, Text = "" });

        Assert.That(_messages, Is.Empty,
            "пустой reasoning-блок не должен оставаться в чате");
    }

    [Test]
    public void ContentOnly_DoesNotCreateReasoningMessage()
    {
        Feed(
            new ChatStreamDelta { Phase = ChatStreamPhase.ContentBegin, SegmentId = "c1" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ContentDelta, Text = "answer" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ContentEnd, Text = "answer" });

        Assert.That(_messages, Is.Empty);
    }

    [Test]
    public void StopAll_MarksActiveSegmentsAsNotStreaming()
    {
        Feed(
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningBegin, SegmentId = "r1" },
            new ChatStreamDelta { Phase = ChatStreamPhase.ReasoningDelta, Text = "think" });

        _binder.StopAll();

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Has.Count.EqualTo(1));
            Assert.That(_messages[0].IsThinking, Is.False);
            Assert.That(_messages[0].IsPartial, Is.False);
        });
    }

    [Test]
    public void LegacyDeltas_ProduceSingleReasoningBlock()
    {
        Feed(
            new ChatStreamDelta { ReasoningDelta = "legacy think" },
            new ChatStreamDelta { ContentDelta = "answer" });

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Has.Count.EqualTo(1));
            Assert.That(_messages[0].Reasoning, Is.EqualTo("legacy think"));
        });
    }
}
