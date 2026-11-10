using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RPA.Chat.Services.Spec
{
    /// <summary>
    /// Текстовые представления ТЗ: производный <c>specification.md</c> и ответы инструментов
    /// (<c>TZ-03</c>, <c>TZ-05</c>, <c>TZ-06</c>, <c>TZ-08</c>).
    /// <para>
    /// Markdown — не источник правды: он перегенерируется из JSON при каждом сохранении, поэтому
    /// расхождение JSON↔md — это ошибка, а не второй формат для правки.
    /// </para>
    /// </summary>
    public static class SpecificationText
    {
        /// <summary>Предел текста в ответе инструмента по умолчанию.</summary>
        public const int DefaultShowChars = 20000;

        /// <summary>Производный markdown спецификации (стабильный порядок разделов).</summary>
        public static string RenderMarkdown(RpaSpecification spec)
        {
            if (spec == null)
            {
                return string.Empty;
            }

            spec.Normalize();
            var text = new StringBuilder();

            text.Append("# ").AppendLine(string.IsNullOrWhiteSpace(spec.Title) ? "ТЗ" : spec.Title).AppendLine();

            text.Append("> слаг: ").Append(spec.Slug)
                .Append(" · версия схемы: ").Append(spec.SchemaVersion);
            if (spec.SourceRef != null && spec.SourceRef.Version > 0)
            {
                text.Append(" · исходник: версия ").Append(spec.SourceRef.Version);
                if (!string.IsNullOrWhiteSpace(spec.SourceRef.Sha256))
                {
                    text.Append(", sha ").Append(ShortSha(spec.SourceRef.Sha256));
                }
            }

            text.AppendLine().AppendLine();

            text.AppendLine("## Цель").AppendLine();
            text.AppendLine(string.IsNullOrWhiteSpace(spec.Goal) ? "_(не заполнена)_" : spec.Goal).AppendLine();

            text.AppendLine("## Предусловия").AppendLine();
            if (spec.Preconditions.Count == 0)
            {
                text.AppendLine("_(нет)_");
            }
            else
            {
                foreach (string item in spec.Preconditions)
                {
                    text.Append("- ").AppendLine(item);
                }
            }

            text.AppendLine();

            text.AppendLine("## Шаги").AppendLine();
            if (spec.Steps.Count == 0)
            {
                text.AppendLine("_(нет)_");
            }
            else
            {
                foreach (var step in spec.Steps)
                {
                    text.Append("### ").Append(step.Id).Append(". ").AppendLine(step.Text);
                    text.Append("- ожидаемый результат: ")
                        .AppendLine(string.IsNullOrWhiteSpace(step.Expected) ? "_(не задан)_" : step.Expected);
                    if (step.DataRefs.Count > 0)
                    {
                        text.Append("- данные: ").AppendLine(string.Join(", ", step.DataRefs));
                    }

                    if (step.Branches.Count > 0)
                    {
                        text.AppendLine("- ветвления:");
                        foreach (string branch in step.Branches)
                        {
                            text.Append("  - ").AppendLine(branch);
                        }
                    }

                    text.AppendLine();
                }
            }

            text.AppendLine("## Данные").AppendLine();
            if (spec.Data.Count == 0)
            {
                text.AppendLine("_(нет)_").AppendLine();
            }
            else
            {
                text.AppendLine("| ID | Имя | Тип | Источник | Пример |");
                text.AppendLine("| --- | --- | --- | --- | --- |");
                foreach (var item in spec.Data)
                {
                    text.Append("| ").Append(Cell(item.Id)).Append(" | ").Append(Cell(item.Name))
                        .Append(" | ").Append(Cell(item.Type)).Append(" | ").Append(Cell(item.Source))
                        .Append(" | ").Append(Cell(item.Example)).AppendLine(" |");
                }

                text.AppendLine();
            }

            text.AppendLine("## Исключения").AppendLine();
            if (spec.Exceptions.Count == 0)
            {
                text.AppendLine("_(нет)_").AppendLine();
            }
            else
            {
                text.AppendLine("| Ситуация | Реакция |");
                text.AppendLine("| --- | --- |");
                foreach (var item in spec.Exceptions)
                {
                    text.Append("| ").Append(Cell(item.Situation)).Append(" | ").Append(Cell(item.Reaction))
                        .AppendLine(" |");
                }

                text.AppendLine();
            }

            text.AppendLine("## Критерии приёмки").AppendLine();
            if (spec.AcceptanceCriteria.Count == 0)
            {
                text.AppendLine("_(нет)_").AppendLine();
            }
            else
            {
                text.AppendLine("| ID | Критерий | Проверка |");
                text.AppendLine("| --- | --- | --- |");
                foreach (var criterion in spec.AcceptanceCriteria)
                {
                    text.Append("| ").Append(Cell(criterion.Id)).Append(" | ").Append(Cell(criterion.Text))
                        .Append(" | ").Append(Cell(criterion.Check)).AppendLine(" |");
                }

                text.AppendLine();
            }

            text.AppendLine("## Неоднозначности").AppendLine();
            if (spec.Ambiguities.Count == 0)
            {
                text.AppendLine("_(нет)_").AppendLine();
            }
            else
            {
                foreach (var ambiguity in spec.Ambiguities)
                {
                    text.Append("- **").Append(ambiguity.Id).Append("** [").Append(StatusName(ambiguity.Status))
                        .Append("] ").Append(ambiguity.Question);
                    if (ambiguity.Options.Count > 0)
                    {
                        text.Append(" Варианты: ").Append(string.Join(" / ", ambiguity.Options)).Append('.');
                    }

                    if (!string.IsNullOrWhiteSpace(ambiguity.Answer))
                    {
                        text.Append(" Ответ: ").Append(ambiguity.Answer).Append('.');
                    }

                    text.AppendLine();
                }

                text.AppendLine();
            }

            text.AppendLine("## Задачи").AppendLine();
            if (spec.Tasks.Count == 0)
            {
                text.AppendLine("_(нет)_").AppendLine();
            }
            else
            {
                foreach (var task in spec.Tasks)
                {
                    text.Append("- **").Append(task.Id).Append("** ").Append(task.Title)
                        .Append(" · статус: ").Append(TaskStatusName(task.Status))
                        .Append(" · пункты: ").Append(task.SpecRefs.Count == 0 ? "—" : string.Join(", ", task.SpecRefs));
                    if (task.Steps.Count > 0)
                    {
                        text.Append(" · шаги: ")
                            .Append(string.Join(", ", task.Steps.Select(step => step.ActivityType)));
                    }

                    text.AppendLine();
                }

                text.AppendLine();
            }

            text.AppendLine("## Тест-кейсы").AppendLine();
            if (spec.TestCases.Count == 0)
            {
                text.AppendLine("_(нет)_").AppendLine();
            }
            else
            {
                foreach (var testCase in spec.TestCases)
                {
                    text.Append("- **").Append(testCase.Id).Append("** критерий ").Append(testCase.CriterionId)
                        .Append(testCase.Negative ? " · негативный" : string.Empty);
                    if (testCase.Inputs.Count > 0)
                    {
                        text.Append(" · вход: ").Append(FormatPairs(testCase.Inputs));
                    }

                    if (testCase.ExpectedOutputs.Count > 0)
                    {
                        text.Append(" · ожидание: ").Append(FormatPairs(testCase.ExpectedOutputs));
                    }

                    text.AppendLine();
                }

                text.AppendLine();
            }

            text.AppendLine("## Трассировка").AppendLine();
            if (spec.Links.Count == 0)
            {
                text.AppendLine("_(нет)_");
            }
            else
            {
                foreach (var link in spec.Links)
                {
                    text.Append("- ").Append(link.ItemId).Append(" → ").Append(link.ElementId);
                    if (!string.IsNullOrWhiteSpace(link.Note))
                    {
                        text.Append(" (").Append(link.Note).Append(')');
                    }

                    text.AppendLine();
                }
            }

            return text.ToString().TrimEnd() + Environment.NewLine;
        }

        /// <summary>
        /// <c>spec_show</c>: раздел модели (или весь текст ТЗ), текст ограничен по символам с явной пометкой.
        /// </summary>
        public static string Show(RpaSpecification spec, string section, int maxChars = DefaultShowChars)
        {
            if (spec == null)
            {
                return "модель ТЗ не собрана";
            }

            spec.Normalize();
            string name = (section ?? string.Empty).Trim().ToLowerInvariant();
            string body;

            switch (name)
            {
                case "":
                case "all":
                case "model":
                    body = RenderMarkdown(spec);
                    break;
                case "goal":
                case "цель":
                    body = "цель: " + (string.IsNullOrWhiteSpace(spec.Goal) ? "(не заполнена)" : spec.Goal);
                    break;
                case "steps":
                case "шаги":
                    body = ShowSteps(spec);
                    break;
                case "data":
                case "данные":
                    body = ShowData(spec);
                    break;
                case "exceptions":
                case "исключения":
                    body = ShowExceptions(spec);
                    break;
                case "criteria":
                case "acceptance":
                case "критерии":
                    body = ShowCriteria(spec);
                    break;
                case "ambiguities":
                case "вопросы":
                    body = ShowAmbiguities(spec);
                    break;
                case "tasks":
                case "задачи":
                    body = ShowTasks(spec);
                    break;
                case "testcases":
                case "tests":
                case "тесты":
                    body = ShowTestCases(spec);
                    break;
                case "links":
                case "trace":
                case "трассировка":
                    body = ShowLinks(spec);
                    break;
                default:
                    body = "неизвестный раздел «" + section + "». Доступно: all, goal, steps, data, exceptions, " +
                           "criteria, ambiguities, tasks, testcases, links";
                    break;
            }

            return Truncate(body, maxChars);
        }

        /// <summary><c>spec_show</c> без модели: показывает текст текущей версии ТЗ с разделом-срезом.</summary>
        public static string ShowText(string slug, string text, string section, int maxChars = DefaultShowChars)
        {
            string body = text ?? string.Empty;
            string name = (section ?? string.Empty).Trim();
            if (name.Length > 0 && !string.Equals(name, "all", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "text", StringComparison.OrdinalIgnoreCase))
            {
                string extracted = ExtractSection(body, name);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    body = extracted;
                }
            }

            var result = new StringBuilder();
            result.Append("ТЗ «").Append(slug).Append("», модель не собрана (specification.json отсутствует).").AppendLine();
            result.Append("Текст текущей версии:").AppendLine().AppendLine();
            result.Append(body);

            return Truncate(result.ToString(), maxChars);
        }

        /// <summary>Извлекает раздел markdown по заголовку (регистронезависимо, по подстроке).</summary>
        public static string ExtractSection(string text, string section)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(section))
            {
                return null;
            }

            string[] lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            int start = -1;
            int level = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (!line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int currentLevel = line.Length - line.TrimStart('#').Length;
                string title = line.TrimStart('#').Trim();
                if (title.IndexOf(section.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    start = i + 1;
                    level = currentLevel;
                    break;
                }
            }

            if (start < 0)
            {
                return null;
            }

            var sectionText = new StringBuilder();
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    int currentLevel = trimmed.Length - trimmed.TrimStart('#').Length;
                    if (currentLevel <= level)
                    {
                        break;
                    }
                }

                sectionText.AppendLine(line);
            }

            return sectionText.ToString().Trim();
        }

        /// <summary>Ограничение текста с явной пометкой (никогда не «тихо» обрезаем).</summary>
        public static string Truncate(string text, int maxChars)
        {
            string value = text ?? string.Empty;
            if (maxChars <= 0 || value.Length <= maxChars)
            {
                return value;
            }

            return SafeText.Truncate(value, maxChars) + Environment.NewLine +
                   "… (текст сокращён до " + maxChars + " символов: читайте раздел через spec_show(section))";
        }

        // ------------------------------------------------------------------

        private static string ShowSteps(RpaSpecification spec)
        {
            if (spec.Steps.Count == 0)
            {
                return "шагов нет";
            }

            var text = new StringBuilder();
            foreach (var step in spec.Steps)
            {
                text.Append(step.Id).Append(". ").AppendLine(step.Text);
                text.Append("   ожидание: ")
                    .AppendLine(string.IsNullOrWhiteSpace(step.Expected) ? "(не задан)" : step.Expected);
                if (step.DataRefs.Count > 0)
                {
                    text.Append("   данные: ").AppendLine(string.Join(", ", step.DataRefs));
                }

                foreach (string branch in step.Branches)
                {
                    text.Append("   ветвление: ").AppendLine(branch);
                }
            }

            return text.ToString().TrimEnd();
        }

        private static string ShowData(RpaSpecification spec)
        {
            if (spec.Data.Count == 0)
            {
                return "данных нет";
            }

            return string.Join(Environment.NewLine, spec.Data.Select(item =>
                item.Id + ": " + item.Name + " (" + item.Type + ")" +
                (string.IsNullOrWhiteSpace(item.Source) ? string.Empty : " ← " + item.Source) +
                (string.IsNullOrWhiteSpace(item.Example) ? string.Empty : " · пример: " + item.Example)));
        }

        private static string ShowExceptions(RpaSpecification spec)
        {
            if (spec.Exceptions.Count == 0)
            {
                return "исключений нет";
            }

            return string.Join(Environment.NewLine, spec.Exceptions.Select(item =>
                item.Situation + " → " + item.Reaction));
        }

        private static string ShowCriteria(RpaSpecification spec)
        {
            if (spec.AcceptanceCriteria.Count == 0)
            {
                return "критериев нет";
            }

            return string.Join(Environment.NewLine, spec.AcceptanceCriteria.Select(item =>
                item.Id + ": " + item.Text +
                (string.IsNullOrWhiteSpace(item.Check) ? " (проверка не задана)" : " · проверка: " + item.Check)));
        }

        private static string ShowAmbiguities(RpaSpecification spec)
        {
            if (spec.Ambiguities.Count == 0)
            {
                return "неоднозначностей нет";
            }

            return string.Join(Environment.NewLine, spec.Ambiguities.Select(item =>
                item.Id + " [" + StatusName(item.Status) + "]" +
                (string.IsNullOrWhiteSpace(item.ItemId) ? string.Empty : " (" + item.ItemId + ")") +
                ": " + item.Question +
                (item.Options.Count == 0 ? string.Empty : " · варианты: " + string.Join(" / ", item.Options)) +
                (string.IsNullOrWhiteSpace(item.Answer) ? string.Empty : " · ответ: " + item.Answer)));
        }

        private static string ShowTasks(RpaSpecification spec)
        {
            if (spec.Tasks.Count == 0)
            {
                return "задач нет (spec_decompose)";
            }

            var text = new StringBuilder();
            foreach (var task in spec.Tasks)
            {
                text.Append(task.Id).Append(" [").Append(TaskStatusName(task.Status)).Append("] ").AppendLine(task.Title);
                text.Append("   пункты ТЗ: ")
                    .AppendLine(task.SpecRefs.Count == 0 ? "(нет)" : string.Join(", ", task.SpecRefs));
                foreach (var step in task.Steps)
                {
                    text.Append("   шаг: ").Append(step.ActivityType).Append(" → ").Append(step.Parent);
                    if (!string.IsNullOrWhiteSpace(step.Container))
                    {
                        text.Append(".").Append(step.Container);
                    }

                    if (!string.IsNullOrWhiteSpace(step.ArgumentName))
                    {
                        text.Append(" · ").Append(step.ArgumentName).Append(" = ").Append(step.ArgumentValue);
                    }

                    text.AppendLine();
                }

                if (task.ElementIds.Count > 0)
                {
                    text.Append("   элементы: ").AppendLine(string.Join(", ", task.ElementIds));
                }
            }

            return text.ToString().TrimEnd();
        }

        private static string ShowTestCases(RpaSpecification spec)
        {
            if (spec.TestCases.Count == 0)
            {
                return "тест-кейсов нет";
            }

            return string.Join(Environment.NewLine, spec.TestCases.Select(testCase =>
                testCase.Id + " · критерий " + testCase.CriterionId + (testCase.Negative ? " · негативный" : string.Empty) +
                (testCase.Inputs.Count == 0 ? string.Empty : " · вход: " + FormatPairs(testCase.Inputs)) +
                (testCase.ExpectedOutputs.Count == 0
                    ? string.Empty
                    : " · ожидание: " + FormatPairs(testCase.ExpectedOutputs))));
        }

        private static string ShowLinks(RpaSpecification spec)
        {
            if (spec.Links.Count == 0)
            {
                return "меток нет";
            }

            return string.Join(Environment.NewLine, spec.Links.Select(link =>
                link.ItemId + " → " + link.ElementId +
                (string.IsNullOrWhiteSpace(link.Note) ? string.Empty : " (" + link.Note + ")")));
        }

        internal static string FormatPairs(IDictionary<string, string> pairs)
        {
            if (pairs == null || pairs.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", pairs.Select(pair => pair.Key + "=" + pair.Value));
        }

        internal static string TaskStatusName(SpecTaskStatus status)
        {
            switch (status)
            {
                case SpecTaskStatus.InProgress: return "в работе";
                case SpecTaskStatus.Done: return "сделана";
                case SpecTaskStatus.Rejected: return "отклонена";
                default: return "не начата";
            }
        }

        internal static string StatusName(SpecAmbiguityStatus status)
        {
            switch (status)
            {
                case SpecAmbiguityStatus.Resolved: return "решён";
                case SpecAmbiguityStatus.Deferred: return "отложен";
                default: return "открыт";
            }
        }

        private static string Cell(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string ShortSha(string sha)
        {
            return string.IsNullOrWhiteSpace(sha) ? "?" : (sha.Length > 8 ? sha.Substring(0, 8) : sha);
        }
    }
}
