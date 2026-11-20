using System.Linq;
using NUnit.Framework;
using RPA.Chat.Services;
using Agent.Core.Models;
using RPA.Shared.Models;

namespace RPA.Chat.Tests
{
    [TestFixture]
    public class ChatHistoryStoreTests
    {
        private ChatHistoryStore _store;
        [SetUp]
        public void SetUp()
        {
            _store = new ChatHistoryStore();
            _store.DeleteAllConversations();
        }

        [TearDown]
        public void TearDown()
        {
            _store.DeleteAllConversations();
        }

        [Test]
        public void SaveAndLoad_RoundTripsConversation()
        {
            var conversation = new ChatConversation
            {
                Id = "conv-1",
                Title = "Test chat",
                CreatedAtUtc = System.DateTime.UtcNow,
                UpdatedAtUtc = System.DateTime.UtcNow
            };
            conversation.Messages.Add(new ChatHistoryMessage { Kind = ChatMessageKind.User, Text = "hello" });
            conversation.Messages.Add(new ChatHistoryMessage { Kind = ChatMessageKind.AgentText, Text = "hi there" });

            _store.SaveConversation(conversation);

            var loaded = _store.LoadConversation("conv-1");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Id, Is.EqualTo("conv-1"));
            Assert.That(loaded.Title, Is.EqualTo("Test chat"));
            Assert.That(loaded.Messages.Count, Is.EqualTo(2));
            Assert.That(loaded.Messages[0].Text, Is.EqualTo("hello"));
            Assert.That(loaded.Messages[1].Kind, Is.EqualTo(ChatMessageKind.AgentText));
        }

        [Test]
        public void SaveAndLoad_ToolCall_RoundTripsResult()
        {
            var conversation = new ChatConversation { Id = "conv-tool" };
            conversation.Messages.Add(new ChatHistoryMessage
            {
                Kind = ChatMessageKind.ToolCall,
                Text = "short result",
                ToolName = "activity_search",
            });

            _store.SaveConversation(conversation);

            var loaded = _store.LoadConversation("conv-tool");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Messages[0].Kind, Is.EqualTo(ChatMessageKind.ToolCall));
            Assert.That(loaded.Messages[0].Text, Is.EqualTo("short result"));
            Assert.That(loaded.Messages[0].ToolName, Is.EqualTo("activity_search"));
        }

        [Test]
        public void ListConversations_ReturnsNewestFirst()
        {
            _store.SaveConversation(new ChatConversation { Id = "a", Title = "A", UpdatedAtUtc = System.DateTime.UtcNow.AddMinutes(-5) });
            _store.SaveConversation(new ChatConversation { Id = "b", Title = "B", UpdatedAtUtc = System.DateTime.UtcNow });

            var list = _store.ListConversations();
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list[0].Id, Is.EqualTo("b"));
        }

        [Test]
        public void DeleteConversation_RemovesIt()
        {
            _store.SaveConversation(new ChatConversation { Id = "x", Title = "X" });
            _store.DeleteConversation("x");

            Assert.That(_store.LoadConversation("x"), Is.Null);
        }

        [Test]
        public void SaveConversation_DefaultsEmptyTitle()
        {
            _store.SaveConversation(new ChatConversation { Id = "t" });

            var loaded = _store.LoadConversation("t");
            Assert.That(loaded.Title, Is.EqualTo("New chat"));
        }

        [Test]
        public void SaveConversation_UnpairedSurrogateInText_DoesNotThrow()
        {
            var conversation = new ChatConversation { Id = "conv-surrogate" };
            conversation.Messages.Add(new ChatHistoryMessage
            {
                Kind = ChatMessageKind.AgentText,
                Text = "hello \uD83D"
            });

            Assert.That(() => _store.SaveConversation(conversation), Throws.Nothing);

            var loaded = _store.LoadConversation("conv-surrogate");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Messages.Count, Is.EqualTo(1));
        }

        [Test]
        public void SaveConversation_FullEmoji_RoundTripsUnchanged()
        {
            var conversation = new ChatConversation { Id = "conv-emoji" };
            conversation.Messages.Add(new ChatHistoryMessage
            {
                Kind = ChatMessageKind.User,
                Text = "\uD83D\uDC4D ok"
            });

            _store.SaveConversation(conversation);

            var loaded = _store.LoadConversation("conv-emoji");
            Assert.That(loaded.Messages[0].Text, Is.EqualTo("\uD83D\uDC4D ok"));
        }
    }
}
