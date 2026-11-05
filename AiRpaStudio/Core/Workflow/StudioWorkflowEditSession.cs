using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using RPA.Helpers;
// Мост правок (AG-02) и правка модели конструктора (палитра) исторически называются одинаково:
// в этом файле «WorkflowEditResult» — ответ агенту, «ModelEditResult» — результат модели конструктора.
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;
using ModelEditResult = RPA.Core.Generation.WorkflowEditResult;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Пачка правок открытого процесса в конструкторе (карточка <c>AG-02</c>): мост «агент ↔ дизайнер»
    /// поверх <c>ModelService</c>/<c>ModelItem</c>.
    /// <para>
    /// Почему модель, а не текст XAML: <c>ViewState</c> и <c>sap2010:WorkflowViewState.IdRef</c> проставляет
    /// сам конструктор, поэтому вставленная активность сразу видна на диаграмме, остаётся редактируемой
    /// руками, а отмена идёт штатной историей (<c>Ctrl+Z</c>).
    /// </para>
    /// <para>
    /// Каждая правка — отдельный <c>ModelEditingScope</c> (<c>ModelItem.BeginEdit</c>), то есть одна
    /// запись в истории отмены: <c>Ctrl+Z</c> снимает ровно то, что сделала одна операция. Держать один
    /// scope на всю пачку нельзя: до <c>Complete</c> конструктор держит изменения в буфере — следующая
    /// правка не видит предыдущую (ни адреса, ни дерева), а структура после вставки не читается.
    /// Пачка группирует операции и умеет отменить их все (<see cref="Revert"/>).
    /// </para>
    /// <para>
    /// Все действия выполняются на UI-потоке дизайнера через <see cref="IDesignerUiThread"/>: чат живёт
    /// в фоновом потоке, а модель конструктора к чужому потоку не пускает.
    /// </para>
    /// </summary>
    internal sealed class StudioWorkflowEditSession : IWorkflowEditSession
    {
        private readonly WorkflowDesignerEx _designer;
        private readonly IDesignerUiThread _ui;
        private readonly DesignerModelSession _model;
        private readonly ActivityPaletteRegistry _registry;

        private string _inactiveReason;
        private int _changes;
        private bool _active;

        internal StudioWorkflowEditSession(
            WorkflowDesignerEx designer,
            string filePath,
            string label,
            IDesignerUiThread ui)
        {
            _designer = designer ?? throw new ArgumentNullException(nameof(designer));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _registry = ActivityPaletteRegistry.Default;
            _model = new DesignerModelSession(designer);

            FilePath = filePath ?? string.Empty;
            Label = string.IsNullOrWhiteSpace(label) ? "правка агентом" : label.Trim();

            try
            {
                _active = _ui.Invoke(() => GetService<ModelService>()?.Root != null);
            }
            catch (Exception ex)
            {
                _inactiveReason = ErrorText.Describe("пачка правок не открыта", ex);
                return;
            }

            if (!_active)
            {
                _inactiveReason = "модель конструктора недоступна: ModelService не найден";
            }
        }

        public string FilePath { get; }

        public string Label { get; }

        public bool IsActive => _active && _inactiveReason == null;

        /// <summary>Почему пачка не открыта (для ответа инструмента).</summary>
        internal string InactiveReason => _inactiveReason;

        /// <summary>Сколько правок применилось в пачке.</summary>
        internal int ChangeCount => _changes;

        public WorkflowEditResult Insert(WorkflowInsertRequest request)
        {
            if (request == null)
            {
                return NotRequested("insert", "не задан запрос вставки");
            }

            if (string.IsNullOrWhiteSpace(request.ActivityType))
            {
                return NotRequested("insert", "не задан тип активности (activityType) — подсказку даёт activity_search");
            }

            if (!IsActive)
            {
                return Inactive("insert");
            }

            WorkflowEditResult result = Execute("insert", () =>
            {
                ModelItem parent = Resolve(request.ParentId);
                if (parent == null)
                {
                    return UnknownElement("insert", request.ParentId);
                }

                PaletteActivityDescriptor descriptor = _registry.Describe(parent.ItemType);
                if (descriptor == null)
                {
                    return WorkflowEditResult.Failure(
                            "insert",
                            "тип «" + parent.ItemType + "» не является активностью — вложить нельзя")
                        .WithElements(request.ParentId);
                }

                PaletteContainerDescriptor container = DesignerModelSession.ChooseContainer(
                    descriptor, request.Container, out string containerError);
                if (container == null)
                {
                    return WorkflowEditResult.Failure("insert", containerError).WithElements(request.ParentId);
                }

                // Адрес считается по выбранному контейнеру до вставки: путь вида parent/Container[индекс]
                // совпадает с адресами workflow_outline, а «дифф» идентификаторов после вставки неверен —
                // вставка сдвигает индексы у соседей.
                string address = AddressFor(container, request.ParentId, parent, request.Index);

                ModelEditResult inserted = _model.InsertActivity(
                    new PaletteActivitySpec(request.ActivityType.Trim()),
                    request.ParentId,
                    request.Container,
                    request.Index);

                if (!inserted.Ok)
                {
                    return WorkflowEditResult.Failure("insert", string.Empty)
                        .Merge(inserted.Errors, inserted.Hints)
                        .WithElements(request.ParentId);
                }

                return WorkflowEditResult.Success(
                        "insert",
                        "вставлено в «" + descriptor.Key + "." + container.Name + "»")
                    .WithElements(request.ParentId, address);
            });

            string displayName = (request.DisplayName ?? string.Empty).Trim();
            if (result.Ok && displayName.Length > 0)
            {
                // Подпись — отдельная правка по адресу уже вставленного элемента: в том же scope
                // конструктор ещё не отдаёт дерево с новой активностью.
                SetDisplayName(result.NewElementId, displayName);
            }

            return result;
        }

        public WorkflowEditResult Delete(string elementId)
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return NotRequested("delete", "не задан адрес элемента (elementId)");
            }

            if (!IsActive)
            {
                return Inactive("delete");
            }

            return Execute("delete", () => DeleteCore(elementId.Trim()));
        }

        public WorkflowEditResult Move(WorkflowMoveRequest request)
        {
            if (request == null)
            {
                return NotRequested("move", "не задан запрос переноса");
            }

            if (string.IsNullOrWhiteSpace(request.ElementId))
            {
                return NotRequested("move", "не задан адрес переносимой активности (elementId)");
            }

            if (!IsActive)
            {
                return Inactive("move");
            }

            return Execute("move", () => MoveCore(request));
        }

        public WorkflowEditResult Wrap(WorkflowWrapRequest request)
        {
            if (request == null)
            {
                return NotRequested("wrap", "не задан запрос обёртки");
            }

            if (string.IsNullOrWhiteSpace(request.ElementId))
            {
                return NotRequested("wrap", "не задан адрес обёртываемой активности (elementId)");
            }

            if (!IsActive)
            {
                return Inactive("wrap");
            }

            return Execute("wrap", () => WrapCore(request));
        }

        public WorkflowEditResult SetArgument(WorkflowArgumentSetRequest request)
        {
            if (request == null)
            {
                return NotRequested("argument_set", "не задан запрос задания аргумента");
            }

            if (string.IsNullOrWhiteSpace(request.ElementId))
            {
                return NotRequested("argument_set", "не задан адрес элемента (elementId)");
            }

            if (string.IsNullOrWhiteSpace(request.Argument))
            {
                return NotRequested("argument_set", "не задан аргумент (argument): имена аргументов даёт activity_describe");
            }

            if (!IsActive)
            {
                return Inactive("argument_set");
            }

            return Execute("argument_set", () => SetArgumentCore(request));
        }

        public WorkflowEditResult AddVariable(WorkflowVariableAddRequest request)
        {
            if (request == null)
            {
                return NotRequested("variable_add", "не задан запрос добавления переменной");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return NotRequested("variable_add", "не задано имя переменной (name)");
            }

            if (!IsActive)
            {
                return Inactive("variable_add");
            }

            return Execute("variable_add", () => AddVariableCore(request));
        }

        public WorkflowEditResult RemoveVariable(WorkflowVariableRemoveRequest request)
        {
            if (request == null)
            {
                return NotRequested("variable_remove", "не задан запрос удаления переменной");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return NotRequested("variable_remove", "не задано имя переменной (name)");
            }

            if (!IsActive)
            {
                return Inactive("variable_remove");
            }

            return Execute("variable_remove", () => RemoveVariableCore(request));
        }

        /// <summary>Закрывает пачку: правки уже в модели, в ответе — сводка и структура процесса.</summary>
        public WorkflowEditResult Complete()
        {
            if (!IsActive)
            {
                return Inactive("complete");
            }

            _active = false;

            (bool canUndo, bool canRedo) = UndoStateOf(_designer, _ui);

            return WorkflowEditResult.Success("complete", _changes == 0
                    ? "пачка закрыта без правок"
                    : "пачка закрыта: правок " + _changes)
                .WithState(false, canUndo, canRedo)
                .WithOutline(Outline());
        }

        /// <summary>
        /// Отменяет все правки пачки (по одной записи истории на правку) и закрывает её.
        /// Так модель возвращается к состоянию до <c>BeginEdit</c>.
        /// </summary>
        public WorkflowEditResult Revert()
        {
            if (!IsActive)
            {
                return Inactive("revert");
            }

            int changes = _changes;
            _changes = 0;
            _active = false;

            if (changes == 0)
            {
                return WorkflowEditResult.Success("revert", "правок в пачке не было")
                    .WithState(false)
                    .WithOutline(Outline());
            }

            try
            {
                return _ui.Invoke(() =>
                {
                    UndoEngine engine = GetService<UndoEngine>();

                    if (engine == null)
                    {
                        return WorkflowEditResult.Failure("revert", "откат невозможен: UndoEngine конструктора недоступен")
                            .WithState(false);
                    }

                    int undone = 0;
                    for (int index = 0; index < changes; index++)
                    {
                        if (!engine.Undo())
                        {
                            break;
                        }

                        undone++;
                    }

                    Flush();

                    WorkflowEditResult result = undone == changes
                        ? WorkflowEditResult.Success("revert", "пачка отменена: правок " + undone)
                        : WorkflowEditResult.Failure(
                            "revert",
                            "отменено " + undone + " из " + changes + " правок пачки",
                            "часть правок уже была отменена вручную: проверьте структуру процесса");

                    (bool canUndo, bool canRedo) = UndoStateOf(_designer, _ui);

                    return result.WithState(false, canUndo, canRedo).WithOutline(Outline());
                });
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure("revert", ErrorText.Describe("пачка правок не отменена", ex))
                    .WithState(false);
            }
        }

        public WorkflowOutline Outline(int depth = 0, bool includeArguments = true)
        {
            if (_inactiveReason != null)
            {
                return null;
            }

            try
            {
                return _ui.Invoke(() =>
                    StudioWorkflowOutlineService.FromDesigner(_designer, FilePath, depth, includeArguments));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Структура открытого процесса не прочитана: " + ErrorText.Of(ex));
                return null;
            }
        }

        /// <summary>
        /// Незакрытая пачка не оставляет правок: правка, о которой агент не сообщил пользователю,
        /// не должна оставаться на диаграмме.
        /// </summary>
        public void Dispose()
        {
            if (!IsActive)
            {
                return;
            }

            try
            {
                Revert();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Незавершённая пачка не отменена: " + ErrorText.Of(ex));
            }
        }

        /// <summary>Состояние истории конструктора: есть что отменить / вернуть.</summary>
        internal static (bool CanUndo, bool CanRedo) UndoStateOf(WorkflowDesignerEx designer, IDesignerUiThread ui)
        {
            if (designer == null || ui == null)
            {
                return (false, false);
            }

            try
            {
                return ui.Invoke(() =>
                {
                    UndoEngine engine = designer.Context?.Services?.GetService<UndoEngine>();
                    if (engine == null)
                    {
                        return (false, false);
                    }

                    return (engine.GetUndoActions().Any(), engine.GetRedoActions().Any());
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("История конструктора недоступна: " + ErrorText.Of(ex));
                return (false, false);
            }
        }

        private WorkflowEditResult DeleteCore(string elementId)
        {
            ModelItem item = Resolve(elementId);
            if (item == null)
            {
                return UnknownElement("delete", elementId);
            }

            if (item.Parent == null)
            {
                return WorkflowEditResult.Failure("delete", "«" + elementId + "» — корень процесса: удалить нельзя")
                    .WithElements(elementId);
            }

            if (!TryGetSlot(elementId, out ModelItemCollection collection, out ModelProperty slot, out string slotError))
            {
                return WorkflowEditResult.Failure("delete", slotError).WithElements(elementId);
            }

            if (collection == null)
            {
                string where = slot == null ? string.Empty : " «" + slot.Name + "»";

                return WorkflowEditResult.Failure(
                        "delete",
                        "«" + elementId + "» лежит в одиночном свойстве" + where +
                        ": удаление оставит структуру неполной — вставьте на его место другую активность " +
                        "или удалите родителя")
                    .WithElements(elementId);
            }

            collection.Remove(item);
            Flush();

            return WorkflowEditResult.Success("delete", "удалено: " + elementId)
                .WithElements(elementId);
        }

        private WorkflowEditResult MoveCore(WorkflowMoveRequest request)
        {
            string elementId = request.ElementId.Trim();

            ModelItem item = Resolve(elementId);
            if (item == null)
            {
                return UnknownElement("move", elementId);
            }

            if (item.Parent == null)
            {
                return WorkflowEditResult.Failure("move", "корень процесса перенести нельзя").WithElements(elementId);
            }

            if (!TryGetSlot(elementId, out ModelItemCollection source, out ModelProperty slot, out string slotError))
            {
                return WorkflowEditResult.Failure("move", slotError).WithElements(elementId);
            }

            if (source == null)
            {
                string where = slot == null ? string.Empty : " «" + slot.Name + "»";

                return WorkflowEditResult.Failure(
                        "move",
                        "«" + elementId + "» лежит в одиночном свойстве" + where +
                        ": перенос возможен только из коллекции")
                    .WithElements(elementId);
            }

            if (!(item.GetCurrentValue() is Activity activity))
            {
                return WorkflowEditResult.Failure("move", "«" + elementId + "» — не активность: перенести нельзя")
                    .WithElements(elementId);
            }

            ModelItem parent = Resolve(request.ParentId);
            if (parent == null)
            {
                return UnknownElement("move", request.ParentId);
            }

            PaletteActivityDescriptor descriptor = _registry.Describe(parent.ItemType);
            if (descriptor == null)
            {
                return WorkflowEditResult.Failure(
                        "move",
                        "тип «" + parent.ItemType + "» не является активностью — переносить некуда")
                    .WithElements(elementId);
            }

            PaletteContainerDescriptor container = DesignerModelSession.ChooseContainer(
                descriptor, request.Container, out string containerError);
            if (container == null)
            {
                return WorkflowEditResult.Failure("move", containerError).WithElements(elementId);
            }

            ModelTreeManager treeManager = GetService<ModelTreeManager>();
            if (treeManager == null)
            {
                return WorkflowEditResult.Failure("move", "модель конструктора недоступна (ModelTreeManager не найден)")
                    .WithElements(elementId);
            }

            // Сначала убираем из старого контейнера, затем создаём узел в новом: один объект активности
            // не может находиться в двух местах дерева, а ModelItem после изъятия уже не переиспользуется.
            source.Remove(item);

            ModelItem moved = DesignerModelSession.CreateItemForContainer(treeManager, container, activity);
            if (moved == null)
            {
                return WorkflowEditResult.Failure(
                        "move",
                        "активность «" + descriptor.Key + "» не приняла перенос в «" + container.Name + "»")
                    .WithElements(elementId);
            }

            string address = AddressFor(container, request.ParentId, parent, request.Index);

            if (container.Kind == PaletteContainerKind.ActivityCollection)
            {
                ModelItemCollection target = parent.Properties[container.Name]?.Collection;
                if (target == null)
                {
                    return WorkflowEditResult.Failure(
                            "move",
                            "коллекция «" + container.Name + "» активности «" + descriptor.Key + "» недоступна")
                        .WithElements(elementId);
                }

                if (request.Index.HasValue && request.Index.Value >= 0 && request.Index.Value < target.Count)
                {
                    target.Insert(request.Index.Value, moved);
                }
                else
                {
                    target.Add(moved);
                }
            }
            else
            {
                parent.Properties[container.Name].SetValue(moved);
            }

            Flush();

            return WorkflowEditResult.Success(
                    "move",
                    "перенесено в «" + descriptor.Key + "." + container.Name + "»")
                .WithElements(elementId, address);
        }

        /// <summary>
        /// Оборачивает активность новой активностью (карточка <c>AG-04</c>): обёртка встаёт на место
        /// элемента, элемент становится её первым ребёнком. Так шаг попадает внутрь
        /// <c>If</c>/<c>TryCatch</c>/<c>Sequence</c> со своими аргументами — пересобирать его не нужно.
        /// <para>
        /// Порядок обязателен: сначала элемент вынимается из родителя (одна активность не может лежать
        /// в двух местах дерева), затем его место занимает обёртка, и только потом элемент кладётся
        /// внутрь обёртки. Всё это — один <c>ModelEditingScope</c>, поэтому <c>Ctrl+Z</c> снимает обёртку
        /// целиком, а не «половину».
        /// </para>
        /// </summary>
        private WorkflowEditResult WrapCore(WorkflowWrapRequest request)
        {
            string elementId = request.ElementId.Trim();

            if (string.IsNullOrWhiteSpace(request.WrapperType))
            {
                return WorkflowEditResult.Failure(
                        "wrap",
                        "не задан тип обёртки (wrapperType)",
                        "имена обёрток даёт activity_search: Sequence, If, TryCatch, While")
                    .WithElements(elementId);
            }

            ModelItem item = Resolve(elementId);
            if (item == null)
            {
                return UnknownElement("wrap", elementId);
            }

            if (item.Parent == null)
            {
                return WorkflowEditResult.Failure("wrap", "корень процесса обернуть нельзя: обёртка заняла бы его место")
                    .WithElements(elementId);
            }

            if (!(item.GetCurrentValue() is Activity activity))
            {
                return WorkflowEditResult.Failure("wrap", "«" + elementId + "» — не активность: обернуть нельзя")
                    .WithElements(elementId);
            }

            if (!TryGetSlot(elementId, out ModelItemCollection source, out ModelProperty slot, out string slotError))
            {
                return WorkflowEditResult.Failure("wrap", slotError).WithElements(elementId);
            }

            if (!TrySplitAddress(elementId, out string parentId, out string name, out int? addressIndex, out string addressError))
            {
                return WorkflowEditResult.Failure("wrap", addressError).WithElements(elementId);
            }

            var factory = new ActivityPaletteFactory();
            PaletteBuildResult build = factory.Build(new PaletteActivitySpec(request.WrapperType.Trim()));
            if (!build.Ok)
            {
                return WorkflowEditResult.Failure("wrap", string.Empty)
                    .Merge(build.Errors, build.Hints)
                    .WithElements(elementId);
            }

            PaletteActivityDescriptor wrapper = _registry.Describe(build.Activity.GetType());
            if (wrapper == null)
            {
                return WorkflowEditResult.Failure(
                        "wrap",
                        "тип «" + build.Activity.GetType() + "» не описан в палитре: обёртка не создана")
                    .WithElements(elementId);
            }

            PaletteContainerDescriptor target = DesignerModelSession.ChooseContainer(
                wrapper, request.Container, out string containerError);
            if (target == null)
            {
                return WorkflowEditResult.Failure("wrap", containerError).WithElements(elementId);
            }

            ModelTreeManager treeManager = GetService<ModelTreeManager>();
            if (treeManager == null)
            {
                return WorkflowEditResult.Failure("wrap", "модель конструктора недоступна (ModelTreeManager не найден)")
                    .WithElements(elementId);
            }

            ModelItem wrapperItem = treeManager.CreateModelItem(null, build.Activity);
            if (wrapperItem == null)
            {
                return WorkflowEditResult.Failure("wrap", "обёртка «" + wrapper.Key + "» не создана в модели")
                    .WithElements(elementId);
            }

            int position = addressIndex ?? -1;

            if (source != null)
            {
                source.Remove(item);

                if (position >= 0 && position <= source.Count)
                {
                    source.Insert(position, wrapperItem);
                }
                else
                {
                    source.Add(wrapperItem);
                }
            }
            else
            {
                // Одиночное свойство (Then, Try, Handler): обёртка занимает место элемента,
                // поэтому адрес обёртки совпадает с прежним адресом элемента.
                slot.SetValue(wrapperItem);
                position = -1;
            }

            ModelItem inner = DesignerModelSession.CreateItemForContainer(treeManager, target, activity);
            if (inner == null)
            {
                return WorkflowEditResult.Failure(
                        "wrap",
                        "обёртка «" + wrapper.Key + "» не приняла активность в «" + target.Name + "»")
                    .WithElements(elementId);
            }

            if (target.Kind == PaletteContainerKind.ActivityCollection)
            {
                ModelItemCollection children = wrapperItem.Properties[target.Name]?.Collection;
                if (children == null)
                {
                    return WorkflowEditResult.Failure(
                            "wrap",
                            "коллекция «" + target.Name + "» обёртки «" + wrapper.Key + "» недоступна")
                        .WithElements(elementId);
                }

                children.Add(inner);
            }
            else
            {
                ModelProperty property = wrapperItem.Properties[target.Name];
                if (property == null)
                {
                    return WorkflowEditResult.Failure(
                            "wrap",
                            "свойство «" + target.Name + "» обёртки «" + wrapper.Key + "» недоступно")
                        .WithElements(elementId);
                }

                property.SetValue(inner);
            }

            string displayName = (request.DisplayName ?? string.Empty).Trim();
            if (displayName.Length > 0)
            {
                wrapperItem.Properties["DisplayName"]?.SetValue(displayName);
            }

            string wrapperAddress = source != null
                ? parentId + "/" + name + "[" + position + "]"
                : elementId;
            string innerAddress = InnerAddress(wrapperAddress, target);

            Flush();

            return WorkflowEditResult.Success(
                    "wrap",
                    "обёрнуто в «" + wrapper.Key + "." + target.Name + "»")
                .WithElements(elementId, wrapperAddress)
                .WithApplied(1, new[] { wrapperAddress, innerAddress })
                .AddWarning(
                    innerAddress,
                    "адрес активности изменился: дальше правьте её по «" + innerAddress + "»",
                    "address");
        }

        /// <summary>
        /// Задаёт аргумент активности (карточка <c>AG-05</c>): литерал или выражение.
        /// <para>
        /// Проверки идут до правки модели и в том порядке, в котором агент сможет их исправить:
        /// есть ли такой аргумент у типа → задано ровно одно значение → правила студии для выражения →
        /// синтаксис на языке документа → приведение литерала к типу аргумента (фабрика палитры).
        /// </para>
        /// </summary>
        private WorkflowEditResult SetArgumentCore(WorkflowArgumentSetRequest request)
        {
            string elementId = request.ElementId.Trim();

            ModelItem item = Resolve(elementId);
            if (item == null)
            {
                return UnknownElement("argument_set", elementId);
            }

            PaletteActivityDescriptor descriptor = _registry.Describe(item.ItemType);
            if (descriptor == null)
            {
                return WorkflowEditResult.Failure(
                        "argument_set",
                        "тип «" + item.ItemType + "» не является активностью — аргументов у него нет")
                    .WithElements(elementId);
            }

            PaletteArgumentDescriptor argument = descriptor.FindArgument(request.Argument);
            if (argument == null)
            {
                return WorkflowEditResult.Failure(
                        "argument_set",
                        "у активности «" + descriptor.Key + "» нет аргумента «" + request.Argument.Trim() + "»; " +
                        descriptor.DescribeArguments(),
                        "полный перечень аргументов с типами даёт activity_describe")
                    .WithElements(elementId);
            }

            if (request.HasExpression && request.ValueSpecified)
            {
                return WorkflowEditResult.Failure(
                        "argument_set",
                        "заданы и value, и expression: нужно ровно одно",
                        "value — литерал (\"hello\", 42), expression — выражение (text.ToUpper())")
                    .WithElements(elementId);
            }

            if (!request.HasExpression && !request.ValueSpecified)
            {
                return WorkflowEditResult.Failure(
                        "argument_set",
                        "не задано значение аргумента «" + argument.Name + "»",
                        "передайте value (литерал) или expression (выражение)")
                    .WithElements(elementId);
            }

            PaletteValue value;
            string described;

            if (request.HasExpression)
            {
                WorkflowExpressionLanguage language =
                    WorkflowExpressionLanguageDetector.Resolve(FilePath, request.Language);

                string problems = WorkflowExpressionPolicy.Describe(
                    WorkflowExpressionPolicy.Check(request.Expression, language));

                if (problems.Length > 0)
                {
                    return WorkflowEditResult.Failure("argument_set", "выражение не принято: " + problems)
                        .WithElements(elementId);
                }

                IReadOnlyList<string> syntax = WorkflowExpressionSyntax.Check(request.Expression, language);
                if (syntax.Count > 0)
                {
                    return WorkflowEditResult.Failure("argument_set", string.Join("; ", syntax))
                        .WithElements(elementId);
                }

                value = PaletteValue.FromExpression(
                    request.Expression,
                    request.TypeName,
                    language == WorkflowExpressionLanguage.VisualBasic
                        ? PaletteExpressionLanguage.VisualBasic
                        : PaletteExpressionLanguage.CSharp);

                described = "выражение (" + WorkflowExpressionLanguages.NameOf(language) + "): " + request.Expression.Trim();
            }
            else
            {
                value = PaletteValue.FromLiteral(request.Value, request.TypeName);
                described = "литерал: " + (request.Value ?? "null");
            }

            ModelEditResult applied = _model.SetArgument(elementId, argument.Name, value);
            if (!applied.Ok)
            {
                return WorkflowEditResult.Failure("argument_set", string.Empty)
                    .Merge(applied.Errors, applied.Hints)
                    .WithElements(elementId);
            }

            return WorkflowEditResult.Success(
                    "argument_set",
                    "«" + descriptor.Key + "." + argument.Name + "» = " + described +
                    " (" + argument.DirectionName + ", " + TypeTextOf(argument) + ")")
                .WithElements(elementId);
        }

        /// <summary>
        /// Объявляет переменную (карточка <c>AG-05</c>): тип из списка студии, значение по умолчанию —
        /// литерал или выражение. Переменная сразу попадает в область видимости, поэтому следующие
        /// выражения могут на неё ссылаться.
        /// </summary>
        private WorkflowEditResult AddVariableCore(WorkflowVariableAddRequest request)
        {
            string elementId = request.ElementId;
            string name = request.Name.Trim();

            ModelItem item = Resolve(elementId);
            if (item == null)
            {
                return UnknownElement("variable_add", elementId);
            }

            ModelItemCollection variables = VariablesOf(item);
            if (variables == null)
            {
                return NoVariables("variable_add", elementId);
            }

            List<string> names = VariableNames(variables);

            if (names.Exists(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            {
                return WorkflowEditResult.Failure(
                        "variable_add",
                        "переменная «" + name + "» уже объявлена у «" + elementId + "»",
                        "объявлены: " + DescribeNames(names))
                    .WithElements(elementId);
            }

            if (!IsIdentifier(name))
            {
                return WorkflowEditResult.Failure(
                        "variable_add",
                        "имя переменной «" + name + "» не является идентификатором",
                        "имя начинается с буквы или «_» и состоит из букв, цифр и «_»")
                    .WithElements(elementId);
            }

            if (!PaletteTypeNames.TryResolve(request.TypeName, out Type valueType))
            {
                return WorkflowEditResult.Failure(
                        "variable_add",
                        "тип «" + (request.TypeName ?? string.Empty) + "» неизвестен",
                        "известные типы: " + PaletteTypeNames.KnownNames())
                    .WithElements(elementId);
            }

            if (request.HasExpression && request.ValueSpecified)
            {
                return WorkflowEditResult.Failure(
                        "variable_add",
                        "у переменной «" + name + "» заданы и value, и expression: нужно ровно одно")
                    .WithElements(elementId);
            }

            Activity defaultValue = null;

            if (request.HasExpression || request.ValueSpecified)
            {
                if (!TryCreateDefaultValue(request, valueType, name, out defaultValue, out WorkflowEditResult failure))
                {
                    return failure.WithElements(elementId);
                }
            }

            ModelTreeManager treeManager = GetService<ModelTreeManager>();
            if (treeManager == null)
            {
                return WorkflowEditResult.Failure(
                        "variable_add",
                        "модель конструктора недоступна (ModelTreeManager не найден)")
                    .WithElements(elementId);
            }

            Type variableType = typeof(Variable<>).MakeGenericType(valueType);
            var variable = (Variable)Activator.CreateInstance(variableType);
            variable.Name = name;

            if (defaultValue != null)
            {
                // Default объявлен дважды: LocationReference.Default (Activity) и Variable<T>.Default
                // (Activity<T>) — без DeclaredOnly отражение бросает AmbiguousMatchException.
                variableType
                    .GetProperty(
                        "Default",
                        System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.DeclaredOnly)
                    ?.SetValue(variable, defaultValue);
            }

            ModelItem variableItem = treeManager.CreateModelItem(null, variable);
            if (variableItem == null)
            {
                return WorkflowEditResult.Failure("variable_add", "переменная «" + name + "» не создана в модели")
                    .WithElements(elementId);
            }

            variables.Add(variableItem);
            Flush();

            WorkflowEditResult result = WorkflowEditResult.Success(
                    "variable_add",
                    "переменная «" + name + "»: " + PaletteTypeNames.NameOf(valueType) +
                    " объявлена в «" + elementId + "»" +
                    (defaultValue == null ? " (без значения по умолчанию)" : string.Empty))
                .WithElements(elementId);

            string outerScope = FindOuterScopeWithVariable(item, name);
            if (outerScope != null)
            {
                result.AddWarning(
                    elementId,
                    "переменная «" + name + "» уже объявлена во внешней области («" + outerScope +
                    "»): внутри «" + elementId + "» она перекрывает внешнюю",
                    "shadowing");
            }

            return result;
        }

        /// <summary>Удаляет переменную по имени (карточка <c>AG-05</c>); отсутствие — отказ со списком имён.</summary>
        private WorkflowEditResult RemoveVariableCore(WorkflowVariableRemoveRequest request)
        {
            string elementId = request.ElementId;
            string name = request.Name.Trim();

            ModelItem item = Resolve(elementId);
            if (item == null)
            {
                return UnknownElement("variable_remove", elementId);
            }

            ModelItemCollection variables = VariablesOf(item);
            if (variables == null)
            {
                return NoVariables("variable_remove", elementId);
            }

            ModelItem target = null;

            foreach (ModelItem variable in variables)
            {
                if (string.Equals(VariableNameOf(variable), name, StringComparison.OrdinalIgnoreCase))
                {
                    target = variable;
                    break;
                }
            }

            if (target == null)
            {
                return WorkflowEditResult.Failure(
                        "variable_remove",
                        "переменной «" + name + "» у «" + elementId + "» нет",
                        "объявлены: " + DescribeNames(VariableNames(variables)))
                    .WithElements(elementId);
            }

            variables.Remove(target);
            Flush();

            return WorkflowEditResult.Success(
                    "variable_remove",
                    "переменная «" + name + "» удалена из «" + elementId + "»")
                .WithElements(elementId);
        }

        /// <summary>Коллекция <c>Variables</c> активности или <c>null</c>, если объявлять переменные негде.</summary>
        private static ModelItemCollection VariablesOf(ModelItem item)
        {
            return item?.Properties["Variables"]?.Collection;
        }

        private static List<string> VariableNames(ModelItemCollection variables)
        {
            var names = new List<string>();

            foreach (ModelItem variable in variables)
            {
                string name = VariableNameOf(variable);

                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static string VariableNameOf(ModelItem variable)
        {
            if (variable?.GetCurrentValue() is Variable value)
            {
                return value.Name;
            }

            return variable?.Properties["Name"]?.ComputedValue as string;
        }

        private static string DescribeNames(List<string> names)
        {
            return names == null || names.Count == 0 ? "(ни одной)" : string.Join(", ", names);
        }

        /// <summary>Имя переменной: буква или «_», дальше буквы, цифры и «_» (как требуют C# и VB).</summary>
        private static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!char.IsLetter(name[0]) && name[0] != '_')
            {
                return false;
            }

            foreach (char symbol in name)
            {
                if (!char.IsLetterOrDigit(symbol) && symbol != '_')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Внешняя область, где переменная с этим именем уже объявлена: перекрытие имён WF разрешает,
        /// но агент должен об этом знать — иначе правит «не ту» переменную.
        /// </summary>
        private static string FindOuterScopeWithVariable(ModelItem item, string name)
        {
            for (ModelItem parent = item?.Parent; parent != null; parent = parent.Parent)
            {
                ModelItemCollection variables = VariablesOf(parent);
                if (variables == null)
                {
                    continue;
                }

                foreach (ModelItem variable in variables)
                {
                    if (string.Equals(VariableNameOf(variable), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return parent.ItemType?.Name ?? "родитель";
                    }
                }
            }

            return null;
        }

        /// <summary>Значение по умолчанию переменной: выражение или литерал приведённого типа.</summary>
        private bool TryCreateDefaultValue(
            WorkflowVariableAddRequest request,
            Type valueType,
            string name,
            out Activity defaultValue,
            out WorkflowEditResult failure)
        {
            defaultValue = null;
            failure = null;

            if (request.HasExpression)
            {
                WorkflowExpressionLanguage language =
                    WorkflowExpressionLanguageDetector.Resolve(FilePath, request.Language);

                string problems = WorkflowExpressionPolicy.Describe(
                    WorkflowExpressionPolicy.Check(request.Expression, language));

                if (problems.Length > 0)
                {
                    failure = WorkflowEditResult.Failure("variable_add", "выражение не принято: " + problems);
                    return false;
                }

                IReadOnlyList<string> syntax = WorkflowExpressionSyntax.Check(request.Expression, language);
                if (syntax.Count > 0)
                {
                    failure = WorkflowEditResult.Failure("variable_add", string.Join("; ", syntax));
                    return false;
                }

                try
                {
                    defaultValue = CreateTextExpression(valueType, request.Expression, language);
                    return true;
                }
                catch (Exception ex)
                {
                    failure = WorkflowEditResult.Failure(
                        "variable_add",
                        ErrorText.Describe("значение по умолчанию переменной «" + name + "» не создано", ex));
                    return false;
                }
            }

            try
            {
                object converted = ConvertDefaultLiteral(request.Value, valueType);

                defaultValue = (Activity)Activator.CreateInstance(
                    typeof(System.Activities.Expressions.Literal<>).MakeGenericType(valueType),
                    converted);

                return true;
            }
            catch (Exception ex)
            {
                failure = WorkflowEditResult.Failure(
                    "variable_add",
                    ErrorText.Describe(
                        "значение «" + request.Value + "» не приводится к типу " + PaletteTypeNames.NameOf(valueType),
                        ex),
                    "value задаётся текстом: 42, true, \"текст\"");
                return false;
            }
        }

        /// <summary>Выражение-значение на языке документа: <c>CSharpValue&lt;T&gt;</c> или <c>VisualBasicValue&lt;T&gt;</c>.</summary>
        private static Activity CreateTextExpression(Type valueType, string expression, WorkflowExpressionLanguage language)
        {
            Type openType = language == WorkflowExpressionLanguage.VisualBasic
                ? typeof(Microsoft.VisualBasic.Activities.VisualBasicValue<>)
                : typeof(Microsoft.CSharp.Activities.CSharpValue<>);

            return (Activity)Activator.CreateInstance(openType.MakeGenericType(valueType), expression.Trim());
        }

        private static object ConvertDefaultLiteral(string literal, Type valueType)
        {
            if (valueType == typeof(object))
            {
                return literal;
            }

            if (valueType == typeof(string))
            {
                return literal ?? string.Empty;
            }

            return Convert.ChangeType(literal, valueType, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string TypeTextOf(PaletteArgumentDescriptor argument)
        {
            return argument.ValueType == null
                ? "тип задаётся значением"
                : PaletteTypeNames.NameOf(argument.ValueType);
        }

        private WorkflowEditResult NoVariables(string operation, string elementId)
        {
            return WorkflowEditResult.Failure(
                    operation,
                    "у элемента «" + elementId + "» нет коллекции переменных (Variables)",
                    "переменные объявляются в Sequence, Flowchart или в корне процесса (root)")
                .WithElements(elementId);
        }

        /// <summary>Адрес активности, которая оказалась внутри обёртки, — по правилам <c>workflow_outline</c>.</summary>
        private static string InnerAddress(string wrapperAddress, PaletteContainerDescriptor container)
        {
            switch (container.Kind)
            {
                case PaletteContainerKind.ActivityCollection:
                    return wrapperAddress + "/" + container.Name + "[0]";
                case PaletteContainerKind.ActivityAction:
                    return wrapperAddress + "/" + container.Name + "/Handler";
                default:
                    return wrapperAddress + "/" + container.Name;
            }
        }

        /// <summary>
        /// Адрес элемента, который появится в контейнере: путь того же вида, что отдаёт
        /// <c>workflow_outline</c> (<c>root/Activities[2]</c>, <c>root/Then</c>, <c>root/Body/Handler</c>).
        /// </summary>
        private static string AddressFor(
            PaletteContainerDescriptor container,
            string parentId,
            ModelItem parent,
            int? index)
        {
            string parentPath = string.IsNullOrWhiteSpace(parentId) ? WorkflowEditing.RootElementId : parentId;

            if (container.Kind == PaletteContainerKind.ActivityCollection)
            {
                int count = parent.Properties[container.Name]?.Collection?.Count ?? 0;
                int actual = index.HasValue && index.Value >= 0 && index.Value < count ? index.Value : count;

                return parentPath + "/" + container.Name + "[" + actual + "]";
            }

            return container.Kind == PaletteContainerKind.ActivityAction
                ? parentPath + "/" + container.Name + "/Handler"
                : parentPath + "/" + container.Name;
        }

        /// <summary>
        /// Гнездо элемента по его адресу: последний сегмент пути — свойство родителя
        /// (<c>root/Activities[2]</c> → коллекция <c>Activities</c>, <c>root/Then</c> → свойство <c>Then</c>).
        /// <para>
        /// Так надёжнее, чем <c>ModelItem.Source</c> и сравнение элементов: <c>ModelItem</c> — живая
        /// обёртка, при перечислении коллекции конструктор выдаёт разные экземпляры для одного и того же
        /// элемента, и <c>ReferenceEquals</c> по ним не работает.
        /// </para>
        /// </summary>
        /// <summary>
        /// Разбирает адрес узла на родителя, имя свойства и индекс: <c>root/Activities[2]</c> →
        /// (<c>root</c>, <c>Activities</c>, 2), <c>root/Then</c> → (<c>root</c>, <c>Then</c>, <c>null</c>).
        /// <para>
        /// Разбор один на все правки: адрес, который отдаёт <c>workflow_outline</c>, читается и правится
        /// одним и тем же кодом, поэтому «прочитал один узел, поправил другой» не случается.
        /// </para>
        /// </summary>
        private static bool TrySplitAddress(
            string elementId,
            out string parentId,
            out string name,
            out int? index,
            out string error)
        {
            parentId = null;
            name = null;
            index = null;
            error = null;

            string id = (elementId ?? string.Empty).Trim();

            if (id.Length == 0 || string.Equals(id, WorkflowEditing.RootElementId, StringComparison.OrdinalIgnoreCase))
            {
                error = "адрес «" + elementId + "» — корень процесса: у него нет родительского контейнера";
                return false;
            }

            int slash = id.LastIndexOf('/');
            if (slash <= 0 || slash == id.Length - 1)
            {
                error = "адрес «" + elementId + "» не разобран: ожидается вид root/Activities[0] или root/Then";
                return false;
            }

            parentId = id.Substring(0, slash);
            string segment = id.Substring(slash + 1);

            name = segment;
            int bracket = segment.IndexOf('[');

            if (bracket > 0 && segment.EndsWith("]", StringComparison.Ordinal))
            {
                name = segment.Substring(0, bracket);

                if (!int.TryParse(segment.Substring(bracket + 1, segment.Length - bracket - 2), out int parsed))
                {
                    error = "адрес «" + elementId + "» не разобран: индекс в «" + segment + "» не число";
                    return false;
                }

                index = parsed;
            }

            return true;
        }

        private bool TryGetSlot(
            string elementId,
            out ModelItemCollection collection,
            out ModelProperty slot,
            out string error)
        {
            collection = null;
            slot = null;
            error = null;

            if (!TrySplitAddress(elementId, out string parentId, out string name, out int? index, out error))
            {
                return false;
            }

            ModelItem parent = Resolve(parentId);
            ModelProperty property = parent?.Properties[name];

            if (property == null)
            {
                error = "у элемента «" + parentId + "» нет свойства «" + name + "»: адрес «" + elementId +
                        "» не найден в модели конструктора";
                return false;
            }

            if (index.HasValue)
            {
                collection = property.Collection;

                if (collection == null || index.Value < 0 || index.Value >= collection.Count)
                {
                    error = "в «" + parentId + "." + name + "» нет элемента с индексом " + index.Value;
                    return false;
                }

                return true;
            }

            slot = property;
            return true;
        }

        private void SetDisplayName(string elementId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(elementId))
            {
                return;
            }

            Execute("displayName", () =>
            {
                ModelItem item = Resolve(elementId);
                ModelProperty property = item?.Properties["DisplayName"];

                if (property == null)
                {
                    return WorkflowEditResult.Failure(
                        "displayName",
                        "у элемента «" + elementId + "» нет подписи (DisplayName)").WithElements(elementId);
                }

                property.SetValue(displayName);
                Flush();

                return WorkflowEditResult.Success("displayName", "подпись: " + displayName).WithElements(elementId);
            });
        }

        /// <summary>
        /// Выполняет одну правку на UI-потоке в собственном <c>ModelEditingScope</c>: либо правка
        /// применяется целиком, либо модель остаётся прежней. Исключение и отказ равносильны.
        /// </summary>
        private WorkflowEditResult Execute(string operation, Func<WorkflowEditResult> action)
        {
            try
            {
                return _ui.Invoke(() =>
                {
                    ModelItem root = GetService<ModelService>()?.Root;
                    if (root == null)
                    {
                        return WorkflowEditResult.Failure(
                            operation,
                            "модель конструктора недоступна (ModelService не найден)")
                            .WithState(IsActive);
                    }

                    ModelEditingScope scope = root.BeginEdit(Label + ": " + operation);
                    WorkflowEditResult result;

                    try
                    {
                        result = action();
                    }
                    catch (Exception ex)
                    {
                        SafeRevert(scope);

                        return WorkflowEditResult.Failure(
                                operation,
                                ErrorText.Describe("правка «" + operation + "» не выполнена", ex))
                            .WithState(IsActive);
                    }

                    if (!result.Ok)
                    {
                        SafeRevert(scope);
                        return result.WithState(IsActive);
                    }

                    scope.Complete();
                    _changes++;
                    Flush();

                    return result.WithState(IsActive);
                });
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(
                        operation,
                        ErrorText.Describe("правка «" + operation + "» не выполнена", ex),
                        "UI-поток студии занят: закройте модальные окна конструктора и повторите")
                    .WithState(IsActive);
            }
        }

        private static void SafeRevert(ModelEditingScope scope)
        {
            try
            {
                scope.Revert();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Правка не отменена: " + ErrorText.Of(ex));
            }
        }

        private ModelItem Resolve(string elementId)
        {
            ModelService modelService = GetService<ModelService>();
            ModelTreeManager treeManager = GetService<ModelTreeManager>();

            if (modelService?.Root == null || treeManager == null)
            {
                return null;
            }

            return DesignerModelSession.ResolveItem(modelService.Root, treeManager, elementId);
        }

        private T GetService<T>() where T : class
        {
            return _designer?.Context?.Services?.GetService<T>();
        }

        /// <summary>Сообщает конструктору о завершении правки (обновление диаграммы и валидации).</summary>
        private void Flush()
        {
            try
            {
                _designer.Flush();
            }
            catch (Exception ex)
            {
                // Flush — сервисная операция: её сбой не должен отменять уже сделанную правку модели.
                System.Diagnostics.Debug.WriteLine("Flush конструктора не выполнен: " + ErrorText.Of(ex));
            }
        }

        private WorkflowEditResult Inactive(string operation)
        {
            return WorkflowEditResult.Failure(
                    operation,
                    _inactiveReason ?? "пачка правок закрыта: начните новую (BeginEdit)",
                    "откройте файл процесса вкладкой конструктора и начните пачку заново")
                .WithState(false);
        }

        private static WorkflowEditResult NotRequested(string operation, string message)
        {
            return WorkflowEditResult.Failure(operation, message).WithState(false);
        }

        private WorkflowEditResult UnknownElement(string operation, string elementId)
        {
            return WorkflowEditResult.Failure(
                    operation,
                    "элемент «" + elementId + "» не найден в модели конструктора",
                    "адреса берутся из workflow_outline (root/Activities[0], root/Then)")
                .WithElements(elementId);
        }
    }
}
