using System;
using System.Collections.Generic;
using System.Text;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Текстовый ответ инструмента <c>workflow_validate</c> (карточка <c>AG-06</c>).
    /// <para>
    /// Формат держится отдельно от отчёта, как и у <see cref="WorkflowOutlineText"/>: его проверяют тесты,
    /// а отчёт можно расширять, не переписывая ожидаемый текст. Три состояния печатаются по-разному —
    /// «ошибок нет», «ошибки есть» и «валидация не выполнена», — иначе агент принимает отсутствие данных
    /// за успешную проверку и уходит собирать заведомо невалидного робота.
    /// </para>
    /// </summary>
    public static class WorkflowValidationText
    {
        private const string Hint =
            "дальше: правьте по этим адресам (activity_insert, activity_delete, activity_move, activity_wrap, " +
            "argument_set, variable_add) и повторяйте workflow_validate, пока не останется «ошибок нет».";

        /// <summary>Ответ по отчёту валидации: вердикт, источник, замечания с адресами и подсказка.</summary>
        public static string Describe(WorkflowValidationReport report)
        {
            if (report == null)
            {
                return string.Empty;
            }

            if (report.AddressNotFound)
            {
                return DescribeAddressNotFound(report);
            }

            if (!report.Available)
            {
                return DescribeUnavailable(report);
            }

            return DescribeValidation(report);
        }

        private static string DescribeAddressNotFound(WorkflowValidationReport report)
        {
            var text = new StringBuilder();

            text.AppendLine($"валидация: адрес не найден — {report.ElementId}; источник: {report.SourceName}");
            AppendProcess(text, report);
            text.AppendLine();

            if (report.KnownAddresses.Count > 0)
            {
                text.AppendLine("известные адреса: " + string.Join(", ", report.KnownAddresses));
            }

            text.AppendLine("что это значит: элемента с таким адресом в процессе нет, поэтому и замечаний по нему нет — " +
                "вердикта «ошибок нет» здесь не выдаётся.");
            text.Append("подсказка: адрес берётся из workflow_outline (root, root/Activities[0], root/Then) — " +
                "прочитайте структуру и повторите workflow_validate.");

            return text.ToString().TrimEnd();
        }

        private static string DescribeUnavailable(WorkflowValidationReport report)
        {
            var text = new StringBuilder();

            text.AppendLine($"валидация: не выполнена; источник: {report.SourceName}");
            AppendProcess(text, report);
            text.AppendLine("причина: " + report.UnavailableReason);
            text.AppendLine("что это значит: «не выполнена» ≠ «ошибок нет» — процесс не проверен, опираться на этот ответ нельзя.");

            if (report.LoadIssues.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("замечания чтения:");
                AppendIssues(text, report.LoadIssues);
            }

            text.Append("подсказка: устраните причину (файл, путь, ошибка загрузки XAML) и повторите workflow_validate.");

            return text.ToString().TrimEnd();
        }

        private static string DescribeValidation(WorkflowValidationReport report)
        {
            var text = new StringBuilder();

            text.AppendLine(
                $"валидация: ошибок {report.Errors}, предупреждений {report.Warnings}; источник: {report.SourceName}");
            AppendProcess(text, report);

            if (!string.IsNullOrWhiteSpace(report.ScopeId))
            {
                text.AppendLine($"область: {report.ScopeId} (элемент и вложенные)");
            }

            text.AppendLine($"активностей проверено: {report.ActivityCount}");

            if (report.Errors == 0)
            {
                text.AppendLine();
                text.AppendLine(report.Warnings == 0
                    ? "ошибок нет: валидация WF выполнена, править нечего."
                    : $"ошибок нет: валидация WF выполнена, есть только предупреждения ({report.Warnings}).");
            }

            if (report.Issues.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("замечания:");
                AppendIssues(text, report.Issues);
            }

            if (report.HiddenIssues > 0)
            {
                text.AppendLine(
                    $"  … ещё {report.HiddenIssues} замечаний не показаны (лимит max_issues), всего {report.TotalIssues}");
            }

            text.AppendLine();
            text.Append(Hint);

            return text.ToString().TrimEnd();
        }

        private static void AppendProcess(StringBuilder text, WorkflowValidationReport report)
        {
            if (!string.IsNullOrWhiteSpace(report.FilePath))
            {
                text.AppendLine("процесс: " + report.FilePath);
            }
        }

        private static void AppendIssues(StringBuilder text, IReadOnlyList<WorkflowOutlineIssue> issues)
        {
            foreach (WorkflowOutlineIssue issue in issues)
            {
                text.Append("  - ").AppendLine(DescribeIssue(issue));
            }
        }

        /// <summary>
        /// Одно замечание строкой: «ошибка root/Activities[0]: текст (код: wf.validate, свойство: Text)».
        /// Адрес, свойство и код печатаются всегда — без них агент не знает, что именно править.
        /// </summary>
        public static string DescribeIssue(WorkflowOutlineIssue issue)
        {
            if (issue == null)
            {
                return string.Empty;
            }

            var text = new StringBuilder();

            text.Append(issue.SeverityName);

            if (!string.IsNullOrEmpty(issue.ElementId))
            {
                text.Append(' ').Append(issue.ElementId);
            }

            text.Append(": ").Append(issue.Message);

            var details = new List<string>();

            if (!string.IsNullOrWhiteSpace(issue.Code))
            {
                details.Add("код: " + issue.Code);
            }

            if (!string.IsNullOrWhiteSpace(issue.PropertyName))
            {
                details.Add("свойство: " + issue.PropertyName);
            }

            if (details.Count > 0)
            {
                text.Append(" (").Append(string.Join(", ", details)).Append(')');
            }

            return text.ToString();
        }
    }
}
