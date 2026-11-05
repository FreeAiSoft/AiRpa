using System;
using System.Collections.Generic;
using System.Text;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Что изменилось в валидации процесса после правки (карточка <c>AG-09</c>).
    /// <para>
    /// Агенту нужны только <b>новые</b> ошибки: в процессе могло быть сломано что-то до него, и если
    /// возвращать весь список, агент начнёт править чужие ошибки вместо своей правки. Поэтому сравнение
    /// идёт по тексту замечаний, а не по адресам: вставка сдвигает индексы соседей, и адрес
    /// <c>root/Activities[2]</c> после правки может означать уже другой элемент.
    /// </para>
    /// </summary>
    public sealed class WorkflowValidationDelta
    {
        private static readonly WorkflowOutlineIssue[] NoIssues = new WorkflowOutlineIssue[0];

        private readonly List<WorkflowOutlineIssue> _newErrors = new List<WorkflowOutlineIssue>();
        private readonly List<WorkflowOutlineIssue> _fixedErrors = new List<WorkflowOutlineIssue>();

        private WorkflowValidationDelta(bool available, int baselineErrors, int errors)
        {
            Available = available;
            BaselineErrors = baselineErrors;
            Errors = errors;
        }

        /// <summary>Валидация недоступна: структура процесса не прочитана до или после правки.</summary>
        public static WorkflowValidationDelta Unavailable { get; } = new WorkflowValidationDelta(false, 0, 0);

        /// <summary>Сравнение удалось: до и после правки структура прочитана.</summary>
        public bool Available { get; }

        /// <summary>Ошибок валидации было до правки.</summary>
        public int BaselineErrors { get; }

        /// <summary>Ошибок валидации стало после правки.</summary>
        public int Errors { get; }

        /// <summary>Ошибки, которых до правки не было; адреса — из структуры после правки.</summary>
        public IReadOnlyList<WorkflowOutlineIssue> NewErrors => _newErrors;

        /// <summary>Ошибки, которые правка устранила.</summary>
        public IReadOnlyList<WorkflowOutlineIssue> FixedErrors => _fixedErrors;

        public bool HasNewErrors => _newErrors.Count > 0;

        public int FixedCount => _fixedErrors.Count;

        /// <summary>
        /// Сравнивает ошибки валидации до и после правки. <c>null</c> в любом аргументе — валидация
        /// недоступна (нечего сравнивать), а не «ошибок нет».
        /// </summary>
        public static WorkflowValidationDelta Compare(WorkflowOutline baseline, WorkflowOutline after)
        {
            if (baseline == null || after == null)
            {
                return Unavailable;
            }

            var delta = new WorkflowValidationDelta(true, CountErrors(baseline), CountErrors(after));

            // Мультимножество текстов: одинаковые ошибки у разных узлов не должны «закрывать» друг друга.
            Dictionary<string, int> baselineCounts = CountsOf(baseline);
            Dictionary<string, int> afterCounts = CountsOf(after);

            foreach (WorkflowOutlineIssue issue in after.Errors)
            {
                string key = Key(issue);

                if (baselineCounts.TryGetValue(key, out int count) && count > 0)
                {
                    baselineCounts[key] = count - 1;
                    continue;
                }

                delta._newErrors.Add(issue);
            }

            foreach (WorkflowOutlineIssue issue in baseline.Errors)
            {
                string key = Key(issue);

                if (afterCounts.TryGetValue(key, out int count) && count > 0)
                {
                    afterCounts[key] = count - 1;
                    continue;
                }

                delta._fixedErrors.Add(issue);
            }

            return delta;
        }

        /// <summary>Строка для ответа инструмента: сколько ошибок было, стало и сколько из них новых.</summary>
        public string Summary
        {
            get
            {
                if (!Available)
                {
                    return "валидация: недоступна (структура процесса не прочитана)";
                }

                var text = new StringBuilder();
                text.Append("валидация: ошибок ").Append(Errors);

                if (Errors != BaselineErrors)
                {
                    text.Append(" (до правки ").Append(BaselineErrors).Append(")");
                }

                text.Append("; новых ").Append(_newErrors.Count);

                if (FixedCount > 0)
                {
                    text.Append(", исправлено ").Append(FixedCount);
                }

                return text.ToString();
            }
        }

        public override string ToString()
        {
            return Summary;
        }

        private static int CountErrors(WorkflowOutline outline)
        {
            int count = 0;
            foreach (WorkflowOutlineIssue issue in outline.Errors)
            {
                count++;
            }

            return count;
        }

        private static Dictionary<string, int> CountsOf(WorkflowOutline outline)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (WorkflowOutlineIssue issue in outline.Errors)
            {
                string key = Key(issue);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
            }

            return counts;
        }

        /// <summary>Ключ сравнения: текст замечания без лишних пробелов; адрес намеренно не входит.</summary>
        private static string Key(WorkflowOutlineIssue issue)
        {
            return Normalize(issue?.Message);
        }

        private static string Normalize(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            var text = new StringBuilder(message.Length);
            bool space = false;

            foreach (char symbol in message.Trim())
            {
                if (char.IsWhiteSpace(symbol))
                {
                    space = true;
                    continue;
                }

                if (space && text.Length > 0)
                {
                    text.Append(' ');
                }

                space = false;
                text.Append(symbol);
            }

            return text.ToString();
        }
    }
}
