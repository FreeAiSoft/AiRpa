using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Agent.Core.Models;

namespace RPA.Chat.Services
{
    /// <summary>Chat tab view: the active conversation or the history list.</summary>
    public enum ChatViewMode
    {
        Conversation,
        History
    }

    /// <summary>Main tabs of the chat panel.</summary>
    public enum ChatMainTab
    {
        Chat,
        Settings
    }

    /// <summary>Per-session UI state persisted between studio sessions.</summary>
    public class UiStateData
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public ChatViewMode ChatViewMode { get; set; }

        /// <summary>Id of the active conversation, or empty when none.</summary>
        public string LastConversationId { get; set; } = "";

        /// <summary>Agent mode: Plan, Act or Help.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public AgentMode AgentMode { get; set; }

        /// <summary>Active main tab.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ChatMainTab ActiveTab { get; set; }
    }

    /// <summary>Loads/saves the UI state JSON at %AppData%\AiRpaStudio\Chat\chat_ui_state.json.</summary>
    public static class UiStateStore
    {
        public static UiStateData Load()
        {
            try
            {
                if (File.Exists(ChatPaths.UiStateFile))
                {
                    var json = File.ReadAllText(ChatPaths.UiStateFile);
                    var state = JsonConvert.DeserializeObject<UiStateData>(json) ?? new UiStateData();
                    EnsureDefaults(state);
                    return state;
                }
            }
            catch
            {
                // ignore read errors — fall back to defaults
            }

            var defaults = new UiStateData();
            EnsureDefaults(defaults);
            return defaults;
        }

        public static void Save(UiStateData state)
        {
            if (state == null) return;
            try
            {
                ChatPaths.EnsureDirectories();
                File.WriteAllText(ChatPaths.UiStateFile, JsonConvert.SerializeObject(state, Formatting.Indented));
            }
            catch
            {
                // ignore write errors
            }
        }

        private static void EnsureDefaults(UiStateData state)
        {
            if (state == null) return;

            if (state.ChatViewMode != ChatViewMode.Conversation && state.ChatViewMode != ChatViewMode.History)
                state.ChatViewMode = ChatViewMode.Conversation;

            if (state.LastConversationId == null)
                state.LastConversationId = "";

            if (state.AgentMode != AgentMode.Plan && state.AgentMode != AgentMode.Act && state.AgentMode != AgentMode.Help)
                state.AgentMode = AgentMode.Plan;

            if (!Enum.IsDefined(typeof(ChatMainTab), state.ActiveTab))
                state.ActiveTab = ChatMainTab.Chat;
        }
    }
}
