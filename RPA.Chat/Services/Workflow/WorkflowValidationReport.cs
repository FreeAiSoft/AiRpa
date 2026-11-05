using System;
using System.Collections.Generic;
using System.Linq;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Вердикт валидации процесса для агента (карточка <c>AG-06</c>, инструмент <c>workflow_validate</c>).
    /// <para>
    /// Строится поверх <see cref="WorkflowOutline"/> — то есть над тем же деревом и теми же адресами,
    /// что отдают <c>workflow_outline</c> и инструменты правки (<c>AG-04</c>, <c>AG-05</c>): агент получает
    /// «ошибка в <c>root/Activities[0]</c>» и правит именно этот элемент. Второй валидации в студии нет
    /// намеренно, иначе два инструмента показывали бы разные адреса и разные тексты.
    /// </para>
    /// <para>
    /// Три состояния различаются явно, и это главное требование карточки:
    /// <list type="bullet">
    /// <item><description>валидация выполнена, ошибок нет (<see cref="Ok"/>) — можно собирать робота;</description></item>
    /// <item><description>валидация выполнена, ошибки есть — печатаются с адресами и свойствами;</description></item>
    /// <item><description>валидация <b>не выполнена</b> (<see cref="Available"/> = <c>false</c>) — файл не прочитан
    /// или <c>ActivityValidationServices.Validate</c> упал. Это «неизвестно», а не «ошибок нет».</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Ошибки чтения XAML (<c>xaml.path</c>, <c>xaml.load</c>) лежат отдельно от замечаний WF-валидации
    /// (<c>wf.validate</c>): первое — «процесс не прочитан», второе — «процесс прочитан и невалиден».
    /// Смешивать их значит обещать агенту вердикт, которого не было.
    /// </para>
    /// </summary>
    public sealed class WorkflowValidationReport
    {
        /// <summary>Предел числа известных адресов в диагностике «адрес не найден».</summary>
        public const int KnownAddressLimit = 12;

        private readonly List<WorkflowOutlineIssue> _issues = new List<WorkflowOutlineIssue>();
        private readonly List<WorkflowOutlineIssue> _loadIssues = new List<WorkflowOutlineIssue>();
        private readonly List<WorkflowOutlineIssue> _validationIssues = new List<WorkflowOutlineIssue>();
        private readonly List<string> _knownAddresses = new List<string>();

        private WorkflowValidationReport(
            string filePath,
            string elementId,
            WorkflowOutlineSource source,
            int activityCount,
            int maxIssues)
        {
            FilePath = filePath;
            ElementId = string.IsNullOrWhiteSpace(elementId) ? null : elementId.Trim();
            Source = source;
            ActivityCount = activityCount < 0 ? 0 : activityCount;
            MaxIssues = maxIssues < 0 ? 0 : maxIssues;
        }

        /// <summary>Путь процесса, по которому запрошена валидация.</summary>
        public string FilePath { get; }

        /// <summary>Запрошенный адрес элемента; <c>null</c> — валидировался процесс целиком.</summary>
        public string ElementId { get; }

        /// <summary>Адрес элемента, по которому реально отфильтровано (может прийти из подписи); <c>null</c> — без фильтра.</summary>
        public string ScopeId { get; private set; }

        /// <summary>Источник структуры: живой конструктор (с несохранёнными правками) или файл.</summary>
        public WorkflowOutlineSource Source { get; }

        /// <summary><c>конструктор</c> / <c>файл</c> / <c>неизвестно</c> — сразу в ответ инструмента.</summary>
        public string SourceName
        {
            get
            {
                switch (Source)
                {
                    case WorkflowOutlineSource.Designer:
                        return "конструктор";
                    case WorkflowOutlineSource.File:
                        return "файл";
                    default:
                        return "неизвестно";
                }
            }
        }

        /// <summary>Число активностей в проверенном дереве.</summary>
        public int ActivityCount { get; }

        /// <summary>Предел числа замечаний в отчёте; остальные считаются, но не печатаются.</summary>
        public int MaxIssues { get; }

        /// <summary>Валидация WF выполнена: «ошибок нет» — утверждение, а не отсутствие данных.</summary>
        public bool Available { get; private set; }

        /// <summary>Почему валидация не выполнена; <c>null</c>, если выполнена.</summary>
        public string UnavailableReason { get; private set; }

        /// <summary>Адрес из запроса не найден в процессе: это диагностика, а не «ошибок нет».</summary>
        public bool AddressNotFound { get; private set; }

        /// <summary>Известные адреса процесса — помощь агенту, когда адрес не найден.</summary>
        public IReadOnlyList<string> KnownAddresses => _knownAddresses;

        /// <summary>Замечания чтения (<c>xaml.path</c>, <c>xaml.load</c>): процесс не прочитан.</summary>
        public IReadOnlyList<WorkflowOutlineIssue> LoadIssues => _loadIssues;

        /// <summary>Замечания WF-валидации и прочие замечания модели (<c>wf.validate</c>).</summary>
        public IReadOnlyList<WorkflowOutlineIssue> ValidationIssues => _validationIssues;

        /// <summary>Замечания в области отчёта, обрезанные по <see cref="MaxIssues"/>.</summary>
        public IReadOnlyList<WorkflowOutlineIssue> Issues => _issues;

        /// <summary>Сколько замечаний попало в область отчёта (до обрезки по лимиту).</summary>
        public int TotalIssues { get; private set; }

        /// <summary>Сколько замечаний не показано из-за лимита.</summary>
        public int HiddenIssues => TotalIssues - _issues.Count;

        /// <summary>Ошибок в области отчёта (считаются все, включая не показанные по лимиту).</summary>
        public int Errors { get; private set; }

        /// <summary>Предупреждений в области отчёта.</summary>
        public int Warnings { get; private set; }

        /// <summary>
        /// Процесс проверен и ошибок нет. Предупреждения вердикт не отменяют, «валидация не выполнена»,
        /// «адрес не найден» и ошибки — отменяют.
        /// </summary>
        public bool Ok => Available && Errors == 0 && !AddressNotFound;

        /// <summary>Отчёт «валидация не выполнена»: причина обязательна, иначе агент прочитает это как «ошибок нет».</summary>
        public static WorkflowValidationReport Unavailable(string filePath, string reason)
        {
            var report = new WorkflowValidationReport(filePath, null, WorkflowOutlineSource.Unknown, 0, WorkflowOutlineText.MaxIssues)
            {
                Available = false,
                UnavailableReason = string.IsNullOrWhiteSpace(reason)
                    ? "валидация WF не выполнена: причина неизвестна"
                    : reason.Trim()
            };

            return report;
        }

        /// <summary>
        /// Отчёт по структуре процесса. <paramref name="elementId"/> — адрес из <c>workflow_outline</c>
        /// (или уникальная подпись): тогда считаются замечания самого элемента и его потомков.
        /// <c>null</c> в <paramref name="outline"/> — структура не прочитана, валидации не было.
        /// </summary>
        public static WorkflowValidationReport FromOutline(
            WorkflowOutline outline,
            string elementId = null,
            int maxIssues = WorkflowOutlineText.MaxIssues)
        {
            if (outline == null)
            {
                return Unavailable(null, "структура процесса не прочитана: валидация не выполнялась");
            }

            var report = new WorkflowValidationReport(
                outline.FilePath,
                elementId,
                outline.Source,
                outline.ActivityCount,
                maxIssues);

            // Чтение и валидация — разные вещи: у каждой свой список, чтобы «файл не прочитан»
            // никогда не выглядел как «валидация прошла».
            foreach (WorkflowOutlineIssue issue in outline.Issues)
            {
                if (issue == null)
                {
                    continue;
                }

                if (IsLoadIssue(issue))
                {
                    report._loadIssues.Add(issue);
                }
                else
                {
                    report._validationIssues.Add(issue);
                }
            }

            if (!outline.Ok)
            {
                report.Available = false;
                report.UnavailableReason = DescribeLoadFailure(report._loadIssues);
                return report;
            }

            if (!outline.ValidationAvailable)
            {
                report.Available = false;
                report.UnavailableReason = string.IsNullOrWhiteSpace(outline.ValidationUnavailableReason)
                    ? "валидация WF не выполнена: ActivityValidationServices.Validate не отработал"
                    : outline.ValidationUnavailableReason.Trim();
                return report;
            }

            report.Available = true;
            report.ApplyScope(outline);

            return report;
        }

        /// <summary>Замечание относится к чтению файла, а не к валидации WF.</summary>
        public static bool IsLoadIssue(WorkflowOutlineIssue issue)
        {
            string code = issue?.Code;

            return string.Equals(code, "xaml.path", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "xaml.load", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Замечание валидации WF (на него и опирается вердикт «ошибок нет»).</summary>
        public static bool IsValidationIssue(WorkflowOutlineIssue issue)
        {
            return !IsLoadIssue(issue);
        }

        /// <summary>
        /// Замечание входит в область адреса: сам элемент, его потомки и замечания без адреса
        /// (они относятся к процессу целиком, и скрывать их фильтром нельзя).
        /// Сравнение по сегментам пути: <c>root/Then</c> не должен захватывать <c>root/Then2</c>.
        /// </summary>
        public static bool InScope(WorkflowOutlineIssue issue, string scopeId)
        {
            if (issue == null)
            {
                return false;
            }

            string id = issue.ElementId;

            if (string.IsNullOrEmpty(id))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(scopeId))
            {
                return true;
            }

            if (string.Equals(id, scopeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return id.StartsWith(scopeId + "/", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyScope(WorkflowOutline outline)
        {
            IEnumerable<WorkflowOutlineIssue> scope = _validationIssues;

            if (ElementId != null)
            {
                WorkflowOutlineNode node = outline.Find(ElementId);

                if (node == null)
                {
                    AddressNotFound = true;

                    foreach (WorkflowOutlineNode known in outline.All().Take(KnownAddressLimit))
                    {
                        _knownAddresses.Add(known.Id);
                    }

                    return;
                }

                ScopeId = node.Id;
                scope = _validationIssues.Where(issue => InScope(issue, node.Id));
            }

            foreach (WorkflowOutlineIssue issue in scope)
            {
                TotalIssues++;

                if (issue.IsError)
                {
                    Errors++;
                }
                else
                {
                    Warnings++;
                }

                if (_issues.Count < MaxIssues)
                {
                    _issues.Add(issue);
                }
            }
        }

        private static string DescribeLoadFailure(IReadOnlyList<WorkflowOutlineIssue> loadIssues)
        {
            if (loadIssues == null || loadIssues.Count == 0)
            {
                return "структура процесса не прочитана: валидация не выполнялась";
            }

            return "процесс не прочитан, валидация не выполнялась: " + loadIssues[0].Message;
        }

        public override string ToString()
        {
            if (AddressNotFound)
            {
                return $"{FilePath}: адрес не найден — {ElementId}";
            }

            return Available
                ? $"{FilePath}: ошибок {Errors}, предупреждений {Warnings}, источник: {SourceName}"
                : $"{FilePath}: валидация не выполнена — {UnavailableReason}";
        }
    }
}
