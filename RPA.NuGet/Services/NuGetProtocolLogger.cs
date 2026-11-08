using System;
using System.Threading.Tasks;
using NuGet.Common;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Адаптер NuGet.Protocol → лог панели NuGet. Раньше все запросы шли с <c>NullLogger.Instance</c>,
    /// и сетевые ошибки (TLS, HTTP-статусы, ретраи) терялись. Этот логгер транслирует их в
    /// <see cref="NuGetLog.Sink"/>, то есть в log.txt студии и панель «Log».
    /// </summary>
    public sealed class NuGetProtocolLogger : ILogger
    {
        public static readonly NuGetProtocolLogger Instance = new NuGetProtocolLogger();

        private NuGetProtocolLogger()
        {
        }

        public void LogDebug(string data) => NuGetLog.Debug(data);

        public void LogVerbose(string data) => NuGetLog.Debug(data);

        public void LogInformation(string data) => NuGetLog.Info(data);

        public void LogMinimal(string data) => NuGetLog.Info(data);

        public void LogWarning(string data) => NuGetLog.Warn(data);

        public void LogError(string data) => NuGetLog.Error(data);

        public void LogInformationSummary(string data) => NuGetLog.Info(data);

        public void Log(LogLevel level, string data) => Write(level, data);

        public Task LogAsync(LogLevel level, string data)
        {
            Write(level, data);
            return Task.CompletedTask;
        }

        public void Log(ILogMessage message) => WriteMessage(message);

        public Task LogAsync(ILogMessage message)
        {
            WriteMessage(message);
            return Task.CompletedTask;
        }

        private static void Write(LogLevel level, string data)
        {
            switch (level)
            {
                case LogLevel.Error:
                    NuGetLog.Error(data);
                    break;
                case LogLevel.Warning:
                    NuGetLog.Warn(data);
                    break;
                case LogLevel.Minimal:
                case LogLevel.Information:
                    NuGetLog.Info(data);
                    break;
                default:
                    NuGetLog.Debug(data);
                    break;
            }
        }

        private static void WriteMessage(ILogMessage message)
        {
            if (message == null)
                return;

            string prefix = message.Code == NuGetLogCode.Undefined
                ? string.Empty
                : "[" + message.Code + "] ";

            Write(message.Level, prefix + message.Message);
        }
    }
}
