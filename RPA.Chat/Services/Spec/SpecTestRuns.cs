using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RPA.Chat.Services.Spec
{
    /// <summary>Запрос прогона робота для тест-кейса (<c>TZ-08</c>).</summary>
    public sealed class SpecRunRequest
    {
        public string WorkflowPath { get; set; } = string.Empty;

        public Dictionary<string, string> Inputs { get; set; } = new Dictionary<string, string>();

        public int TimeoutSeconds { get; set; } = 120;
    }

    /// <summary>Итог прогона: состояние, код возврата, выходные аргументы, ошибка со стеком.</summary>
    public sealed class SpecRunOutcome
    {
        public bool Ok { get; set; }

        public string State { get; set; } = string.Empty;

        public int ExitCode { get; set; }

        public Dictionary<string, object> Outputs { get; set; } = new Dictionary<string, object>();

        public string ErrorText { get; set; } = string.Empty;

        public bool TimedOut { get; set; }

        public string Reason { get; set; } = string.Empty;

        public static SpecRunOutcome Failure(string reason)
        {
            return new SpecRunOutcome { Reason = reason ?? "прогон невозможен" };
        }

        public object GetOutput(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Outputs == null)
            {
                return null;
            }

            return Outputs.TryGetValue(name, out object value) ? value : null;
        }
    }

    /// <summary>
    /// Прогонщик робота за интерфейсом (<c>TZ-08</c>, шаг 4): сейчас это движок студии
    /// (<c>WorkflowRunEngine</c>), позже за тем же контрактом встанет <c>RpaRunner.exe</c> (<c>OR-02</c>)
    /// без переписывания карточки.
    /// </summary>
    public interface IWorkflowRunService
    {
        /// <summary>Прогон доступен (движок подключён хостом).</summary>
        bool Available { get; }

        /// <summary>Почему прогон недоступен — текстом, а не исключением.</summary>
        string UnavailableReason { get; }

        /// <summary>Прогоняет процесс. Исключений не бросает.</summary>
        SpecRunOutcome Run(SpecRunRequest request);
    }

    /// <summary>Null-object: хост не подключил движок прогона.</summary>
    public sealed class NullWorkflowRunService : IWorkflowRunService
    {
        public static readonly NullWorkflowRunService Instance = new NullWorkflowRunService();

        public bool Available => false;

        public string UnavailableReason => "движок прогона не подключён хостом студии";

        public SpecRunOutcome Run(SpecRunRequest request)
        {
            return SpecRunOutcome.Failure(UnavailableReason);
        }
    }

    /// <summary>Статус пункта чек-листа. «Не запускался» — отдельное состояние, а не «пройден».</summary>
    public enum SpecCheckStatus
    {
        NotRun,
        Passed,
        Failed,

        /// <summary>Кейс не проверялся, потому что прогон недоступен/кейсов нет.</summary>
        Skipped
    }

    /// <summary>Строка чек-листа приёмки: критерий → кейс → статус.</summary>
    public sealed class SpecChecklistEntry
    {
        public string TestCaseId { get; set; } = string.Empty;

        public string CriterionId { get; set; } = string.Empty;

        public SpecCheckStatus Status { get; set; } = SpecCheckStatus.NotRun;

        public string Details { get; set; } = string.Empty;

        public string StatusName
        {
            get
            {
                switch (Status)
                {
                    case SpecCheckStatus.Passed: return "пройден";
                    case SpecCheckStatus.Failed: return "провален";
                    case SpecCheckStatus.Skipped: return "не проверялся";
                    default: return "не запускался";
                }
            }
        }
    }

    /// <summary>Отчёт о прогоне тест-кейсов: чек-лист приёмки, который пишется в <c>runs\</c>.</summary>
    public sealed class SpecRunReport
    {
        public string Slug { get; set; } = string.Empty;

        public string WorkflowPath { get; set; } = string.Empty;

        public string StartedAtUtc { get; set; } = string.Empty;

        public List<SpecChecklistEntry> Entries { get; } = new List<SpecChecklistEntry>();

        public int Passed => Entries.Count(entry => entry.Status == SpecCheckStatus.Passed);

        public int Failed => Entries.Count(entry => entry.Status == SpecCheckStatus.Failed);

        public int NotRun => Entries.Count(entry => entry.Status == SpecCheckStatus.NotRun);

        public int Skipped => Entries.Count(entry => entry.Status == SpecCheckStatus.Skipped);

        public bool AllPassed => Entries.Count > 0 && Passed == Entries.Count;

        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("чек-лист приёмки: пройдено ").Append(Passed)
                .Append(" из ").Append(Entries.Count)
                .Append(", провалено ").Append(Failed)
                .Append(", не запускалось ").Append(NotRun)
                .Append(", не проверялось ").Append(Skipped);

            foreach (var entry in Entries)
            {
                text.AppendLine().Append("- ").Append(entry.TestCaseId)
                    .Append(" (критерий ").Append(entry.CriterionId).Append("): ")
                    .Append(entry.StatusName);
                if (!string.IsNullOrWhiteSpace(entry.Details))
                {
                    text.Append(" — ").Append(entry.Details);
                }
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>JSON-отчёт для <c>docs\spec\&lt;slug&gt;\runs\&lt;stamp&gt;.json</c>.</summary>
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, RpaSpecification.JsonSettings());
        }
    }

    /// <summary>
    /// Прогон тест-кейсов ТЗ (<c>TZ-08</c>): входные аргументы → прогон робота → сравнение ожидаемых
    /// выходных аргументов → чек-лист приёмки. Падение кейса — «провален», а не ошибка инструмента.
    /// </summary>
    public static class SpecTestRunner
    {
        public static SpecRunReport Run(RpaSpecification specification, IWorkflowRunService service,
            string workflowPath, string slug, Func<DateTime> clock = null)
        {
            var report = new SpecRunReport
            {
                Slug = slug ?? string.Empty,
                WorkflowPath = workflowPath ?? string.Empty,
                StartedAtUtc = (clock ?? (() => DateTime.UtcNow))().ToString("u")
            };

            if (specification == null)
            {
                return report;
            }

            specification.Normalize();

            if (specification.TestCases.Count == 0)
            {
                return report;
            }

            if (service == null || !service.Available)
            {
                foreach (var testCase in specification.TestCases)
                {
                    report.Entries.Add(new SpecChecklistEntry
                    {
                        TestCaseId = testCase.Id,
                        CriterionId = testCase.CriterionId,
                        Status = SpecCheckStatus.NotRun,
                        Details = service?.UnavailableReason ?? "движок прогона не подключён"
                    });
                }

                return report;
            }

            if (string.IsNullOrWhiteSpace(workflowPath))
            {
                foreach (var testCase in specification.TestCases)
                {
                    report.Entries.Add(new SpecChecklistEntry
                    {
                        TestCaseId = testCase.Id,
                        CriterionId = testCase.CriterionId,
                        Status = SpecCheckStatus.NotRun,
                        Details = "путь процесса не указан"
                    });
                }

                return report;
            }

            foreach (var testCase in specification.TestCases)
            {
                var request = new SpecRunRequest
                {
                    WorkflowPath = workflowPath,
                    Inputs = new Dictionary<string, string>(testCase.Inputs ?? new Dictionary<string, string>())
                };

                SpecRunOutcome outcome;
                try
                {
                    outcome = service.Run(request) ?? SpecRunOutcome.Failure("прогонщик вернул пустой результат");
                }
                catch (Exception ex)
                {
                    outcome = SpecRunOutcome.Failure("прогон упал: " + ex.Message);
                }

                report.Entries.Add(Evaluate(testCase, outcome));
            }

            return report;
        }

        /// <summary>Оценка одного кейса: негативный ждёт отказа, позитивный — совпадения выходных аргументов.</summary>
        public static SpecChecklistEntry Evaluate(SpecTestCase testCase, SpecRunOutcome outcome)
        {
            var entry = new SpecChecklistEntry
            {
                TestCaseId = testCase?.Id ?? string.Empty,
                CriterionId = testCase?.CriterionId ?? string.Empty
            };

            if (outcome == null)
            {
                entry.Status = SpecCheckStatus.Failed;
                entry.Details = "прогон не вернул результат";
                return entry;
            }

            if (!string.IsNullOrWhiteSpace(outcome.Reason))
            {
                entry.Status = SpecCheckStatus.NotRun;
                entry.Details = outcome.Reason;
                return entry;
            }

            if (testCase != null && testCase.Negative)
            {
                bool rejected = outcome.ExitCode != 0 || !outcome.Ok;
                entry.Status = rejected ? SpecCheckStatus.Passed : SpecCheckStatus.Failed;
                entry.Details = rejected
                    ? "ожидаемый отказ получен: " + Describe(outcome)
                    : "ожидался отказ, но робот завершился успешно";
                return entry;
            }

            if (!outcome.Ok)
            {
                entry.Status = SpecCheckStatus.Failed;
                entry.Details = "прогон не успешен: " + Describe(outcome);
                return entry;
            }

            var mismatches = new List<string>();
            foreach (var expected in testCase?.ExpectedOutputs ?? new Dictionary<string, string>())
            {
                object actual = outcome.GetOutput(expected.Key);
                string actualText = actual?.ToString() ?? string.Empty;
                if (!string.Equals(actualText.Trim(), (expected.Value ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add(expected.Key + ": ожидалось «" + expected.Value + "», получено «" + actualText + "»");
                }
            }

            if (mismatches.Count > 0)
            {
                entry.Status = SpecCheckStatus.Failed;
                entry.Details = string.Join("; ", mismatches);
                return entry;
            }

            entry.Status = SpecCheckStatus.Passed;
            entry.Details = "выходные аргументы совпали";
            return entry;
        }

        private static string Describe(SpecRunOutcome outcome)
        {
            var text = new StringBuilder();
            text.Append("состояние: ").Append(string.IsNullOrWhiteSpace(outcome.State) ? "?" : outcome.State)
                .Append(", код возврата: ").Append(outcome.ExitCode);
            if (outcome.TimedOut)
            {
                text.Append(", таймаут");
            }

            if (!string.IsNullOrWhiteSpace(outcome.ErrorText))
            {
                text.AppendLine().Append(outcome.ErrorText);
            }

            return text.ToString();
        }
    }
}
