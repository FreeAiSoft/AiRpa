using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RPA.Chat.Services.Spec
{
    /// <summary>Три состояния проверки ТЗ — как у отчёта валидации workflow (<c>WorkflowValidationText</c>).</summary>
    public enum SpecValidationState
    {
        /// <summary>Замечаний нет: можно генерировать.</summary>
        Ready,

        /// <summary>Есть замечания с адресами пунктов.</summary>
        HasIssues,

        /// <summary>Модель не собрана (нет спецификации / не прочитана) — это не «ошибок нет».</summary>
        NotBuilt
    }

    /// <summary>Одно замечание проверки: адрес пункта ТЗ и текст.</summary>
    public sealed class SpecIssue
    {
        public SpecIssue(string address, string message, bool blocking = false)
        {
            Address = address ?? string.Empty;
            Message = message ?? string.Empty;
            Blocking = blocking;
        }

        /// <summary>Адрес: <c>goal</c>, <c>steps[1].expected</c>, <c>acceptanceCriteria[0].check</c>.</summary>
        public string Address { get; }

        public string Message { get; }

        /// <summary>Без исправления генерировать нельзя.</summary>
        public bool Blocking { get; }

        public override string ToString()
        {
            return (Blocking ? "блокирует: " : "замечание: ") + Address + " — " + Message;
        }
    }

    /// <summary>Отчёт проверки спецификации.</summary>
    public sealed class SpecValidationReport
    {
        private readonly List<SpecIssue> _issues = new List<SpecIssue>();

        public SpecValidationReport(SpecValidationState state, IEnumerable<SpecIssue> issues = null)
        {
            State = state;
            if (issues != null)
            {
                _issues.AddRange(issues);
            }
        }

        public SpecValidationState State { get; private set; }

        public IReadOnlyList<SpecIssue> Issues => _issues;

        public int IssueCount => _issues.Count;

        public int BlockingCount => _issues.Count(issue => issue.Blocking);

        public bool Ready => State == SpecValidationState.Ready;

        public void Add(string address, string message, bool blocking = false)
        {
            _issues.Add(new SpecIssue(address, message, blocking));
            State = SpecValidationState.HasIssues;
        }

        /// <summary>Текст отчёта: три состояния названы явно, у каждого замечания есть адрес.</summary>
        public string Describe()
        {
            switch (State)
            {
                case SpecValidationState.Ready:
                    return "модель ТЗ собрана, замечаний нет — готова к генерации";

                case SpecValidationState.NotBuilt:
                    return "модель ТЗ не собрана — это не «ошибок нет»: " +
                           (_issues.Count == 0
                               ? "спецификация не найдена или не прочитана"
                               : string.Join("; ", _issues.Select(issue => issue.Message)));

                default:
                    var text = new StringBuilder();
                    text.Append("замечаний: ").Append(_issues.Count)
                        .Append(", из них блокирующих: ").Append(BlockingCount);
                    foreach (var issue in _issues)
                    {
                        text.AppendLine().Append("- ").Append(issue);
                    }

                    return text.ToString();
            }
        }
    }

    /// <summary>
    /// Проверки модели ТЗ (<c>TZ-03</c>), декомпозиции (<c>TZ-05</c>), тест-кейсов (<c>TZ-08</c>)
    /// и трассировки (<c>TZ-06</c>). Все проверки возвращают адреса пунктов, а не общие слова.
    /// </summary>
    public static class SpecificationValidator
    {
        /// <summary>Проверка модели: цель, шаги, ожидаемые результаты, данные, критерии, дубли идентификаторов.</summary>
        public static SpecValidationReport Validate(RpaSpecification spec)
        {
            if (spec == null)
            {
                return new SpecValidationReport(SpecValidationState.NotBuilt);
            }

            spec.Normalize();
            var report = new SpecValidationReport(SpecValidationState.Ready);

            if (string.IsNullOrWhiteSpace(spec.Title))
            {
                report.Add("title", "у ТЗ нет названия", blocking: true);
            }

            if (string.IsNullOrWhiteSpace(spec.Goal))
            {
                report.Add("goal", "не заполнена цель ТЗ", blocking: true);
            }

            if (spec.Steps.Count == 0)
            {
                report.Add("steps", "в ТЗ нет шагов", blocking: true);
            }

            var dataIds = new HashSet<string>(
                spec.Data.Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => item.Id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < spec.Steps.Count; i++)
            {
                var step = spec.Steps[i];
                if (string.IsNullOrWhiteSpace(step.Id))
                {
                    report.Add("steps[" + i + "].id", "у шага нет идентификатора", blocking: true);
                }

                if (string.IsNullOrWhiteSpace(step.Text))
                {
                    report.Add("steps[" + i + "].text", "шаг без текста", blocking: true);
                }

                if (string.IsNullOrWhiteSpace(step.Expected))
                {
                    report.Add("steps[" + i + "].expected", "шаг без ожидаемого результата");
                }

                foreach (string reference in step.DataRefs.Where(r => !string.IsNullOrWhiteSpace(r)))
                {
                    if (!dataIds.Contains(reference.Trim()))
                    {
                        report.Add("steps[" + i + "].dataRefs", "ссылка на несуществующие данные: " + reference);
                    }
                }
            }

            for (int i = 0; i < spec.Data.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(spec.Data[i].Id))
                {
                    report.Add("data[" + i + "].id", "у данных нет идентификатора", blocking: true);
                }

                if (string.IsNullOrWhiteSpace(spec.Data[i].Name))
                {
                    report.Add("data[" + i + "].name", "у данных нет имени");
                }
            }

            for (int i = 0; i < spec.AcceptanceCriteria.Count; i++)
            {
                var criterion = spec.AcceptanceCriteria[i];
                if (string.IsNullOrWhiteSpace(criterion.Id))
                {
                    report.Add("acceptanceCriteria[" + i + "].id", "у критерия нет идентификатора", blocking: true);
                }

                if (string.IsNullOrWhiteSpace(criterion.Text))
                {
                    report.Add("acceptanceCriteria[" + i + "].text", "критерий без текста", blocking: true);
                }

                if (string.IsNullOrWhiteSpace(criterion.Check))
                {
                    report.Add("acceptanceCriteria[" + i + "].check",
                        "у критерия нет проверяемого признака — из него нельзя построить тест-кейс");
                }
            }

            AddDuplicates(report, "steps", spec.Steps.Select(step => step.Id));
            AddDuplicates(report, "data", spec.Data.Select(item => item.Id));
            AddDuplicates(report, "acceptanceCriteria", spec.AcceptanceCriteria.Select(item => item.Id));
            AddDuplicates(report, "tasks", spec.Tasks.Select(item => item.Id));
            AddDuplicates(report, "testCases", spec.TestCases.Select(item => item.Id));
            AddDuplicates(report, "ambiguities", spec.Ambiguities.Select(item => item.Id));

            var knownItems = new HashSet<string>(spec.AllItemIds(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < spec.Ambiguities.Count; i++)
            {
                var ambiguity = spec.Ambiguities[i];
                if (ambiguity.Status == SpecAmbiguityStatus.Open)
                {
                    report.Add("ambiguities[" + i + "]",
                        "открытый вопрос: " + Shorten(ambiguity.Question));
                }

                if (!string.IsNullOrWhiteSpace(ambiguity.ItemId) && !knownItems.Contains(ambiguity.ItemId.Trim()))
                {
                    report.Add("ambiguities[" + i + "].itemId",
                        "вопрос ссылается на несуществующий пункт: " + ambiguity.ItemId);
                }
            }

            for (int i = 0; i < spec.Links.Count; i++)
            {
                if (!knownItems.Contains((spec.Links[i].ItemId ?? string.Empty).Trim()))
                {
                    report.Add("links[" + i + "].itemId",
                        "метка ссылается на несуществующий пункт ТЗ: " + spec.Links[i].ItemId);
                }

                if (string.IsNullOrWhiteSpace(spec.Links[i].ElementId))
                {
                    report.Add("links[" + i + "].elementId", "у метки нет адреса элемента workflow");
                }
            }

            if (report.State == SpecValidationState.Ready && report.IssueCount > 0)
            {
                report = new SpecValidationReport(SpecValidationState.HasIssues, report.Issues);
            }

            return report;
        }

        /// <summary>
        /// Проверка декомпозиции (<c>TZ-05</c>): каждая задача ссылается на существующий пункт,
        /// каждый шаг и каждый критерий покрыт хотя бы одной задачей.
        /// </summary>
        public static SpecValidationReport ValidateDecomposition(RpaSpecification spec)
        {
            if (spec == null)
            {
                return new SpecValidationReport(SpecValidationState.NotBuilt);
            }

            spec.Normalize();
            var report = new SpecValidationReport(SpecValidationState.Ready);

            if (spec.Steps.Count == 0)
            {
                report.Add("steps", "в ТЗ нет шагов — декомпозиция невозможна", blocking: true);
            }

            if (spec.Tasks.Count == 0)
            {
                report.Add("tasks", "декомпозиции нет: ни одной задачи", blocking: true);
                return report;
            }

            var knownItems = new HashSet<string>(spec.AllItemIds(), StringComparer.OrdinalIgnoreCase);
            var coveredSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var coveredCriteria = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < spec.Tasks.Count; i++)
            {
                var task = spec.Tasks[i];
                if (string.IsNullOrWhiteSpace(task.Title))
                {
                    report.Add("tasks[" + i + "].title", "у задачи нет названия");
                }

                if (task.SpecRefs.Count == 0)
                {
                    report.Add("tasks[" + i + "].specRefs", "задача не ссылается ни на один пункт ТЗ", blocking: true);
                }

                foreach (string reference in task.SpecRefs.Where(r => !string.IsNullOrWhiteSpace(r)))
                {
                    string value = reference.Trim();
                    if (!knownItems.Contains(value))
                    {
                        report.Add("tasks[" + i + "].specRefs", "ссылка на несуществующий пункт ТЗ: " + value,
                            blocking: true);
                        continue;
                    }

                    if (spec.Steps.Any(step => Same(step.Id, value)))
                    {
                        coveredSteps.Add(value);
                    }

                    if (spec.AcceptanceCriteria.Any(criterion => Same(criterion.Id, value)))
                    {
                        coveredCriteria.Add(value);
                    }
                }

                if (task.Steps.Count == 0)
                {
                    report.Add("tasks[" + i + "].steps", "у задачи нет шагов workflow — генерировать нечего");
                }
            }

            foreach (var step in spec.Steps.Where(step => !string.IsNullOrWhiteSpace(step.Id)))
            {
                if (!coveredSteps.Contains(step.Id.Trim()))
                {
                    report.Add("steps[" + step.Id + "]",
                        "шаг не покрыт ни одной задачей", blocking: true);
                }
            }

            foreach (var criterion in spec.AcceptanceCriteria.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
            {
                if (!coveredCriteria.Contains(criterion.Id.Trim()))
                {
                    report.Add("acceptanceCriteria[" + criterion.Id + "]",
                        "критерий не проверяется ни одной задачей", blocking: true);
                }
            }

            return report;
        }

        /// <summary>Проверка тест-кейсов (<c>TZ-08</c>): у каждого критерия есть кейс, ссылки живые.</summary>
        public static SpecValidationReport ValidateTestCases(RpaSpecification spec)
        {
            if (spec == null)
            {
                return new SpecValidationReport(SpecValidationState.NotBuilt);
            }

            spec.Normalize();
            var report = new SpecValidationReport(SpecValidationState.Ready);

            if (spec.TestCases.Count == 0)
            {
                report.Add("testCases", "тест-кейсов нет", blocking: true);
            }

            var criterionIds = new HashSet<string>(
                spec.AcceptanceCriteria.Where(item => !string.IsNullOrWhiteSpace(item.Id)).Select(item => item.Id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < spec.TestCases.Count; i++)
            {
                var testCase = spec.TestCases[i];
                if (string.IsNullOrWhiteSpace(testCase.CriterionId))
                {
                    report.Add("testCases[" + i + "].criterionId", "кейс не ссылается на критерий", blocking: true);
                    continue;
                }

                if (!criterionIds.Contains(testCase.CriterionId.Trim()))
                {
                    report.Add("testCases[" + i + "].criterionId",
                        "кейс ссылается на несуществующий критерий: " + testCase.CriterionId, blocking: true);
                }

                if (!testCase.Negative && testCase.ExpectedOutputs.Count == 0)
                {
                    report.Add("testCases[" + i + "].expectedOutputs",
                        "у позитивного кейса нет ожидаемых выходных аргументов");
                }
            }

            foreach (var criterion in spec.AcceptanceCriteria.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
            {
                bool covered = spec.TestCases.Any(testCase => Same(testCase.CriterionId, criterion.Id));
                if (!covered)
                {
                    report.Add("acceptanceCriteria[" + criterion.Id + "]",
                        "критерий не проверяется ни одним тест-кейсом", blocking: true);
                }
            }

            return report;
        }

        private static void AddDuplicates(SpecValidationReport report, string collection, IEnumerable<string> ids)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()))
            {
                if (!seen.Add(id))
                {
                    report.Add(collection, "повторяющийся идентификатор: " + id, blocking: true);
                }
            }
        }

        private static bool Same(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "(без текста)";
            }

            text = text.Trim().Replace("\r", " ").Replace("\n", " ");
            return text.Length <= 80 ? text : SafeText.Truncate(text, 77) + "…";
        }
    }
}
