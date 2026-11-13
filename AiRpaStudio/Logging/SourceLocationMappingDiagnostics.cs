using System;
using System.Activities;
using System.Activities.Debugger;
using System.Globalization;
using System.IO;

namespace RPA.Logging
{
    /// <summary>
    /// Файловая диагностика построения SourceLocationMapping в rehosted-дизайнере WF.
    /// <para>
    /// Это очень хрупкое место: штатный маппинг дизайнера заполняется только штатной десериализацией
    /// XAML, а loose-путь открытия (<c>LooseXamlDocumentPersistenceService</c>) идёт мимо него и строит
    /// маппинг вручную через <see cref="SourceLocationProvider.CollectMapping"/>. Любое расхождение
    /// между штатным и ручным маппингом даёт «точка останова не ставится». Поэтому каждый шаг создания
    /// маппинга пишется в отдельный файл, чтобы дефект можно было разобрать по следу.
    /// </para>
    /// <para>
    /// Запись идёт в <see cref="LogPath"/> (по умолчанию — файл во временном каталоге). Тесты подменяют
    /// путь на собственный временный файл и очищают его через <see cref="Reset"/>. Класс никогда не
    /// бросает исключений наружу: диагностика не должна влиять на работу студии.
    /// </para>
    /// </summary>
    internal static class SourceLocationMappingDiagnostics
    {
        private static readonly object Sync = new object();

        private static string _logPath = Path.Combine(Path.GetTempPath(), "airpa-sourcelocation-mapping.log");

        /// <summary>Путь к файлу диагностики. Пустое значение сбрасывается на путь по умолчанию.</summary>
        internal static string LogPath
        {
            get => _logPath;
            set => _logPath = string.IsNullOrWhiteSpace(value)
                ? Path.Combine(Path.GetTempPath(), "airpa-sourcelocation-mapping.log")
                : value;
        }

        /// <summary>Включает/выключает запись. По умолчанию включено.</summary>
        internal static bool Enabled { get; set; } = true;

        /// <summary>Добавляет строку в файл диагностики и в отладочный вывод.</summary>
        internal static void Write(string message)
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}{2}",
                    DateTime.Now,
                    message,
                    Environment.NewLine);

                lock (Sync)
                {
                    File.AppendAllText(LogPath, line);
                }

                System.Diagnostics.Debug.WriteLine("[SourceLocationMapping] " + message);
            }
            catch (Exception)
            {
                // диагностика не должна влиять на работу студии
            }
        }

        /// <summary>Логирует исключение с контекстом и стеком.</summary>
        internal static void WriteException(Exception ex, string context)
        {
            if (ex == null)
            {
                return;
            }

            Write(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} (type {2})",
                context,
                ex.Message,
                ex.GetType().FullName ?? ex.GetType().Name));

            Write("  " + (ex.StackTrace ?? "<no stack>").Replace(Environment.NewLine, Environment.NewLine + "  "));
        }

        /// <summary>Читаемое описание <see cref="SourceLocation"/> для лога.</summary>
        internal static string Describe(SourceLocation location)
        {
            if (location == null)
            {
                return "<null>";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "SourceLocation[{0} L{1}C{2}-L{3}C{4}]",
                location.FileName ?? "<no-file>",
                location.StartLine,
                location.StartColumn,
                location.EndLine,
                location.EndColumn);
        }

        /// <summary>Читаемое описание <see cref="Activity"/> для лога.</summary>
        internal static string Describe(Activity activity)
        {
            if (activity == null)
            {
                return "<null>";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} [DisplayName={1}, Id={2}]",
                activity.GetType().FullName ?? activity.GetType().Name,
                activity.DisplayName ?? "<null>",
                activity.Id ?? "<null>");
        }

        /// <summary>Очищает файл диагностики (для тестов и старта нового прогона).</summary>
        internal static void Reset()
        {
            try
            {
                lock (Sync)
                {
                    File.WriteAllText(LogPath, string.Empty);
                }
            }
            catch (Exception)
            {
                // файл может быть занят другим процессом — не критично для диагностики
            }
        }
    }
}
