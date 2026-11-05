using System;
using System.Activities;
using System.Activities.Validation;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

using RPA.Core;
using RPA.Helpers;

namespace RPA.Runner
{
    /// <summary>
    /// Исполнение одного прогона по разобранным параметрам (`OR-02`, шаги `W3`–`W5`).
    /// <para>
    /// Здесь собрано всё, что делает раннер, кроме разбора командной строки и `Main`:
    /// найти робота (<see cref="RobotLocator"/>), подключить каталог зависимостей
    /// (<see cref="RobotDependencyProbe"/>), загрузить активность общим движком
    /// (<see cref="WorkflowRunEngine"/>), проверить её (`--validate`), прогнать с пределом
    /// времени и отменой, записать лог и <c>result.json</c>.
    /// </para>
    /// <para>
    /// Метод не бросает исключений: любой исход превращается в <see cref="RunnerOutcome"/>
    /// с кодом возврата. Иначе `RpaRunner.exe` на чистой машине падал бы в необработанном
    /// исключении и оркестратор получал бы вместо кода — текст CLR.
    /// </para>
    /// </summary>
    public static class WorkflowRunnerHost
    {
        /// <summary>Прогоняет робота и возвращает итог с кодом возврата.</summary>
        public static RunnerOutcome Execute(
            RunnerOptions options,
            IRunnerJournal journal = null,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            journal = journal ?? NullRunnerJournal.Instance;

            var outcome = new RunnerOutcome
            {
                RunId = journal.RunId,
                StartedAt = DateTimeOffset.Now
            };

            try
            {
                journal.Info("прогон " + outcome.RunId + " начат; " + options.DescribeSource());

                // Код отказа — из локатора: «пакета нет» (6) и «пакет битый» (5) требуют разных
                // действий от оркестратора, поэтому раннер не сводит оба случая к одному коду.
                if (!RobotLocator.TryResolve(
                    options,
                    out RobotLocation location,
                    out string locationError,
                    out RunnerExitCode locationCode))
                {
                    return Fail(outcome, journal, locationCode, locationError);
                }

                ApplyLocation(outcome, location);
                journal.Info(location.Describe());

                foreach (string note in location.Notes)
                {
                    journal.Info(note);
                }

                if (location.Candidates.Count > 0)
                {
                    journal.Info("каталоги сборки: " + string.Join("; ", location.Candidates));
                }

                RobotDependencyProbe.Install(location.BinDirectory);

                if (!TryLoadActivity(location, out Activity activity, out string loadError))
                {
                    return Fail(outcome, journal, RunnerExitCode.LoadFailed, loadError);
                }

                journal.Info("workflow загружен: " + activity.GetType().FullName);

                if (options.ValidateOnly)
                {
                    return Validate(outcome, journal, activity);
                }

                journal.Info("прогон пошёл: аргументов "
                    + options.Arguments.Count.ToString(CultureInfo.InvariantCulture));

                WorkflowRunResult result = WorkflowRunEngine.Run(
                    activity,
                    options.Arguments,
                    options.Timeout,
                    cancellationToken,
                    args => journal.Track(DescribeEvent(args)));

                return Complete(outcome, journal, result);
            }
            catch (Exception ex)
            {
                return Fail(outcome, journal, RunnerExitCode.Faulted, ErrorText.Of(ex));
            }
            finally
            {
                outcome.FinishedAt = DateTimeOffset.Now;
                journal.Info(outcome.Summary);
                WriteResultFile(outcome, options.ResultPath, journal);
            }
        }

