using System;
using System.Collections.Generic;
using System.Text;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Текстовый ответ инструмента <c>workflow_outline</c> (карточка <c>AG-03</c>).
    /// <para>
    /// Формат держится отдельно от реализации: его проверяют тесты, а дерево можно расширять
    /// (переменные, обработчики, вложенные контейнеры), не переписывая ожидаемый текст ответа.
    /// Ответ компактный: одна строка на узел, аргументы и переменные — отдельными строками,
    /// длинные выражения обрезаются (иначе outline открытого робота не влезает в контекст модели).
    /// </para>
    /// </summary>
    public static class WorkflowOutlineText
    {
        /// <summary>Предел длины выражения в ответе, символы.</summary>
        public const int MaxExpressionLength = 120;

        /// <summary>Предел числа замечаний в ответе: остальные считаются, но не печатаются.</summary>
        public const int MaxIssues = 40;

        private const string Indent = "  ";

        /// <summary>Ответ по структуре процесса: шапка, дерево, замечания, подсказка про адреса.</summary>
        public static string Describe(WorkflowOutline outline)
        {
            if (outline == null)
            {
                return string.Empty;
            }

            var text = new StringBuilder();

            if (!outline.Ok || outline.Root == null)
            {
                text.AppendLine("Структура процесса не прочитана: " + (outline.FilePath ?? string.Empty));
                AppendIssues(text, outline);

                return text.ToString().TrimEnd();
            }

            text.AppendLine("Структура процесса: " + outline.FilePath);
            text.AppendLine("x:Class: " +
                (string.IsNullOrWhiteSpace(outline.WorkflowId) ? "не задан" : outline.WorkflowId));

            int shownNodes = 0;
            foreach (WorkflowOutlineNode _ in outline.All())
            {
                shownNodes++;
            }

            text.AppendLine(
                $"активностей: {outline.ActivityCount}, показано узлов: {shownNodes}, " +
                (outline.Depth <= 0 ? "глубина: всё дерево" : $"глубина: {outline.Depth}"));

            int errors = 0;
            int warnings = 0;
            foreach (WorkflowOutlineIssue issue in outline.Issues)
            {
                if (issue.IsError)
                {
                    errors++;
                }
                else
                {
                    warnings++;
                }
            }

            text.AppendLine($"ошибки: {errors}, предупреждения: {warnings}");
            text.AppendLine();
            text.AppendLine("Дерево (id: тип «подпись»):");

            AppendNode(text, outline.Root, outline.IncludeArguments, level: 0);

            if (outline.Issues.Count > 0)
            {
                text.AppendLine();
                AppendIssues(text, outline);
            }

            text.AppendLine();
            text.Append("id — путь в дереве (root/Activities[0], root/Then). Этот же id принимают " +
                "инструменты правки: сначала прочитайте структуру, потом правьте по id.");

            return text.ToString().TrimEnd();
        }

        /// <summary>Один узел дерева с отступом по уровню вложенности.</summary>
        public static string DescribeNode(WorkflowOutlineNode node, bool includeArguments, int level)
        {
            if (node == null)
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            AppendNode(text, node, includeArguments, level);

            return text.ToString().TrimEnd();
        }

        private static void AppendNode(StringBuilder text, WorkflowOutlineNode node, bool includeArguments, int level)
        {
            string indent = Repeat(Indent, level);

            text.Append(indent).Append(node.Id).Append(": ").Append(node.ShortTypeName);

            if (!string.IsNullOrWhiteSpace(node.DisplayName))
            {
                text.Append(" «").Append(node.DisplayName).Append('»');
            }

            if (node.TypeName != node.ShortTypeName)
            {
                text.Append(" — ").Append(node.TypeName);
            }

            text.AppendLine();

            string detailIndent = indent + Indent;

            if (includeArguments && node.Arguments.Count > 0)
            {
                // Показываем только заданные аргументы: полный перечень (в том числе пустые служебные)
                // даёт activity_describe, а в структуре он занимает весь контекст модели.
                string arguments = JoinArguments(node.Arguments);

                if (!string.IsNullOrEmpty(arguments))
                {
                    text.Append(detailIndent).AppendLine("аргументы: " + arguments);
                }
            }

            if (node.Variables.Count > 0)
            {
                text.Append(detailIndent).AppendLine("переменные: " + JoinVariables(node.Variables));
            }

            if (node.IsTruncated)
            {
                text.Append(detailIndent)
                    .AppendLine($"… ещё {node.ChildCount} вложенных (глубина ограничена, увеличьте depth)");
            }

            foreach (WorkflowOutlineNode child in node.Children)
            {
                AppendNode(text, child, includeArguments, level + 1);
            }
        }

        private static void AppendIssues(StringBuilder text, WorkflowOutline outline)
        {
            if (outline.Issues.Count == 0)
            {
                return;
            }

            text.AppendLine("Ошибки и предупреждения:");

            int printed = 0;
            foreach (WorkflowOutlineIssue issue in outline.Issues)
            {
                if (printed >= MaxIssues)
                {
                    text.AppendLine($"  … ещё {outline.Issues.Count - printed} замечаний (не показаны)");
                    break;
                }

                text.Append("  - ").AppendLine(issue.ToString());
                printed++;
            }
        }

        private static string JoinArguments(IReadOnlyList<WorkflowOutlineArgument> arguments)
        {
            var text = new StringBuilder();

            var set = new List<WorkflowOutlineArgument>();
            foreach (WorkflowOutlineArgument candidate in arguments)
            {
                if (candidate.IsSet)
                {
                    set.Add(candidate);
                }
            }

            for (int index = 0; index < set.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(", ");
                }

                WorkflowOutlineArgument argument = set[index];
                text.Append(argument.Name).Append('(').Append(argument.Direction).Append(", ").Append(argument.TypeName).Append(')');

                if (argument.IsSet)
                {
                    text.Append(" = ").Append(Shorten(argument.Expression));
                }
            }

            return text.ToString();
        }

        private static string JoinVariables(IReadOnlyList<WorkflowOutlineVariable> variables)
        {
            var text = new StringBuilder();

            for (int index = 0; index < variables.Count; index++)
            {
                if (index > 0)
                {
                    text.Append(", ");
                }

                WorkflowOutlineVariable variable = variables[index];
                text.Append(variable.Name).Append('(').Append(variable.TypeName).Append(')');

                if (!string.IsNullOrWhiteSpace(variable.DefaultExpression))
                {
                    text.Append(" = ").Append(Shorten(variable.DefaultExpression));
                }
            }

            return text.ToString();
        }

        /// <summary>Обрезает длинное выражение, чтобы outline не занимал весь контекст модели.</summary>
        public static string Shorten(string expression)
        {
            if (string.IsNullOrEmpty(expression))
            {
                return string.Empty;
            }

            string single = expression.Replace("\r", " ").Replace("\n", " ").Trim();

            return single.Length <= MaxExpressionLength
                ? single
                : single.Substring(0, MaxExpressionLength - 1) + "…";
        }

        private static string Repeat(string value, int count)
        {
            if (count <= 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder(value.Length * count);
            for (int index = 0; index < count; index++)
            {
                text.Append(value);
            }

            return text.ToString();
        }
    }
}
