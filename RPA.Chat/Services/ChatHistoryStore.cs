using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using VibeCoder.Core.Models;

namespace RPA.Chat.Services
{
    /// <summary>A single chat message persisted in the chat history.</summary>
    public class ChatHistoryMessage
    {
        public ChatMessageKind Kind { get; set; } = ChatMessageKind.SystemNotice;
        public string Text { get; set; } = "";
        public string Reasoning { get; set; } = "";
        public string Mode { get; set; } = "";
        public string ToolName { get; set; } = "";
    }

    /// <summary>A saved chat conversation with its messages.</summary>
    public class ChatConversation
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<ChatHistoryMessage> Messages { get; set; } = new List<ChatHistoryMessage>();

        /// <summary>Text shown in the history dropdown.</summary>
        public string DisplayTitle
        {
            get
            {
                var title = string.IsNullOrWhiteSpace(Title) ? "(empty)" : Title;
                var when = UpdatedAtUtc == default(DateTime)
                    ? ""
                    : "  ·  " + UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                return title + when;
            }
        }

        /// <summary>Title for a history card. The default "New chat" placeholder is hidden.</summary>
        public string CardTitle
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Title)) return "";
                return Title == "New chat" ? "" : Title;
            }
        }

        /// <summary>Local date shown on a history card.</summary>
        public string DisplayDate => UpdatedAtUtc == default(DateTime)
            ? ""
            : UpdatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        /// <summary>
        /// First message with actual text, flattened to a single line. Used as the
        /// one-line preview on the history card (the view truncates it with an ellipsis).
        /// </summary>
        public string Preview
        {
            get
            {
                if (Messages == null) return "";
                foreach (var message in Messages)
                {
                    if (string.IsNullOrWhiteSpace(message?.Text)) continue;
                    var singleLine = message.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                    if (singleLine.Length > 0) return singleLine;
                }
                return "";
            }
        }
    }

    /// <summary>
    /// JSON-backed chat history (replaces the VSIX SQLite store). The store lives at
    /// %AppData%\AiRpaStudio\Chat\chat_history.json.
    /// </summary>
    public static class ChatHistoryStore
    {
        public const int MaxStoredConversations = 50;

        private static readonly object Sync = new object();

        /// <summary>Creates the history file (empty store) if it does not exist yet.</summary>
        public static void Initialize()
        {
            lock (Sync)
            {
                if (!File.Exists(ChatPaths.HistoryFile))
                {
                    ChatPaths.EnsureDirectories();
                    WriteAll(new List<ChatConversation>());
                }
            }
        }

        /// <summary>Returns conversation metadata (newest first), without messages.</summary>
        public static IReadOnlyList<ChatConversation> ListConversations(int limit = MaxStoredConversations)
        {
            var all = ReadAll();
            all.Sort((a, b) => b.UpdatedAtUtc.CompareTo(a.UpdatedAtUtc));
            if (limit <= 0) limit = MaxStoredConversations;
            if (all.Count <= limit) return all;

            var result = new List<ChatConversation>(limit);
            for (var i = 0; i < limit; i++)
                result.Add(all[i]);
            return result;
        }

        /// <summary>Loads a full conversation, or null if not found.</summary>
        public static ChatConversation LoadConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var conversation in ReadAll())
            {
                if (string.Equals(conversation.Id, id, StringComparison.Ordinal))
                    return conversation;
            }
            return null;
        }

        /// <summary>Inserts or updates a conversation and its messages.</summary>
        public static void SaveConversation(ChatConversation conversation)
        {
            if (conversation == null || string.IsNullOrWhiteSpace(conversation.Id)) return;

            if (string.IsNullOrWhiteSpace(conversation.Title))
                conversation.Title = "New chat";

            if (conversation.Messages == null)
                conversation.Messages = new List<ChatHistoryMessage>();

            lock (Sync)
            {
                var all = ReadAll();
                var replaced = false;
                for (var i = 0; i < all.Count; i++)
                {
                    if (string.Equals(all[i].Id, conversation.Id, StringComparison.Ordinal))
                    {
                        all[i] = conversation;
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    all.Add(conversation);
                    if (all.Count > MaxStoredConversations)
                    {
                        all.Sort((a, b) => b.UpdatedAtUtc.CompareTo(a.UpdatedAtUtc));
                        all.RemoveRange(MaxStoredConversations, all.Count - MaxStoredConversations);
                    }
                }

                WriteAll(all);
            }
        }

        /// <summary>Deletes a conversation by id.</summary>
        public static void DeleteConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (Sync)
            {
                var all = ReadAll();
                all.RemoveAll(c => string.Equals(c.Id, id, StringComparison.Ordinal));
                WriteAll(all);
            }
        }

        /// <summary>Deletes every stored conversation.</summary>
        public static void DeleteAllConversations()
        {
            lock (Sync)
            {
                WriteAll(new List<ChatConversation>());
            }
        }

        private static List<ChatConversation> ReadAll()
        {
            try
            {
                if (File.Exists(ChatPaths.HistoryFile))
                {
                    var json = File.ReadAllText(ChatPaths.HistoryFile);
                    var list = JsonConvert.DeserializeObject<List<ChatConversation>>(json) ?? new List<ChatConversation>();
                    foreach (var conversation in list)
                        if (conversation.Messages == null)
                            conversation.Messages = new List<ChatHistoryMessage>();
                    return list;
                }
            }
            catch
            {
                // ignore read errors — fall back to an empty store
            }

            return new List<ChatConversation>();
        }

        private static void WriteAll(List<ChatConversation> conversations)
        {
            ChatPaths.EnsureDirectories();
            File.WriteAllText(ChatPaths.HistoryFile, JsonConvert.SerializeObject(conversations, Formatting.Indented));
        }
    }
}

