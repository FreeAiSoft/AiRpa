using System;
using System.IO;
using Newtonsoft.Json;
using Agent.Core.Contracts;
using Agent.Core.Models;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Loads/saves the UI state JSON at %AppData%\AiRpaStudio\Chat\chat_ui_state.json,
    /// implementing the shared <see cref="IUiStateStore"/> contract so the shared
    /// <see cref="Agent.Models.ChatModel"/> persists its session state.
    /// </summary>
    public sealed class UiStateStore : IUiStateStore
    {
        public UiStateData Load()
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

        public void Save(UiStateData state)
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

            if (!Enum.IsDefined(typeof(MainTab), state.ActiveTab))
                state.ActiveTab = MainTab.Chat;
        }
    }
}
