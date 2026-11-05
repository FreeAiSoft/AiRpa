using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RPA.Chat.Services.Workflow;

namespace RPA.Chat.Services.Spec
{
    /// <summary>Один пункт ТЗ в отчёте покрытия (<c>TZ-06</c>).</summary>
    public sealed class SpecCoverageEntry
    {
        public string ItemId { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public List<string> ElementIds { get; set; } = new List<string>();

        public override string ToString()
        {
            return ItemId + (ElementIds.Count == 0 ? string.Empty : " → " + string.Join(", ", ElementIds));
        }
    }

    /// <summary>
    /// Отчёт трассировки «пункт ТЗ ↔ элементы workflow» с тремя состояниями:
    /// «покрыто всё» / «есть непокрытые пункты (адреса)» / «трассировка не выполнена (причина)».
    /// </summary>
    public sealed class SpecCoverageReport
    {
        public bool Traced { get; set; }

        public string Reason { get; set; } = string.Empty;

        public List<SpecCoverageEntry> Covered { get; } = new List<SpecCoverageEntry>();

        public List<SpecCoverageEntry> Uncovered { get; } = new List<SpecCoverageEntry>();

        /// <summary>Битые метки: ссылка на удалённый пункт ТЗ или на удалённый элемент workflow.</summary>
        public List<string> BrokenLinks { get; } = new List<string>();

        public int Total => Covered.Count + Uncovered.Count;

        /// <summary>Текст отчёта. «Не выполнена» никогда не выглядит как «покрыто всё».</summary>
        public string Describe()
        {
            if (!Traced)
            {
                return "трассировка не выполнена: " +
                       (string.IsNullOrWhiteSpace(Reason) ? "нет открытого процесса или данных ТЗ" : Reason);
            }

            var text = new StringBuilder();
            text.Append("покрыто ").Append(Covered.Count).Append(" из ").Append(Total).Append(" пунктов ТЗ");

            if (Covered.Count > 0)
            {
                text.AppendLine().AppendLine("покрыто:");
                foreach (var entry in Covered)
                {
                    text.Append("- ").AppendLine(entry.ToString());
                }
            }

            if (Uncovered.Count == 0)
            {
                text.AppendLine().Append("непокрытых пунктов нет");
            }
            else
            {
                text.AppendLine().AppendLine("непокрытые пункты (адреса):");
                foreach (var entry in Uncovered)
                {
                    text.Append("- ").Append(entry.ItemId);
                    if (!string.IsNullOrWhiteSpace(entry.Text))
                    {
                        text.Append(": ").Append(entry.Text);
                    }

                    text.AppendLine();
                }
            }

            if (BrokenLinks.Count > 0)
            {
                text.AppendLine().AppendLine("битые метки:");
                foreach (string link in BrokenLinks)
                {
                    text.Append("- ").AppendLine(link);
                }
            }

            return text.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Трассировка «пункт ТЗ ↔ элементы workflow» (<c>TZ-06</c>). Метка хранится в модели ТЗ
    /// (<see cref="SpecLink"/>) и адресует элемент тем же <c>root/Activities[0]</c>, что
    /// <c>workflow_outline</c> и инструменты правки, поэтому проверка «элемент жив» делается по структуре,
    /// а не по памяти.
    /// </summary>
    public static class SpecCoverage
    {
        /// <summary>Строит отчёт по модели ТЗ и структуре процесса (структура может быть <c>null</c>).</summary>
        public static SpecCoverageReport Build(RpaSpecification specification, WorkflowOutline outline)
        {
            var report = new SpecCoverageReport();

            if (specification == null)
            {
                report.Reason = "модель ТЗ не собрана (spec_extract)";
                return report;
            }

            specification.Normalize();

            if (outline == null || outline.Root == null)
            {
                report.Reason = "нет открытого процесса: укажите path к XAML или откройте его в конструкторе";
                return report;
            }

            report.Traced = true;

            var knownElements = new HashSet<string>(
                outline.All().Select(node => node.Id).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);

            var items = new List<SpecCoverageEntry>();
            foreach (var step in specification.Steps.Where(step => !string.IsNullOrWhiteSpace(step.Id)))
            {
                items.Add(new SpecCoverageEntry { ItemId = step.Id.Trim(), Text = step.Text });
            }

            foreach (var criterion in specification.AcceptanceCriteria.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
            {
                items.Add(new SpecCoverageEntry { ItemId = criterion.Id.Trim(), Text = criterion.Text });
            }

            var byItem = items.ToDictionary(entry => entry.ItemId, entry => entry, StringComparer.OrdinalIgnoreCase);

            foreach (var link in specification.Links)
            {
                string itemId = (link.ItemId ?? string.Empty).Trim();
                string elementId = (link.ElementId ?? string.Empty).Trim();

                if (!byItem.TryGetValue(itemId, out var entry))
                {
                    report.BrokenLinks.Add("метка «" + itemId + " → " + elementId +
                                           "»: пункт ТЗ удалён или переименован");
                    continue;
                }

                if (!knownElements.Contains(elementId))
                {
                    report.BrokenLinks.Add("метка «" + itemId + " → " + elementId +
                                           "»: элемент workflow отсутствует в структуре");
                    continue;
                }

                if (!entry.ElementIds.Contains(elementId, StringComparer.OrdinalIgnoreCase))
                {
                    entry.ElementIds.Add(elementId);
                }
            }

            foreach (var entry in items)
            {
                if (entry.ElementIds.Count > 0)
                {
                    report.Covered.Add(entry);
                }
                else
                {
                    report.Uncovered.Add(entry);
                }
            }

            return report;
        }

        /// <summary>Отчёт «трассировка не выполнена» с причиной.</summary>
        public static SpecCoverageReport NotTraced(string reason)
        {
            return new SpecCoverageReport { Reason = reason };
        }
    }
}
