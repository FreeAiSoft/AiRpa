using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace RPA.Runner
{
    /// <summary>
    /// Журнал прогона раннера (`OR-02`, шаг `W4`): что происходит, куда и в каком формате.
    /// <para>
    /// Формат файла — JSON-lines: одна запись на строку, поэтому лог можно читать
    /// построчно во время прогона (так его читает оркестратор) и он не ломается,
    /// если процесс убили посреди записи. Консоль получает человекочитаемый текст.
    /// </para>
    /// <para>
    /// Журнал не имеет права уронить прогон: ошибки записи в файл (нет прав, занятый файл)
    /// остаются в журнале как предупреждение, а робот продолжает работать — иначе диагностика
    /// мешала бы исполнению.
    /// </para>
    /// </summary>
    public interface IRunnerJournal : IDisposable
    {
        /// <summary>Идентификатор прогона, попадающий в каждую запись.</summary>
        string RunId { get; }

        /// <summary>Обычное сообщение.</summary>
        void Info(string message);

        /// <summary>Событие трекера робота (активность, состояние).</summary>
        void Track(string message);

        /// <summary>Ошибка.</summary>
        void Error(string message);
    }

    /// <summary>Файловый (JSON-lines) и консольный журнал прогона.</summary>
    public sealed class RunnerJournal : IRunnerJournal
    {
        private readonly object _sync = new object();
        private readonly string _logPath;
        private TextWriter _file;
        private readonly TextWriter _console;
        private bool _disposed;

        private RunnerJournal(string runId, string logPath, TextWriter file, TextWriter console)
        {
            RunId = runId;
            _logPath = logPath;
            _file = file;
            _console = console;
        }

        /// <inheritdoc />
        public string RunId { get; }

        /// <summary>Путь к файлу лога; <c>null</c>, если лог в файл не писался.</summary>
        public string LogPath => _logPath;

        /// <summary>
        /// Открывает журнал по параметрам <c>--log</c> и <c>--run-id</c>.
        /// <paramref name="console"/> — куда дублировать человекочитаемый текст (обычно <see cref="Console.Out"/>).
        /// </summary>
        public static RunnerJournal Open(RunnerOptions options, TextWriter console = null)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            string runId = options.ResolveRunId();
            TextWriter file = null;
            string logPath = null;

            if (!string.IsNullOrWhiteSpace(options.LogPath))
            {
                logPath = Path.GetFullPath(options.LogPath);
                string directory = Path.GetDirectoryName(logPath);

                try
                {
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    file = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        AutoFlush = true
                    };
                }
                catch (Exception ex)
                {
                    console?.WriteLine("[warn] не удалось открыть лог «" + logPath + "»: " + ex.Message);
                    file = null;
                }
            }

            return new RunnerJournal(runId, file == null ? null : logPath, file, console);
        }

        /// <inheritdoc />
        public void Info(string message)
        {
            Write("info", message);
        }

        /// <inheritdoc />
        public void Track(string message)
        {
            Write("track", message);
        }

        /// <inheritdoc />
        public void Error(string message)
        {
            Write("error", message);
        }

        private void Write(string level, string message)
        {
            if (_disposed)
            {
                return;
            }

            string text = message ?? string.Empty;

            lock (_sync)
            {
                _console?.WriteLine("[" + level + "] " + text);

                if (_file == null)
                {
                    return;
                }

                try
                {
                    var entry = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "time", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) },
                        { "level", level },
                        { "runId", RunId },
                        { "message", text }
                    };

                    var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    _file.WriteLine(serializer.Serialize(entry));
                }
                catch (Exception ex)
                {
                    _console?.WriteLine("[warn] запись в лог не удалась: " + ex.Message);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_file == null)
                {
                    return;
                }

                try
                {
                    _file.Flush();
                    _file.Dispose();
                }
                catch (Exception)
                {
                    // Закрытие лога не может быть причиной падения прогона.
                }
                finally
                {
                    _file = null;
                }
            }
        }
    }

    /// <summary>Журнал-пустышка: нужен там, где лог не требуется (тесты, встраивание).</summary>
    public sealed class NullRunnerJournal : IRunnerJournal
    {
        /// <summary>Общий экземпляр: журнал без состояния.</summary>
        public static readonly NullRunnerJournal Instance = new NullRunnerJournal();

        private NullRunnerJournal()
        {
            RunId = string.Empty;
        }

        /// <inheritdoc />
        public string RunId { get; }

        /// <inheritdoc />
        public void Info(string message)
        {
        }

        /// <inheritdoc />
        public void Track(string message)
        {
        }

        /// <inheritdoc />
        public void Error(string message)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
