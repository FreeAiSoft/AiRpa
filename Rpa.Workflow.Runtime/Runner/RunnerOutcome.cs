using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RPA.Runner
{
    /// <summary>
    /// Итог одного прогона раннера (`OR-02`): код возврата, состояние, выходные аргументы,
    /// ошибка со стеком и источник.
    /// <para>
    /// Это же модель отчёта <c>result.json</c> и источник строки в логе. Отдельный тип нужен
    /// потому, что вызывающей стороне (оркестратор `OR-05`, запускатор `LN-02`, тест) нужен
    /// не «текст на экране», а структура: по ней принимают решение «повторить / отменить / починить робота».
    /// </para>
    /// </summary>
    public sealed class RunnerOutcome
    {
        public RunnerOutcome()
        {
            Outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            RunId = string.Empty;
            State = "NotStarted";
            // Пустая ошибка, а не null: оркестратор (OR-05) читает result.json и не обязан
            // проверять поле на null — «ошибки нет» и «поля нет» должны выглядеть одинаково.
            ErrorText = string.Empty;
        }

        /// <summary>Код возврата процесса (см. <see cref="RunnerExitCode"/>).</summary>
        public RunnerExitCode ExitCode { get; set; }

        /// <summary>Идентификатор прогона: тот, что пришёл в <c>--run-id</c>, или сгенерированный.</summary>
        public string RunId { get; set; }

        /// <summary>Состояние: <c>Completed</c>, <c>Faulted</c>, <c>Cancelled</c>, <c>Aborted</c>, <c>Validated</c>, <c>NotStarted</c>.</summary>
        public string State { get; set; }

        /// <summary>Прогон остановлен пределом времени.</summary>
        public bool TimedOut { get; set; }

        /// <summary>Выходные аргументы робота (значения приведены к строке — так их читает отчёт).</summary>
        public IDictionary<string, string> Outputs { get; private set; }

        /// <summary>Ошибка со стеком; пустая строка при успехе.</summary>
        public string ErrorText { get; set; }

        /// <summary>Способ указания робота: <c>project</c>, <c>workflow</c> или <c>assembly</c>.</summary>
        public string SourceKind { get; set; }

        /// <summary>Проект робота, если прогон шёл по проекту.</summary>
        public string ProjectPath { get; set; }

        /// <summary>Собранная сборка робота, если прогон шёл по сборке.</summary>
        public string AssemblyPath { get; set; }

        /// <summary>XAML-сценарий, если он был указан.</summary>
        public string WorkflowPath { get; set; }

        /// <summary>Тип активности.</summary>
        public string TypeName { get; set; }

        /// <summary>Каталог зависимостей робота.</summary>
        public string BinDirectory { get; set; }

        /// <summary>Пакет робота, если прогон шёл из пакета (`OR-03`).</summary>
        public string PackagePath { get; set; }

        /// <summary>Идентификатор робота из манифеста пакета.</summary>
        public string PackageId { get; set; }

        /// <summary>Версия пакета из манифеста.</summary>
        public string PackageVersion { get; set; }

        /// <summary>Хэш содержимого пакета: по нему оркестратор опознаёт байты версии.</summary>
        public string PackageContentHash { get; set; }

        /// <summary>Когда прогон начался.</summary>
        public DateTimeOffset StartedAt { get; set; }

        /// <summary>Когда прогон закончился.</summary>
        public DateTimeOffset FinishedAt { get; set; }

        /// <summary>Длительность прогона в миллисекундах.</summary>
        public long DurationMs
        {
            get
            {
                double milliseconds = (FinishedAt - StartedAt).TotalMilliseconds;

                return milliseconds > 0 ? (long)milliseconds : 0L;
            }
        }

        /// <summary>Прогон завершён успешно.</summary>
        public bool IsSuccess => ExitCode == RunnerExitCode.Success;

        /// <summary>Однострочный итог для консоли и лога.</summary>
        public string Summary
        {
            get
            {
                var text = new StringBuilder();
                text.Append("прогон ").Append(RunId).Append(": ").Append(State);
                text.Append(" (код ").Append(((int)ExitCode).ToString(CultureInfo.InvariantCulture)).Append(")");

                if (TimedOut)
                {
                    text.Append(", таймаут");
                }

                if (Outputs.Count > 0)
                {
                    text.Append("; выход: ");

                    bool first = true;

                    foreach (KeyValuePair<string, string> output in Outputs)
                    {
                        if (!first)
                        {
                            text.Append(", ");
                        }

                        text.Append(output.Key).Append('=').Append(output.Value);
                        first = false;
                    }
                }

                if (!string.IsNullOrWhiteSpace(ErrorText))
                {
                    text.Append("; ошибка: ").Append(FirstLine(ErrorText));
                }

                return text.ToString();
            }
        }

        /// <summary>Модель <c>result.json</c>: только простые значения, без WF-типов.</summary>
        public IDictionary<string, object> ToJson()
        {
            var outputs = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, string> output in Outputs)
            {
                outputs[output.Key] = output.Value;
            }

            var model = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "runId", RunId },
                { "status", Status },
                { "exitCode", (int)ExitCode },
                { "state", State },
                { "timedOut", TimedOut },
                { "startedAt", StartedAt.ToString("o", CultureInfo.InvariantCulture) },
                { "finishedAt", FinishedAt.ToString("o", CultureInfo.InvariantCulture) },
                { "durationMs", DurationMs },
                { "source", SourceKind ?? string.Empty },
                { "project", ProjectPath ?? string.Empty },
                { "assembly", AssemblyPath ?? string.Empty },
                { "workflow", WorkflowPath ?? string.Empty },
                { "type", TypeName ?? string.Empty },
                { "bin", BinDirectory ?? string.Empty },
                { "package", PackagePath ?? string.Empty },
                { "packageId", PackageId ?? string.Empty },
                { "packageVersion", PackageVersion ?? string.Empty },
                { "packageHash", PackageContentHash ?? string.Empty },
                { "outputs", outputs },
                { "error", ErrorText ?? string.Empty }
            };

            return model;
        }

        /// <summary>Статус одной строкой: <c>success</c> / <c>failed</c> / <c>cancelled</c> / <c>timeout</c> / <c>not-built</c> / <c>bad-arguments</c>.</summary>
        public string Status
        {
            get
            {
                switch (ExitCode)
                {
                    case RunnerExitCode.Success:
                        return "success";
                    case RunnerExitCode.Cancelled:
                        return "cancelled";
                    case RunnerExitCode.TimedOut:
                        return "timeout";
                    case RunnerExitCode.BadArguments:
                        return "bad-arguments";
                    case RunnerExitCode.LoadFailed:
                        return "load-failed";
                    case RunnerExitCode.RobotNotFound:
                        return "not-built";
                    default:
                        return "failed";
                }
            }
        }

        private static string FirstLine(string text)
        {
            int index = text.IndexOfAny(new[] { '\r', '\n' });

            return index < 0 ? text : text.Substring(0, index);
        }
    }
}
