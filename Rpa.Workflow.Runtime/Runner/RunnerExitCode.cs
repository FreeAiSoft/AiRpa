using System;
using System.Collections.Generic;
using System.Text;

namespace RPA.Runner
{
    /// <summary>
    /// Коды возврата <c>RpaRunner.exe</c> (`OR-02`).
    /// <para>
    /// Таблица кодов — часть контракта: по ней оркестратор (`OR-05`) и запускатор (`LN-02`) решают,
    /// что делать с прогоном, поэтому значения закреплены числами и проверяются тестами
    /// (<c>RunnerUsageTests</c> и <c>RunnerOptionsTests</c>). Ноль — только успех; любой нештатный
    /// исход имеет собственный код, чтобы «узнать причину» не требовало чтения лога.
    /// </para>
    /// <para>
    /// Коды движка (<see cref="RPA.Core.WorkflowRunResult.ExitCode"/> = 0/1) остаются прежними —
    /// это контракт студии. Раннер их уточняет: падение — 1, отмена — 3, таймаут — 4, —
    /// потому что для оркестратора это разные поводы (повторить, отменить, увеличить предел).
    /// </para>
    /// </summary>
    public enum RunnerExitCode
    {
        /// <summary>Прогон завершён, выходные аргументы получены.</summary>
        Success = 0,

        /// <summary>Прогон упал на необработанном исключении (ошибка робота).</summary>
        Faulted = 1,

        /// <summary>Неверные аргументы командной строки: печатается справка.</summary>
        BadArguments = 2,

        /// <summary>Прогон отменён (Ctrl+C или отмена вызывающей стороной).</summary>
        Cancelled = 3,

        /// <summary>Прогон не уложился в предел времени и был остановлен движком.</summary>
        TimedOut = 4,

        /// <summary>Не удалось загрузить или проверить workflow (битый XAML, нет типа, ошибки валидации).</summary>
        LoadFailed = 5,

        /// <summary>Робот не найден или не собран: сборки нет в <c>bin</c> проекта.</summary>
        RobotNotFound = 6
    }

    /// <summary>
    /// Текстовые описания кодов возврата — единственный источник для справки <c>--help</c>
    /// и для сообщений в логе. Справка собирается из этого класса, поэтому код не может
    /// появиться в <see cref="RunnerExitCode"/> и не попасть в документацию.
    /// </summary>
    public static class RunnerExitCodes
    {
        /// <summary>Все коды в порядке возрастания значения.</summary>
        public static IReadOnlyList<RunnerExitCode> All { get; } = new[]
        {
            RunnerExitCode.Success,
            RunnerExitCode.Faulted,
            RunnerExitCode.BadArguments,
            RunnerExitCode.Cancelled,
            RunnerExitCode.TimedOut,
            RunnerExitCode.LoadFailed,
            RunnerExitCode.RobotNotFound
        };

        /// <summary>Описание кода: одна строка без перевода строки.</summary>
        public static string Describe(RunnerExitCode code)
        {
            switch (code)
            {
                case RunnerExitCode.Success:
                    return "прогон завершён, выходные аргументы получены";
                case RunnerExitCode.Faulted:
                    return "прогон упал на необработанном исключении";
                case RunnerExitCode.BadArguments:
                    return "неверные аргументы командной строки";
                case RunnerExitCode.Cancelled:
                    return "прогон отменён";
                case RunnerExitCode.TimedOut:
                    return "прогон остановлен по пределу времени";
                case RunnerExitCode.LoadFailed:
                    return "workflow не загрузился или не прошёл проверку";
                case RunnerExitCode.RobotNotFound:
                    return "робот не найден или не собран (нет сборки в bin проекта)";
                default:
                    return "неизвестный код";
            }
        }

        /// <summary>Таблица «код — описание» для справки и лога.</summary>
        public static string Table()
        {
            var text = new StringBuilder();

            foreach (RunnerExitCode code in All)
            {
                text.Append("  ").Append(((int)code).ToString()).Append(" — ").Append(Describe(code)).AppendLine();
            }

            return text.ToString();
        }
    }
}
