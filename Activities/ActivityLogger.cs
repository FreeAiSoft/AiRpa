using System;
using NLog;

namespace Activities
{
    public static class ActivityLogger
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public static Action<string> OutputAction;

        public static void Log(LogLevel logLevel, Exception ex, string message)
        {
            _logger.Log(logLevel, ex, message);
            OutputAction?.Invoke(message);
        }

        public static void Log(LogLevel logLevel, Exception ex, string message, bool isLogging)
        {
            if (isLogging)
            {
                Log(logLevel, ex, message);
            }
        }

        public static void Info(string message)
        {
            Log(LogLevel.Info, null, message);
        }

        public static void Info(string message, bool isLogging)
        {
            if (isLogging)
            {
                Info(message);
            }
        }
    }
}
