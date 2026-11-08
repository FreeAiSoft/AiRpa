using System;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Лёгкий логгер-фасад для сетевых операций панели NuGet. Библиотека RPA.NuGet не ссылается
    /// на сборку студии, поэтому по умолчанию ничего не пишет; хост (AiRpaStudio) подключает
    /// <see cref="Sink"/> к своему штатному логу RPA.Logging.Log (log.txt + панель «Log»).
    /// <para>
    /// Образец — ChatLog в RPA.Chat: тот же паттерн <c>Action&lt;string&gt; Sink</c>, чтобы не
    /// тянуть зависимость на студию и при этом писать диагностику в одно место.
    /// </para>
    /// </summary>
    public static class NuGetLog
    {
        /// <summary>Приёмник строки лога (хост пишет её в свой журнал). Null — лог выключен.</summary>
        public static Action<string> Sink { get; set; }

        public static void Debug(string message) => Write("DEBUG", message);

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message) => Write("ERROR", message);

        public static void Error(Exception exception, string message = null)
        {
            string text = message ?? (exception == null ? string.Empty : exception.Message);
            if (exception != null)
                text += " | " + exception.GetBaseException().Message;
            Write("ERROR", text);
        }

        private static void Write(string level, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [NuGet." + level + "] " + message;
            try
            {
                Sink?.Invoke(line);
            }
            catch
            {
                // Логирование не должно ломать работу панели.
            }
        }
    }
}