        private static RunnerOutcome Complete(RunnerOutcome outcome, IRunnerJournal journal, WorkflowRunResult result)
        {
            outcome.State = result.State.ToString();
            outcome.TimedOut = result.TimedOut;
            outcome.ExitCode = MapExitCode(result);

            foreach (KeyValuePair<string, object> output in result.Outputs)
            {
                outcome.Outputs[output.Key] = Convert.ToString(output.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (!result.IsSuccess)
            {
                outcome.ErrorText = result.ErrorText;
                journal.Error("прогон не завершился успехом: " + outcome.Summary);
            }

            return outcome;
        }

        /// <summary>Код возврата раннера по итогу движка (см. комментарий <see cref="RunnerExitCode"/>).</summary>
        internal static RunnerExitCode MapExitCode(WorkflowRunResult result)
        {
            if (result == null)
            {
                return RunnerExitCode.Faulted;
            }

            if (result.IsSuccess)
            {
                return RunnerExitCode.Success;
            }

            if (result.TimedOut)
            {
                return RunnerExitCode.TimedOut;
            }

            if (result.State == WorkflowRunState.Cancelled)
            {
                return RunnerExitCode.Cancelled;
            }

            return RunnerExitCode.Faulted;
        }

        private static RunnerOutcome Validate(RunnerOutcome outcome, IRunnerJournal journal, Activity activity)
        {
            ValidationResults results;

            try
            {
                results = ActivityValidationServices.Validate(activity);
            }
            catch (Exception ex)
            {
                return Fail(outcome, journal, RunnerExitCode.LoadFailed, "проверка workflow не выполнена: " + ErrorText.Of(ex));
            }

            List<ValidationError> errors = results?.Errors?.ToList() ?? new List<ValidationError>();

            if (errors.Count == 0)
            {
                outcome.State = "Validated";
                outcome.ExitCode = RunnerExitCode.Success;
                journal.Info("проверка workflow пройдена: ошибок нет");

                return outcome;
            }

            var text = new System.Text.StringBuilder();
            text.Append("проверка workflow нашла ошибок: ").Append(errors.Count.ToString(CultureInfo.InvariantCulture));

            foreach (ValidationError error in errors)
            {
                text.AppendLine().Append("  - ").Append(error.Message);

                if (!string.IsNullOrWhiteSpace(error.Id))
                {
                    text.Append(" (").Append(error.Id).Append(')');
                }
            }

            outcome.State = "Validated";
            return Fail(outcome, journal, RunnerExitCode.LoadFailed, text.ToString());
        }

        private static bool TryLoadActivity(RobotLocation location, out Activity activity, out string error)
        {
            activity = null;
            error = null;

            if (string.Equals(location.SourceKind, "workflow", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string xaml = File.ReadAllText(location.WorkflowPath);

                    return WorkflowRunEngine.TryLoadActivityFromText(xaml, out activity, out error);
                }
                catch (Exception ex)
                {
                    error = "не удалось прочитать XAML-файл «" + location.WorkflowPath + "»: " + ErrorText.Of(ex);
                    return false;
                }
            }

            try
            {
                object handle = Activator.CreateInstanceFrom(location.AssemblyPath, location.TypeName).Unwrap();

                if (handle is Activity loaded)
                {
                    activity = loaded;
                    return true;
                }

                error = "тип «" + location.TypeName + "» в сборке «" + location.AssemblyPath
                    + "» не является WF-активностью";
                return false;
            }
            catch (Exception ex)
            {
                error = "не удалось создать активность «" + location.TypeName + "» из сборки «"
                    + location.AssemblyPath + "»: " + ErrorText.Of(ex);
                return false;
            }
        }

        private static RunnerOutcome Fail(
            RunnerOutcome outcome,
            IRunnerJournal journal,
            RunnerExitCode code,
            string error)
        {
            outcome.ExitCode = code;
            outcome.ErrorText = error ?? string.Empty;

            if (string.IsNullOrEmpty(outcome.State) || outcome.State == "NotStarted")
            {
                outcome.State = "NotStarted";
            }

            journal.Error("прогон " + outcome.RunId + " — код "
                + ((int)code).ToString(CultureInfo.InvariantCulture) + ": "
                + RunnerExitCodes.Describe(code) + Environment.NewLine + (error ?? string.Empty));

            return outcome;
        }

        private static void ApplyLocation(RunnerOutcome outcome, RobotLocation location)
        {
            outcome.SourceKind = location.SourceKind;
            outcome.ProjectPath = location.ProjectPath;
            outcome.AssemblyPath = location.AssemblyPath;
            outcome.WorkflowPath = location.WorkflowPath;
            outcome.TypeName = location.TypeName;
            outcome.BinDirectory = location.BinDirectory;
            outcome.PackagePath = location.PackagePath;
            outcome.PackageId = location.PackageId;
            outcome.PackageVersion = location.PackageVersion;
            outcome.PackageContentHash = location.PackageContentHash;
        }

        private static string DescribeEvent(ActivitiesTracker.Entities.TrackingEventArgs args)
        {
            if (args == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(args.Message) ? "(без описания)" : args.Message;
        }

        /// <summary>
        /// Пишет <c>result.json</c>. Ошибка записи не меняет код прогона (робот уже отработал),
        /// но попадает в лог: без отчёта оркестратор не увидит выходные аргументы.
        /// </summary>
        private static void WriteResultFile(RunnerOutcome outcome, string resultPath, IRunnerJournal journal)
        {
            if (string.IsNullOrWhiteSpace(resultPath))
            {
                return;
            }

            try
            {
                string full = Path.GetFullPath(resultPath);
                string directory = Path.GetDirectoryName(full);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                File.WriteAllText(full, serializer.Serialize(outcome.ToJson()));

                journal.Info("отчёт: " + full);
            }
            catch (Exception ex)
            {
                journal.Error("не удалось записать отчёт «" + resultPath + "»: " + ErrorText.Of(ex));
            }
        }
    }
}
