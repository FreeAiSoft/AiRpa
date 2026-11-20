using System;
using Agent.Core.Contracts;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Adapts <see cref="IAgentLogger"/> (the model-layer logging seam) to <see cref="ChatLog"/>,
    /// the chat module's file/sink logger. This lets model diagnostics (cache-loss causes, Do-it
    /// mode switches) reach the same log the host already writes to, without the model depending
    /// on the host's logging infrastructure. Logging-only: it never changes chat behaviour.
    /// </summary>
    public sealed class ChatLogLogger : IAgentLogger
    {
        public static readonly ChatLogLogger Instance = new ChatLogLogger();

        private ChatLogLogger()
        {
        }

        public void Info(string message) => ChatLog.Info(message);

        public void Warn(string message) => ChatLog.Warn(message);

        public void Error(string message) => ChatLog.Error(message);

        public void Error(Exception exception, string message = null) => ChatLog.Error(exception, message);
    }
}
