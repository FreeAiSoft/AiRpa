using System;
using System.IO;
using System.Threading;

using RPA.Runner;

namespace RpaRunner
{
    /// <summary>
    /// Точка входа <c>RpaRunner.exe</c> (`OR-02`).
    /// <para>
    /// Здесь только то, чего не должно быть в библиотеке: консоль, Ctrl+C и код возврата.
    /// Никакого UI — ни <c>MessageBox</c>, ни <c>Console.ReadKey()</c>, ни ожидания ввода:
    /// процесс запускается планировщиком и оркестратором, где некому нажимать клавиши
    /// (страж <c>RunnerSourceGuardTests</c>).
    /// </para>
    /// <para>
    /// Отмена: первый Ctrl+C вежливо отменяет прогон (робот получает отмену через
    /// <see cref="CancellationToken"/>), второй — тоже просто отмена: раннеру не нужно
    /// «жёсткое» завершение, потому что зависший прогон всё равно ограничен <c>--timeout</c>.
    /// </para>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // UTF-8 в консоли: раннер печатает по-русски, а планировщик/оркестратор читает stdout
            // перенаправленным. Ошибки кодировки не имеют права уронить прогон.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { }

            if (!RunnerOptions.TryParse(args, out RunnerOptions options, out string error))
            {
                Console.Error.WriteLine("Ошибка аргументов: " + error);
                Console.Error.WriteLine();
                Console.Error.WriteLine(RunnerUsage.Text);

                return (int)RunnerExitCode.BadArguments;
            }

            if (options.ShowHelp)
            {
                Console.WriteLine(RunnerUsage.Text);

                return (int)RunnerExitCode.Success;
            }

            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler onCancel = (sender, eventArgs) =>
                {
                    // Процесс не должен умирать по Ctrl+C: прогон отменяется штатно, чтобы
                    // успеть записать отчёт и лог (иначе оркестратор не узнает причину).
                    eventArgs.Cancel = true;
                    cancellation.Cancel();
                };

                Console.CancelKeyPress += onCancel;

                try
                {
                    using (RunnerJournal journal = RunnerJournal.Open(options, Console.Out))
                    {
                        RunnerOutcome outcome = WorkflowRunnerHost.Execute(options, journal, cancellation.Token);

                        Console.Out.WriteLine(outcome.Summary);

                        return (int)outcome.ExitCode;
                    }
                }
                catch (Exception ex)
                {
                    // Последняя страховка: даже падение самого раннера обязано дать код и текст,
                    // а не «процесс завершился с -532462766».
                    Console.Error.WriteLine("Раннер упал: " + RPA.Helpers.ErrorText.Of(ex));

                    return (int)RunnerExitCode.Faulted;
                }
                finally
                {
                    Console.CancelKeyPress -= onCancel;
                }
            }
        }
    }
}
