using System;
using System.Collections.Generic;
using Agent.Core.Models;
using Agent.Core.Services;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Maps <see cref="ChatStreamAssembler"/> reasoning segments to separate
    /// <see cref="ChatMessage"/> entries. Each <c>ReasoningBegin</c> opens a new message, so
    /// consecutive reasoning segments of one agent turn are not merged into the first block
    /// (the Cline "streaming reasoning" behaviour). Content segments are ignored here: the
    /// final answer is appended by the host as a single result block.
    /// <para>
    /// UI-free by contract: message insertion/removal is injected through delegates, so the
    /// binder can be exercised headlessly in tests and reused by <see cref="StudioChatHost"/>.
    /// </para>
    /// </summary>
    public sealed class ChatReasoningSegmentBinder
    {
        private readonly Action<ChatMessage> _append;
        private readonly Action<ChatMessage> _remove;
        private readonly string _mode;
        private readonly Dictionary<string, ChatMessage> _segments = new Dictionary<string, ChatMessage>();

        public ChatReasoningSegmentBinder(Action<ChatMessage> append, Action<ChatMessage> remove = null, string mode = "")
        {
            _append = append ?? throw new ArgumentNullException(nameof(append));
            _remove = remove;
            _mode = mode ?? "";
        }

        /// <summary>Reasoning segments that are currently streaming (opened but not yet closed).</summary>
        public IReadOnlyCollection<ChatMessage> ActiveSegments => _segments.Values;

        /// <summary>Applies one assembler event. Non-reasoning events are ignored.</summary>
        public void Apply(ChatStreamSegmentEventArgs e)
        {
            if (e == null || e.Kind != ChatStreamSegmentKind.Reasoning) return;

            switch (e.Event)
            {
                case ChatStreamSegmentEventKind.Opened:
                    OpenSegment(e.SegmentId);
                    break;

                case ChatStreamSegmentEventKind.Text:
                    if (_segments.TryGetValue(e.SegmentId, out var textMsg))
                        textMsg.Reasoning = e.Text;
                    break;

                case ChatStreamSegmentEventKind.Closed:
                    CloseSegment(e.SegmentId, e.Text);
                    break;
            }
        }

        /// <summary>Marks all open segments as no longer streaming (cancel/error path).</summary>
        public void StopAll()
        {
            foreach (var msg in _segments.Values)
            {
                msg.IsThinking = false;
                msg.IsPartial = false;
            }
        }

        private void OpenSegment(string segmentId)
        {
            var msg = new ChatMessage
            {
                Kind = ChatMessageKind.AgentText,
                Mode = _mode,
                IsThinking = true,
                IsPartial = true
            };
            _segments[segmentId] = msg;
            _append(msg);
        }

        private void CloseSegment(string segmentId, string text)
        {
            if (!_segments.TryGetValue(segmentId, out var msg)) return;

            msg.Reasoning = text ?? msg.Reasoning;
            msg.IsThinking = false;
            msg.IsPartial = false;

            if (msg.HasReasoning)
                msg.Text = string.Empty;
            else if (_remove != null)
                _remove(msg);

            _segments.Remove(segmentId);
        }
    }
}
