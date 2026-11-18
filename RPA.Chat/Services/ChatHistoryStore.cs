using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Agent.Core.Contracts;
using Agent.Core.Models;

namespace RPA.Chat.Services
{
    /// <summary>
    /// JSON-backed <see cref="IChatHistoryStore"/>. The store lives at
    /// %AppData%\AiRpaStudio\Chat\chat_history.json. The studio chat does not scope history
    /// per workspace, so <see cref="WorkspaceId"/> is accepted for the shared contract but ignored.
    /// </summary>
    public sealed class ChatHistoryStore : IChatHistoryStore
    {
        public const int MaxStoredConversations = 50;

        private static readonly object Sync = new object();

        /// <summary>Щадящая UTF-8 без BOM: недопустимые символы заменяются на U+FFFD, а не роняют запись.</summary>
        private static readonly UTF8Encoding WriteEncoding = new UTF8Encoding(false, false);

        /// <summary>Accepted for <see cref="IChatHistoryStore"/> compatibility; the studio chat does not scope history.</summary>
        public string WorkspaceId { get; set; } = "";

        /// <summary>Creates the chat data directory if needed.</summary>
        public void Initialize()
        {
            ChatPaths.EnsureDirectories();
        }

        /// <summary>Returns conversation metadata (newest first), without messages.</summary>
        public IReadOnlyList<ChatConversation> ListConversations(int limit = MaxStoredConversations)
        {
            var result = ReadAll();
            result.Sort((a, b) => b.UpdatedAtUtc.CompareTo(a.UpdatedAtUtc));
            if (result.Count > limit)
                result.RemoveRange(limit, result.Count - limit);
            return result;
        }

        /// <summary>Loads a full conversation (messages in display order), or null if not found.</summary>
        public ChatConversation LoadConversation(string id)
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
        public void SaveConversation(ChatConversation conversation)
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

        /// <summary>Sets the important flag without touching messages or timestamps.</summary>
        public void SetConversationImportant(string id, bool isImportant)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            lock (Sync)
            {
                var all = ReadAll();
                var changed = false;
                foreach (var conversation in all)
                {
                    if (string.Equals(conversation.Id, id, StringComparison.Ordinal))
                    {
                        conversation.IsImportant = isImportant;
                        changed = true;
                        break;
                    }
                }
                if (changed) WriteAll(all);
            }
        }

        /// <summary>Deletes a conversation by id.</summary>
        public void DeleteConversation(string id)
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
        public void DeleteAllConversations()
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
            File.WriteAllText(ChatPaths.HistoryFile, JsonConvert.SerializeObject(conversations, Formatting.Indented), WriteEncoding);
        }
    }
}

