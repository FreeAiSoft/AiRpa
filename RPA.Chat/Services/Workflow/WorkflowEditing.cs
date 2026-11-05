using System;
using System.Collections.Generic;
using System.Text;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Запрос вставки активности в открытый конструктором процесс (карточка <c>AG-02</c>).
    /// <para>
    /// Адреса узлов — те же <c>Id</c>, что отдаёт <c>workflow_outline</c> (<c>root/Activities[1]/Then</c>),
    /// поэтому агенту не нужно пересчитывать пути между чтением и правкой.
    /// </para>
    /// </summary>
    public sealed class WorkflowInsertRequest
    {
        private string _parentId;

        /// <summary>Адрес родителя; пусто — корень процесса.</summary>
        public string ParentId
        {
            get => string.IsNullOrWhiteSpace(_parentId) ? WorkflowEditing.RootElementId : _parentId.Trim();
            set => _parentId = value;
        }

        /// <summary>
        /// Имя контейнера родителя (<c>Activities</c>, <c>Then</c>, <c>Body</c>). Пусто — единственный
        /// подходящий контейнер выбирается автоматически; если их несколько, вернётся ошибка со списком.
        /// </summary>
        public string Container { get; set; }

        /// <summary>Позиция в контейнере; <c>null</c> — в конец.</summary>
        public int? Index { get; set; }

        /// <summary>Тип активности: короткое (<c>WriteLine</c>) или полное имя.</summary>
        public string ActivityType { get; set; }

        /// <summary>Подпись на диаграмме; пусто — как задаст конструктор.</summary>
        public string DisplayName { get; set; }

        public override string ToString()
        {
            string position = Index.HasValue ? "#" + Index.Value : string.Empty;
            string container = string.IsNullOrWhiteSpace(Container) ? string.Empty : "." + Container.Trim();

            return ParentId + container + position + " <- " + ActivityType;
        }
    }

    /// <summary>Запрос переноса активности внутри открытого процесса.</summary>
    public sealed class WorkflowMoveRequest
    {
        private string _parentId;

        /// <summary>Адрес переносимой активности.</summary>
        public string ElementId { get; set; }

        /// <summary>Адрес нового родителя; пусто — корень процесса.</summary>
        public string ParentId
        {
            get => string.IsNullOrWhiteSpace(_parentId) ? WorkflowEditing.RootElementId : _parentId.Trim();
            set => _parentId = value;
        }

        /// <summary>Контейнер нового родителя; пусто — единственный подходящий.</summary>
        public string Container { get; set; }

        /// <summary>Позиция в новом контейнере; <c>null</c> — в конец.</summary>
        public int? Index { get; set; }

        public override string ToString()
        {
            string position = Index.HasValue ? "#" + Index.Value : string.Empty;
            string container = string.IsNullOrWhiteSpace(Container) ? string.Empty : "." + Container.Trim();

            return ElementId + " -> " + ParentId + container + position;
        }
    }

    /// <summary>
    /// Запрос обёртки активности (карточка <c>AG-04</c>): «возьми этот шаг и положи его внутрь новой
    /// активности» — так строятся <c>If</c>, <c>TryCatch</c>, <c>While</c> и <c>ForEach</c> вокруг уже
    /// готового шага. Обёртка занимает место элемента, элемент становится её первым ребёнком.
    /// </summary>
    public sealed class WorkflowWrapRequest
    {
        /// <summary>Адрес обёртываемой активности.</summary>
        public string ElementId { get; set; }

        /// <summary>Тип обёртки: короткое (<c>Sequence</c>, <c>If</c>) или полное имя типа.</summary>
        public string WrapperType { get; set; }

        /// <summary>
        /// Контейнер внутри обёртки, куда кладётся элемент (<c>Activities</c>, <c>Then</c>, <c>Body</c>).
        /// Пусто — единственный подходящий; у <c>If</c>/<c>TryCatch</c> контейнеров несколько, и выбор
        /// не угадывается: придёт ошибка со списком.
        /// </summary>
        public string Container { get; set; }

        /// <summary>Подпись обёртки на диаграмме; пусто — как задаст конструктор.</summary>
        public string DisplayName { get; set; }

        public override string ToString()
        {
            string container = string.IsNullOrWhiteSpace(Container) ? string.Empty : "." + Container.Trim();

            return "wrap " + ElementId + " in " + WrapperType + container;
        }
    }

    /// <summary>
    /// Результат одной правки открытого процесса (карточка <c>AG-02</c>): что сделано, адреса изменённого
    /// и появившегося элементов, замечания и состояние «есть что отменить / вернуть».
    /// <para>
    /// <see cref="Ok"/> = правка применена к модели конструктора. Ошибки WF-валидации правку не отменяют
    /// (это работа <c>AG-09</c>): они возвращаются замечаниями с адресом узла, чтобы агент сразу видел,
    /// что именно исправить.
    /// </para>
    /// </summary>
    public sealed class WorkflowEditResult
    {
        private readonly List<WorkflowOutlineIssue> _issues = new List<WorkflowOutlineIssue>();
        private readonly List<string> _addresses = new List<string>();

        public WorkflowEditResult(string operation, bool ok, string message = null)
        {
            Operation = operation ?? string.Empty;
            Ok = ok;
            Message = message;
        }

        /// <summary>Действие: <c>insert</c>, <c>delete</c>, <c>move</c>, <c>complete</c>, <c>revert</c>, <c>undo</c>, <c>redo</c>.</summary>
        public string Operation { get; }

        public bool Ok { get; private set; }

        /// <summary>Короткая сводка для ответа инструмента.</summary>
        public string Message { get; private set; }

        /// <summary>Адрес элемента, к которому относилась правка.</summary>
        public string ElementId { get; private set; }

        /// <summary>Адрес появившегося элемента (вставка, перенос): по нему агент задаёт аргументы.</summary>
        public string NewElementId { get; private set; }

        /// <summary>Пачка правок ещё открыта: следующий вызов продолжит её, <c>complete</c> — закроет.</summary>
        public bool SessionActive { get; private set; }

        /// <summary>В конструкторе есть что отменить (<c>UndoEngine.CanUndo</c>).</summary>
        public bool CanUndo { get; private set; }

        /// <summary>В конструкторе есть что вернуть (<c>UndoEngine.CanRedo</c>).</summary>
        public bool CanRedo { get; private set; }

        /// <summary>Структура процесса после правки (живая модель, включая несохранённые изменения).</summary>
        public WorkflowOutline Outline { get; private set; }

        /// <summary>Сколько правок применилось в этом вызове (у пачки инструмента — больше одной).</summary>
        public int AppliedCount { get; private set; }

        /// <summary>
        /// Адреса созданных и перемещённых элементов по порядку — по ним агент задаёт аргументы.
        /// <see cref="NewElementId"/> — последний из них.
        /// </summary>
        public IReadOnlyList<string> Addresses => _addresses;

        /// <summary>
        /// Что изменилось в валидации процесса (карточка <c>AG-09</c>): только новые ошибки, а не весь
        /// список, — иначе агент правит то, что было сломано до него.
        /// </summary>
        public WorkflowValidationDelta Validation { get; private set; }

        public IReadOnlyList<WorkflowOutlineIssue> Issues => _issues;

        public bool HasErrors
        {
            get
            {
                foreach (WorkflowOutlineIssue issue in _issues)
                {
                    if (issue.IsError)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Правка применена и замечаний-ошибок нет.</summary>
        public bool IsClean => Ok && !HasErrors;

        public static WorkflowEditResult Success(string operation, string message = null)
        {
            return new WorkflowEditResult(operation, true, message);
        }

        public static WorkflowEditResult Failure(string operation, string error, string hint = null)
        {
            var result = new WorkflowEditResult(operation, false);

            if (!string.IsNullOrWhiteSpace(error))
            {
                result.AddError(string.Empty, error);
            }

            if (!string.IsNullOrWhiteSpace(hint))
            {
                result.AddHint(hint);
            }

            return result;
        }

        /// <summary>Адреса изменённого и появившегося элементов.</summary>
        public WorkflowEditResult WithElements(string elementId, string newElementId = null)
        {
            ElementId = elementId;
            NewElementId = newElementId;
            return this;
        }

        /// <summary>Состояние пачки и истории конструктора на момент ответа.</summary>
        public WorkflowEditResult WithState(bool sessionActive, bool canUndo = false, bool canRedo = false)
        {
            SessionActive = sessionActive;
            CanUndo = canUndo;
            CanRedo = canRedo;
            return this;
        }

        /// <summary>Структура процесса после правки (её же возвращает <c>workflow_outline</c>).</summary>
        public WorkflowEditResult WithOutline(WorkflowOutline outline)
        {
            Outline = outline;
            return this;
        }

        /// <summary>Уточнение сводки (например, «вставлено в Sequence.Activities#2»).</summary>
        public WorkflowEditResult WithMessage(string message)
        {
            Message = message;
            return this;
        }

        /// <summary>
        /// Сколько правок применилось и по каким адресам — ответ на «что именно я собрал» одним вызовом
        /// (пачка <c>activity_insert</c> из нескольких шагов).
        /// </summary>
        public WorkflowEditResult WithApplied(int count, IEnumerable<string> addresses = null)
        {
            AppliedCount = count < 0 ? 0 : count;

            if (addresses != null)
            {
                foreach (string address in addresses)
                {
                    if (!string.IsNullOrWhiteSpace(address))
                    {
                        _addresses.Add(address.Trim());
                    }
                }
            }

            if (_addresses.Count > 0)
            {
                NewElementId = _addresses[_addresses.Count - 1];
            }

            return this;
        }

        /// <summary>Прикладывает изменение валидации (<c>AG-09</c>).</summary>
        public WorkflowEditResult WithValidation(WorkflowValidationDelta validation)
        {
            Validation = validation;
            return this;
        }

        public WorkflowEditResult AddError(string elementId, string message, string code = null)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _issues.Add(new WorkflowOutlineIssue(elementId, WorkflowOutlineIssueSeverity.Error, message, code));
            }

            return this;
        }

        public WorkflowEditResult AddWarning(string elementId, string message, string code = null)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                _issues.Add(new WorkflowOutlineIssue(elementId, WorkflowOutlineIssueSeverity.Warning, message, code));
            }

            return this;
        }

        /// <summary>Подсказка «что делать» — предупреждение с кодом <c>hint</c>.</summary>
        public WorkflowEditResult AddHint(string message)
        {
            return AddWarning(string.Empty, message, "hint");
        }

        /// <summary>Добавляет ошибки и подсказки из другого результата (например, из модели конструктора).</summary>
        public WorkflowEditResult Merge(IEnumerable<string> errors, IEnumerable<string> hints = null)
        {
            if (errors != null)
            {
                foreach (string error in errors)
                {
                    AddError(string.Empty, error);
                }
            }

            if (hints != null)
            {
                foreach (string hint in hints)
                {
                    AddHint(hint);
                }
            }

            return this;
        }

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(Operation).Append(Ok ? ": ок" : ": ошибка");

            if (!string.IsNullOrWhiteSpace(Message))
            {
                text.Append(" — ").Append(Message);
            }

            foreach (WorkflowOutlineIssue issue in _issues)
            {
                text.AppendLine().Append("  ").Append(issue);
            }

            return text.ToString();
        }
    }

    /// <summary>Значения, общие для контракта правок.</summary>
    public static class WorkflowEditing
    {
        /// <summary>Адрес корня процесса — так же его называет <c>workflow_outline</c>.</summary>
        public const string RootElementId = "root";
    }
}
