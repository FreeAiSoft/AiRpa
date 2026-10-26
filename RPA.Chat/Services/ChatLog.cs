using System;
using System.IO;
using System.Text;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Lightweight file logger for the chat module. When the studio host wires
    /// <see cref="Sink"/> to its own logging (RPA.Logging.Log), every line is forwarded
    /// there instead of being appended to the local file. This keeps RPA.Chat independent
    /// of the studio application assembly while still routing diagnostics to one place.
    /// </summary>
    public static class ChatLog
    {
        private static readonly object Sync = new object();

        /// <summary>
        /// Optional sink that receives every formatted log line. When set, the local
        /// file logger is bypassed (the host owns persistence).
        /// </summary>
        public static Action<string> Sink { get; set; }

        public static void Debug(string message) => Write("DEBUG", message);

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message) => Write("ERROR", message);

        public static void Error(Exception exception, string message = null)
        {
            Write("ERROR", message ?? (exception == null ? "" : exception.Message));
        }

        private static void Write(string level, string message)
        {
            if (message == null) return;

            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message;
            try
            {
                var sink = Sink;
                if (sink != null)
                {
                    sink(line);
                    return;
                }

                lock (Sync)
                {
                    ChatPaths.EnsureDirectories();
                    File.AppendAllText(ChatPaths.DefaultLogFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never break the chat.
            }
        }
    }
}
