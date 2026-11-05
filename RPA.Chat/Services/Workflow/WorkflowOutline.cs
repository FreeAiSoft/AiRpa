using System;
using System.Collections.Generic;
using System.Linq;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>Важность замечания в структуре процесса.</summary>
    public enum WorkflowOutlineIssueSeverity
    {
        Error,
        Warning
    }

    /// <summary>
    /// Откуда взята структура процесса (карточка <c>AG-06</c>): из модели открытого конструктора (вместе
    /// с несохранёнными правками) или из файла на диске. Агент обязан видеть разницу: «валидно в
    /// конструкторе» и «валидно в файле» — разные утверждения, пока правки не сохранены.
    /// </summary>
    public enum WorkflowOutlineSource
    {
        /// <summary>Источник неизвестен (структура собрана вызывающим кодом или фейком в тестах).</summary>
        Unknown,

        /// <summary>Файл процесса на диске.</summary>
        File,

        /// <summary>Живая модель открытой вкладки конструктора, включая несохранённые правки.</summary>
        Designer
    }

    /// <summary>
    /// Замечание к узлу структуры процесса (карточка <c>AG-03</c>): ошибка валидации WF, предупреждение
    /// или ошибка чтения файла. Всегда привязано к <c>Id</c> узла, чтобы агент правил именно тот элемент,
    /// из-за которого валидация не проходит, а не перечитывал весь XAML.
    /// </summary>
    public sealed class WorkflowOutlineIssue
    {
        public WorkflowOutlineIssue(
            string elementId,
            WorkflowOutlineIssueSeverity severity,
            string message,
            string code = null,
            string propertyName = null)
        {
            ElementId = (elementId ?? string.Empty).Trim();
            Severity = severity;
            Message = (message ?? string.Empty).Trim();
            Code = code;
            PropertyName = propertyName;
        }

        /// <summary><c>Id</c> узла (<c>root/Activities[0]</c>); пустая строка — замечание к файлу целиком.</summary>
        public string ElementId { get; }

        public WorkflowOutlineIssueSeverity Severity { get; }

        /// <summary><c>ошибка</c> или <c>предупреждение</c> — сразу в ответ инструмента.</summary>
        public string SeverityName =>
            Severity == WorkflowOutlineIssueSeverity.Error ? "ошибка" : "предупреждение";

        public string Message { get; }

        /// <summary>Код проверки (<c>xaml.load</c>, <c>xaml.path</c>) или имя свойства активности.</summary>
        public string Code { get; }

        /// <summary>Свойство активности, из-за которого ошибка (<c>To</c>, <c>Condition</c>).</summary>
        public string PropertyName { get; }

        public bool IsError => Severity == WorkflowOutlineIssueSeverity.Error;

        public override string ToString()
        {
            string prefix = string.IsNullOrEmpty(ElementId) ? SeverityName : SeverityName + " " + ElementId;
            string suffix = string.IsNullOrWhiteSpace(PropertyName) ? string.Empty : " [" + PropertyName + "]";

            return prefix + ": " + Message + suffix;
        }
    }

    /// <summary>Аргумент активности в структуре процесса: имя, направление, тип и текущее выражение.</summary>
    public sealed class WorkflowOutlineArgument
    {
        public WorkflowOutlineArgument(string name, string direction, string typeName, string expression = null)
        {
            Name = name ?? string.Empty;
            Direction = string.IsNullOrWhiteSpace(direction) ? "In" : direction;
            TypeName = string.IsNullOrWhiteSpace(typeName) ? "Object" : typeName;
            Expression = expression;
        }

        public string Name { get; }

        public string Direction { get; }

        public string TypeName { get; }

        /// <summary>Текст выражения C#/VB; <c>null</c>, если аргумент не задан.</summary>
        public string Expression { get; }

        public bool IsSet => !string.IsNullOrWhiteSpace(Expression);

        public override string ToString()
        {
            return IsSet
                ? $"{Name}({Direction}, {TypeName}) = {Expression}"
                : $"{Name}({Direction}, {TypeName})";
        }
    }

    /// <summary>Переменная, объявленная в области видимости активности.</summary>
    public sealed class WorkflowOutlineVariable
    {
        public WorkflowOutlineVariable(string name, string typeName, string defaultExpression = null)
        {
            Name = name ?? string.Empty;
            TypeName = string.IsNullOrWhiteSpace(typeName) ? "Object" : typeName;
            DefaultExpression = defaultExpression;
        }

        public string Name { get; }

        public string TypeName { get; }

        /// <summary>Выражение значения по умолчанию; <c>null</c>, если оно не задано.</summary>
        public string DefaultExpression { get; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DefaultExpression)
                ? $"{Name} ({TypeName})"
                : $"{Name} ({TypeName}) = {DefaultExpression}";
        }
    }

    /// <summary>
    /// Узел структуры процесса. <see cref="Id"/> — путь в дереве (<c>root/Activities[0]</c>,
    /// <c>root/Then</c>, <c>root/Catches[0]/Action/Handler</c>), он же адрес элемента для правок:
    /// одна и та же строка годится и для чтения, и для последующих инструментов <c>AG-04</c>.
    /// </summary>
    public sealed class WorkflowOutlineNode
    {
        private static readonly WorkflowOutlineArgument[] NoArguments = new WorkflowOutlineArgument[0];
        private static readonly WorkflowOutlineVariable[] NoVariables = new WorkflowOutlineVariable[0];

        private readonly List<WorkflowOutlineNode> _children = new List<WorkflowOutlineNode>();
        private int _childCount = -1;

        public WorkflowOutlineNode(
            string id,
            string parentId,
            int index,
            string containerName,
            string typeName,
            string displayName,
            IReadOnlyList<WorkflowOutlineArgument> arguments = null,
            IReadOnlyList<WorkflowOutlineVariable> variables = null)
        {
            Id = id ?? string.Empty;
            ParentId = parentId ?? string.Empty;
            Index = index;
            ContainerName = containerName ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Arguments = arguments ?? NoArguments;
            Variables = variables ?? NoVariables;
        }

        /// <summary>Стабильный адрес узла: <c>root</c>, <c>root/Activities[1]</c>.</summary>
        public string Id { get; }

        /// <summary><c>Id</c> родителя; пустая строка у корня.</summary>
        public string ParentId { get; }

        /// <summary>Порядковый номер в контейнере родителя.</summary>
        public int Index { get; }

        /// <summary>Имя контейнера родителя, в котором лежит узел (<c>Activities[1]</c>, <c>Then</c>).</summary>
        public string ContainerName { get; }

        /// <summary>Полное имя типа (<c>System.Activities.Statements.Sequence</c>).</summary>
        public string TypeName { get; }

        /// <summary>Короткое имя типа (<c>Sequence</c>) — так активность ищется в каталоге.</summary>
        public string ShortTypeName
        {
            get
            {
                int dot = TypeName.LastIndexOf('.');
                return dot >= 0 ? TypeName.Substring(dot + 1) : TypeName;
            }
        }

        /// <summary>Подпись в конструкторе; пустая у активности без <c>DisplayName</c>.</summary>
        public string DisplayName { get; }

        public IReadOnlyList<WorkflowOutlineArgument> Arguments { get; }

        public IReadOnlyList<WorkflowOutlineVariable> Variables { get; }

        public IReadOnlyList<WorkflowOutlineNode> Children => _children;

        /// <summary>Сколько детей у узла в модели (может быть больше показанных при <c>depth</c>).</summary>
        public int ChildCount => _childCount >= 0 ? _childCount : _children.Count;

        /// <summary>Дети есть, но не показаны из-за запрошенной глубины.</summary>
        public bool IsTruncated { get; private set; }

        /// <summary>Узел принимает дочерние активности.</summary>
        public bool CanContainChildren => ChildCount > 0;

        public void AddChild(WorkflowOutlineNode child)
        {
            if (child != null)
            {
                _children.Add(child);
            }
        }

        /// <summary>Сколько детей в модели; вызывается до разворачивания, чтобы сохранить <see cref="ChildCount"/>.</summary>
        public void SetChildCount(int childCount)
        {
            _childCount = childCount < 0 ? 0 : childCount;
        }

        /// <summary>Помечает узел как обрезанный по глубине (дети не показаны).</summary>
        public void MarkTruncated()
        {
            IsTruncated = true;
        }

        /// <summary>Все потомки в порядке обхода (дети и их дети).</summary>
        public IEnumerable<WorkflowOutlineNode> Descendants()
        {
            foreach (WorkflowOutlineNode child in _children)
            {
                yield return child;

                foreach (WorkflowOutlineNode nested in child.Descendants())
                {
                    yield return nested;
                }
            }
        }

        public override string ToString()
        {
            return Id + ": " + ShortTypeName + (string.IsNullOrWhiteSpace(DisplayName) ? string.Empty : " «" + DisplayName + "»");
        }
    }

    /// <summary>
    /// Структура процесса для агента (карточка <c>AG-03</c>): дерево активностей с типами, адресами,
    /// аргументами, переменными и замечаниями валидации — вместо сырого XAML.
    /// <para>
    /// <see cref="Ok"/> означает «структура прочитана», а не «процесс валиден»: у процесса с ошибками
    /// валидации агент всё равно получает дерево и <see cref="Issues"/> с привязкой к узлам, иначе
    /// цикл «правка → валидация → правка» невозможен.
    /// </para>
    /// </summary>
    public sealed class WorkflowOutline
    {
        private readonly List<WorkflowOutlineIssue> _issues = new List<WorkflowOutlineIssue>();

        public WorkflowOutline(string filePath, int depth = 0, bool includeArguments = true)
        {
            FilePath = filePath;
            Depth = depth < 0 ? 0 : depth;
            IncludeArguments = includeArguments;
        }

        /// <summary>Структура прочитана; при <c>false</c> дерева нет, причина — в <see cref="Issues"/>.</summary>
        public bool Ok { get; set; }

        /// <summary>Полный путь файла процесса.</summary>
        public string FilePath { get; }

        /// <summary>
        /// Откуда прочитана структура (карточка <c>AG-06</c>): конструктор или файл. Выставляется
        /// источником данных — по нему <c>workflow_validate</c> сообщает, что именно проверено.
        /// </summary>
        public WorkflowOutlineSource Source { get; set; }

        /// <summary>
        /// Валидация WF выполнена и её результат попал в <see cref="Issues"/>. <c>false</c> — валидации
        /// не было (сбой <c>ActivityValidationServices.Validate</c>), и это «неизвестно», а не «ошибок нет».
        /// </summary>
        public bool ValidationAvailable { get; private set; }

        /// <summary>Почему валидация не выполнена; <c>null</c>, если выполнена.</summary>
        public string ValidationUnavailableReason { get; private set; }

        /// <summary>Валидация WF отработала: вердикт «ошибок нет» теперь утверждение, а не догадка.</summary>
        public void MarkValidationAvailable()
        {
            ValidationAvailable = true;
            ValidationUnavailableReason = null;
        }

        /// <summary>Валидация WF не выполнена: причина обязательна, иначе отчёт прочитают как «ошибок нет».</summary>
        public void MarkValidationUnavailable(string reason)
        {
            ValidationAvailable = false;
            ValidationUnavailableReason = string.IsNullOrWhiteSpace(reason)
                ? "валидация WF не выполнена"
                : reason.Trim();
        }

        /// <summary><c>x:Class</c> процесса; пусто у файла без класса.</summary>
        public string WorkflowId { get; set; }

        public WorkflowOutlineNode Root { get; set; }

        /// <summary>Число активностей в модели (включая не показанные из-за глубины).</summary>
        public int ActivityCount { get; set; }

        /// <summary>Запрошенная глубина: 0 — всё дерево, 1 — только корень.</summary>
        public int Depth { get; }

        /// <summary>Аргументы включены в узлы.</summary>
        public bool IncludeArguments { get; }

        /// <summary>Сколько уровней реально показано (0 у пустого дерева).</summary>
        public int ShownDepth { get; set; }

        public IReadOnlyList<WorkflowOutlineIssue> Issues => _issues;

        public bool HasErrors => _issues.Any(issue => issue.IsError);

        public IEnumerable<WorkflowOutlineIssue> Errors => _issues.Where(issue => issue.IsError);

        public IEnumerable<WorkflowOutlineIssue> Warnings => _issues.Where(issue => !issue.IsError);

        public void AddIssue(WorkflowOutlineIssue issue)
        {
            if (issue != null)
            {
                _issues.Add(issue);
            }
        }

        public void AddError(string elementId, string message, string code = null, string propertyName = null)
        {
            AddIssue(new WorkflowOutlineIssue(elementId, WorkflowOutlineIssueSeverity.Error, message, code, propertyName));
        }

        public void AddWarning(string elementId, string message, string code = null, string propertyName = null)
        {
            AddIssue(new WorkflowOutlineIssue(elementId, WorkflowOutlineIssueSeverity.Warning, message, code, propertyName));
        }

        /// <summary>Все показанные узлы: корень и потомки в порядке обхода.</summary>
        public IEnumerable<WorkflowOutlineNode> All()
        {
            if (Root == null)
            {
                return Enumerable.Empty<WorkflowOutlineNode>();
            }

            return new[] { Root }.Concat(Root.Descendants());
        }

        /// <summary>
        /// Узел по <c>Id</c>; дополнительно принимается <c>DisplayName</c>, если он уникален, — агенту
        /// так проще, а неоднозначная подпись возвращает <c>null</c>, а не «какой-нибудь» узел.
        /// </summary>
        public WorkflowOutlineNode Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            string key = id.Trim();
            WorkflowOutlineNode byId = All().FirstOrDefault(node =>
                string.Equals(node.Id, key, StringComparison.OrdinalIgnoreCase));

            if (byId != null)
            {
                return byId;
            }

            List<WorkflowOutlineNode> byName = All()
                .Where(node => string.Equals(node.DisplayName, key, StringComparison.Ordinal))
                .ToList();

            return byName.Count == 1 ? byName[0] : null;
        }

        public override string ToString()
        {
            return $"{FilePath}: узлов {All().Count()}, активностей {ActivityCount}, замечаний {_issues.Count}";
        }
    }
}
