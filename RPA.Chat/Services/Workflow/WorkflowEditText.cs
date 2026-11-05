using System.Collections.Generic;
using System.Text;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Текстовый ответ инструментов правки открытого процесса (карточка <c>AG-02</c>).
    /// <para>
    /// Формат живёт отдельно от реализации, как и у <see cref="WorkflowOutlineText"/>: его проверяют тесты,
    /// а инструменты <c>AG-04</c> используют тот же текст. Ответ отвечает на три вопроса агента: что
    /// применилось, по какому адресу править дальше и можно ли отменить.
    /// </para>
    /// </summary>
    public static class WorkflowEditText
    {
        /// <summary>Предел числа замечаний в ответе: остальные считаются, но не печатаются.</summary>
        public const int MaxIssues = 40;

        /// <summary>Ответ по одной правке: итог, адреса, состояние истории, замечания и (по запросу) структура.</summary>
        public static string Describe(WorkflowEditResult result, bool includeOutline = true)
        {
            if (result == null)
            {
                return string.Empty;
            }

            var text = new StringBuilder();

            text.Append(result.Ok ? "Правка (" : "Правка не выполнена (")
                .Append(result.Operation)
                .Append(result.Ok ? "): применена" : "): отклонена");

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                text.Append(" — ").Append(result.Message);
            }

            text.AppendLine();
            text.Append(DescribeTargets(result)).AppendLine();
            text.Append(DescribeHistory(result)).AppendLine();
            text.Append("сводка: ").Append(DescribeSummary(result)).AppendLine();

            AppendIssues(text, result.Issues);

            if (includeOutline && result.Outline != null)
            {
                text.AppendLine();
                text.Append(WorkflowOutlineText.Describe(result.Outline));
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>Строка «элемент / новый элемент» — адреса, по которым агент продолжает работу.</summary>
        public static string DescribeTargets(WorkflowEditResult result)
        {
            var text = new StringBuilder();
            text.Append("элемент: ").Append(string.IsNullOrWhiteSpace(result.ElementId) ? "(не задан)" : result.ElementId);

            if (!string.IsNullOrWhiteSpace(result.NewElementId))
            {
                text.Append("; появился: ").Append(result.NewElementId);
            }

            if (result.Addresses.Count > 1)
            {
                // Пачка: агенту нужны все адреса сразу, иначе он читает структуру лишний раз.
                text.Append("; адреса: ").Append(string.Join(", ", result.Addresses));
            }

            return text.ToString();
        }

        /// <summary>
        /// Короткая сводка «что сделано и что стало с валидацией» (карточка <c>AG-09</c>): агент видит
        /// число правок и только новые ошибки, а не весь список проверок процесса.
        /// </summary>
        public static string DescribeSummary(WorkflowEditResult result)
        {
            var text = new StringBuilder();

            if (result.AppliedCount > 0)
            {
                text.Append("правок: ").Append(result.AppliedCount).Append("; ");
            }

            text.Append(result.Validation != null ? result.Validation.Summary : "валидация: не проверялась");

            return text.ToString();
        }

        /// <summary>Строка состояния: открыта ли пачка и что умеет история конструктора.</summary>
        public static string DescribeHistory(WorkflowEditResult result)
        {
            if (!result.SessionActive && !result.CanUndo && !result.CanRedo)
            {
                return "история: недоступна";
            }

            var text = new StringBuilder();
            text.Append(result.SessionActive
                ? "пачка открыта (закройте complete, чтобы получить одну запись в истории отмены)"
                : "пачка закрыта");
            text.Append("; отмена: ").Append(result.CanUndo ? "есть" : "нет");
            text.Append("; возврат: ").Append(result.CanRedo ? "есть" : "нет");

            return text.ToString();
        }

        private static void AppendIssues(StringBuilder text, IReadOnlyList<WorkflowOutlineIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return;
            }

            text.AppendLine("Замечания:");

            int printed = 0;
            foreach (WorkflowOutlineIssue issue in issues)
            {
                if (printed >= MaxIssues)
                {
                    text.AppendLine($"  … ещё {issues.Count - printed} замечаний (не показаны)");
                    break;
                }

                text.Append("  - ").AppendLine(issue.ToString());
                printed++;
            }
        }
    }
}
