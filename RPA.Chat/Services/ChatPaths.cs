using System;
using System.IO;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Resolves the studio chat data directory. All chat persistence (settings, UI state,
    /// conversation history and the chat log) lives under %AppData%\AiRpaStudio\Chat.
    /// </summary>
    public static class ChatPaths
    {
        private static string _overrideDataDirectory;

        public static string DataDirectory => _overrideDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiRpaStudio",
            "Chat");

        /// <summary>
        /// Redirects the chat data directory (used by unit tests to isolate persistence).
        /// Pass null to restore the default %AppData%-based location.
        /// </summary>
        public static void OverrideDataDirectory(string path)
        {
            _overrideDataDirectory = path;
        }

        public static string LogDirectory => Path.Combine(DataDirectory, "Logs");

        public static string SettingsFile => Path.Combine(DataDirectory, "chat_settings.json");

        public static string UiStateFile => Path.Combine(DataDirectory, "chat_ui_state.json");

        public static string HistoryFile => Path.Combine(DataDirectory, "chat_history.json");

        public static string DefaultLogFile => Path.Combine(LogDirectory, "chat.log");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogDirectory);
        }
    }
}
