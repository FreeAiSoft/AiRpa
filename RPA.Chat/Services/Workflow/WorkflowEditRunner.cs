using System;
using System.Collections.Generic;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Что делать с валидацией после правки (карточки <c>AG-04</c> и <c>AG-09</c>).
    /// </summary>
    public enum WorkflowEditValidation
    {
        /// <summary>Не проверять: быстрый режим, когда агент правит заведомо неполный процесс.</summary>
        Off,

        /// <summary>
        /// Проверить и показать новые ошибки, но правку оставить: шаг, у которого ещё не заданы аргументы,
        /// валидацию не проходит — откат сделал бы сборку робота невозможной.
        /// </summary>
        Report,

        /// <summary>Проверить и откатить правку, если появились новые ошибки валидации.</summary>
        Strict
    }

    /// <summary>
    /// Выполнение инструментов правки (карточка <c>AG-04</c>) с атомарностью и валидацией (карточка <c>AG-09</c>).
    /// <para>
    /// Один вызов инструмента — одна атомарная пачка: <c>BeginEdit</c> → правки → валидация →
    /// <c>Complete</c> либо <c>Revert</c>. Отдельная правка внутри пачки отменяет <b>всю</b> пачку: агент,
    /// попросивший «вставь три шага», не должен получить «два вставлено, третий не принялся», иначе
    /// следующая правка пойдёт по изменившимся адресам.
    /// </para>
    /// <para>
    /// Логика живёт здесь, а не в инструментах: так она проверяется на фейковой сессии без конструктора
    /// и одна и та же для всех четырёх операций. Текста ответа здесь нет — его собирает
    /// <see cref="WorkflowEditText"/>.
    /// </para>
    /// </summary>
    public sealed class WorkflowEditRunner
    {
        private readonly IWorkflowEditingService _service;

        public WorkflowEditRunner(IWorkflowEditingService service)
        {
            _service = service ?? NullWorkflowEditingService.Instance;
        }

        /// <summary>Вставляет активности по порядку в один контейнер (или в разные — адрес у каждого свой).</summary>
        public WorkflowEditResult Insert(
            string path,
            IReadOnlyList<WorkflowInsertRequest> requests,
            WorkflowEditValidation validation = WorkflowEditValidation.Report,
            string label = null)
        {
            return Run(path, "insert", validation, label, requests?.Count ?? 0,
                session => Apply(session, requests, (s, request) => s.Insert(request)));
        }

        /// <summary>Удаляет активности по адресам из структуры.</summary>
        public WorkflowEditResult Delete(
            string path,
            IReadOnlyList<string> elementIds,
            WorkflowEditValidation validation = WorkflowEditValidation.Report,
            string label = null)
        {
            return Run(path, "delete", validation, label, elementIds?.Count ?? 0,
                session => Apply(session, elementIds, (s, elementId) => s.Delete(elementId)));
        }

        /// <summary>Переносит активности в другие контейнеры.</summary>
        public WorkflowEditResult Move(
            string path,
            IReadOnlyList<WorkflowMoveRequest> requests,
            WorkflowEditValidation validation = WorkflowEditValidation.Report,
            string label = null)
        {
            return Run(path, "move", validation, label, requests?.Count ?? 0,
                session => Apply(session, requests, (s, request) => s.Move(request)));
        }

        /// <summary>Оборачивает активности новыми активностями.</summary>
        public WorkflowEditResult Wrap(
            string path,
            IReadOnlyList<WorkflowWrapRequest> requests,
            WorkflowEditValidation validation = WorkflowEditValidation.Report,
            string label = null)
        {
            return Run(path, "wrap", validation, label, requests?.Count ?? 0,
                session => Apply(session, requests, (s, request) => s.Wrap(request)));
        }

        /// <summary>
        /// Задаёт аргументы активностей (карточка <c>AG-05</c>): пачка «пройди по шагам и заполни
        /// аргументы» — одна атомарная правка, поэтому шаг без части аргументов не остаётся на диаграмме.
        /// </summary>
        public WorkflowEditResult SetArgument(
            string path,
            IReadOnlyList<WorkflowArgumentSetRequest> requests,
            WorkflowEditValidation validation = WorkflowEditValidation.Report,
            string label = null)
        {
            return Run(path, "argument_set", validation, label, requests?.Count ?? 0,
                session => Apply(session, requests, (s, request) => s.SetArgument(request), addressFromElement: true));
        }

        /// <summary>Добавляет переменные в активности процесса (карточка <c>AG-05</c>).</summary>
        public WorkflowEditResult AddVariable(
            string path,
            IReadOnlyList<WorkflowVariableAddRequest> requests,
            WorkflowEditValidation validation = WorkflowEditValidation.Report,
            string label = null)
        {
            return Run(path, "variable_add", validation, label, requests?.Count ?? 0,
                session => Apply(session, requests, (s, request) => s.AddVariable(request), addressFromElement: true));
        }

        /// <summary>Удаляет переменные по именам (карточка <c>AG-05</c>).</summary>
        public WorkflowEditResult RemoveVariable(
            string path,
            IReadOnlyList<WorkflowVariableRemoveRequest> requests,
            WorkflowEditValidation validation = WorkflowEditValidation.Report,
            string label = null)
        {
            return Run(path, "variable_remove", validation, label, requests?.Count ?? 0,
                session => Apply(session, requests, (s, request) => s.RemoveVariable(request), addressFromElement: true));
        }

        /// <summary>Отменяет последнюю закрытую правку конструктора (как <c>Ctrl+Z</c>).</summary>
        public WorkflowEditResult Undo(string path)
        {
            return Guard("undo", path, service => service.Undo(path));
        }

        /// <summary>Возвращает отменённую правку конструктора (как <c>Ctrl+Y</c>).</summary>
        public WorkflowEditResult Redo(string path)
        {
            return Guard("redo", path, service => service.Redo(path));
        }

        /// <summary>Структура процесса из живой модели (несохранённые правки включены).</summary>
        public WorkflowOutline Outline(string path, int depth = 0, bool includeArguments = true)
        {
            try
            {
                return _service.GetLiveOutline(path, depth, includeArguments);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Структура открытого процесса не прочитана: " + ex.Message);
                return null;
            }
        }

        private WorkflowEditResult Run(
            string path,
            string operation,
            WorkflowEditValidation validation,
            string label,
            int requestCount,
            Func<IWorkflowEditSession, BatchOutcome> apply)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return WorkflowEditResult.Failure(
                        operation,
                        "не задан путь к процессу (path)",
                        "передайте путь к XAML процесса, открытому вкладкой конструктора")
                    .WithState(false);
            }

            if (requestCount == 0)
            {
                return WorkflowEditResult.Failure(operation, "не задан ни один элемент для правки")
                    .WithState(false);
            }

            IWorkflowEditSession session;

            try
            {
                session = _service.BeginEdit(path, label);
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(
                    operation,
                    "пачка правок не открыта: " + ex.Message,
                    "проверьте, что файл процесса открыт вкладкой конструктора");
            }

            if (session == null)
            {
                return WorkflowEditResult.Failure(operation, "мост правки конструктора не вернул пачку")
                    .WithState(false);
            }

            try
            {
                if (!session.IsActive)
                {
                    // Неактивная сессия отвечает причиной отказа на каждую правку — пусть скажет сама.
                    BatchOutcome rejected = apply(session);
                    return rejected.Failure ?? WorkflowEditResult
                        .Failure(operation, "пачка правок недоступна")
                        .WithState(false);
                }

                WorkflowOutline baseline = validation == WorkflowEditValidation.Off ? null : ReadOutline(session);
                BatchOutcome outcome = apply(session);

                if (outcome.Failure != null)
                {
                    return Abort(session, operation, outcome);
                }

                // validate=off: результат правки не проверяется, поэтому и «валидации» в ответе нет
                // (null печатается как «не проверялась» — иначе агент считает, что проверка была).
                WorkflowValidationDelta delta = null;

                if (validation != WorkflowEditValidation.Off)
                {
                    delta = WorkflowValidationDelta.Compare(baseline, ReadOutline(session));

                    if (validation == WorkflowEditValidation.Strict && delta.HasNewErrors)
                    {
                        return RevertOnValidationErrors(session, operation, outcome, delta);
                    }
                }

                WorkflowEditResult complete = session.Complete();
                if (complete == null)
                {
                    return WorkflowEditResult.Failure(operation, "пачка не закрыта: конструктор не вернул ответ");
                }

                complete
                    .WithMessage(Summarize(operation, outcome.Applied))
                    .WithApplied(outcome.Applied, outcome.Addresses)
                    .WithValidation(delta)
                    .WithElements(outcome.LastElementId ?? outcome.Failure?.ElementId, outcome.Addresses.Count > 0
                        ? outcome.Addresses[outcome.Addresses.Count - 1]
                        : null);

                return complete;
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(
                        operation,
                        "правка «" + operation + "» не выполнена: " + ex.Message,
                        "модель конструктора могла остаться без правки — проверьте структуру через workflow_outline")
                    .WithState(false);
            }
            finally
            {
                // Незакрытая пачка не оставляет правок: инструмент всегда закрывает её сам.
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Пачка правок не освобождена: " + ex.Message);
                }
            }
        }

        /// <summary>Отказ правки: пачка отменяется целиком, модель возвращается к состоянию до вызова.</summary>
        private static WorkflowEditResult Abort(IWorkflowEditSession session, string operation, BatchOutcome outcome)
        {
            WorkflowEditResult failure = outcome.Failure;
            bool reverted = false;

            try
            {
                reverted = session.Revert()?.Ok == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Пачка не отменена: " + ex.Message);
            }

            failure
                .WithMessage(outcome.Applied == 0
                    ? "правка не применена: модель не изменилась"
                    : "правка " + (outcome.Applied + 1) + " не применена: пачка отменена целиком")
                .WithApplied(outcome.Applied, outcome.Addresses)
                .WithState(false);

            failure.AddHint(reverted
                ? "модель вернулась к состоянию до вызова: исправьте запрос и повторите"
                : "пачка могла остаться открытой — проверьте структуру процесса");

            return failure;
        }

        /// <summary>Строгий режим: правка отменяется, если она добавила ошибки валидации.</summary>
        private static WorkflowEditResult RevertOnValidationErrors(
            IWorkflowEditSession session,
            string operation,
            BatchOutcome outcome,
            WorkflowValidationDelta delta)
        {
            bool reverted = false;

            try
            {
                reverted = session.Revert()?.Ok == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Пачка не отменена: " + ex.Message);
            }

            WorkflowEditResult result = WorkflowEditResult.Failure(
                    operation,
                    "правка отменена: новых ошибок валидации " + delta.NewErrors.Count,
                    "исправьте аргументы и повторите правку либо используйте validate=report, " +
                    "чтобы оставить правку и получить адреса ошибок")
                .WithState(false)
                .WithValidation(delta);

            foreach (WorkflowOutlineIssue issue in delta.NewErrors)
            {
                result.AddError(issue.ElementId, issue.Message, issue.Code ?? "validation");
            }

            if (!reverted)
            {
                result.AddHint("пачка могла остаться открытой — проверьте структуру процесса");
            }

            return result;
        }

        /// <summary>
        /// Применяет пачку правок по порядку; первая же неудача прекращает пачку (её отменит вызывающий).
        /// <para>
        /// <paramref name="addressFromElement"/> — у правок, которые меняют <b>существующий</b> элемент
        /// (<c>argument_set</c>), адресом результата служит сам элемент: новых адресов у такой правки нет,
        /// а агенту всё равно нужно знать, куда она попала.
        /// </para>
        /// </summary>
        private static BatchOutcome Apply<T>(
            IWorkflowEditSession session,
            IReadOnlyList<T> items,
            Func<IWorkflowEditSession, T, WorkflowEditResult> action,
            bool addressFromElement = false)
        {
            var outcome = new BatchOutcome();

            if (items == null)
            {
                return outcome;
            }

            foreach (T item in items)
            {
                WorkflowEditResult result = action(session, item);

                if (result == null || !result.Ok)
                {
                    outcome.Failure = result ?? WorkflowEditResult.Failure("edit", "правка не вернула результат");
                    return outcome;
                }

                outcome.Applied++;
                outcome.LastElementId = result.ElementId;

                foreach (string address in result.Addresses)
                {
                    outcome.Addresses.Add(address);
                }

                if (result.Addresses.Count == 0 && !string.IsNullOrWhiteSpace(result.NewElementId))
                {
                    outcome.Addresses.Add(result.NewElementId);
                }
                else if (addressFromElement
                         && result.Addresses.Count == 0
                         && !string.IsNullOrWhiteSpace(result.ElementId))
                {
                    outcome.Addresses.Add(result.ElementId);
                }
            }

            return outcome;
        }

        private static WorkflowOutline ReadOutline(IWorkflowEditSession session)
        {
            try
            {
                // Валидации структура нужна без аргументов: замечания приходят к узлам, а дерево
                // с выражениями стоит дорого в контексте агента.
                return session.Outline(0, includeArguments: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Структура процесса не прочитана: " + ex.Message);
                return null;
            }
        }

        private static string Summarize(string operation, int applied)
        {
            switch (operation)
            {
                case "insert":
                    return "вставлено активностей: " + applied;
                case "delete":
                    return "удалено: " + applied;
                case "move":
                    return "перенесено: " + applied;
                case "wrap":
                    return "обёрнуто: " + applied;
                case "argument_set":
                    return "аргументов задано: " + applied;
                case "variable_add":
                    return "переменных добавлено: " + applied;
                case "variable_remove":
                    return "переменных удалено: " + applied;
                default:
                    return "правок: " + applied;
            }
        }

        private WorkflowEditResult Guard(
            string operation,
            string path,
            Func<IWorkflowEditingService, WorkflowEditResult> action)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return WorkflowEditResult.Failure(operation, "не задан путь к процессу (path)").WithState(false);
            }

            try
            {
                return action(_service)
                       ?? WorkflowEditResult.Failure(operation, "мост правки конструктора не вернул ответ")
                           .WithState(false);
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(operation, "«" + operation + "» не выполнено: " + ex.Message);
            }
        }

        /// <summary>Сколько правок применилось и по каким адресам — до валидации и закрытия пачки.</summary>
        private sealed class BatchOutcome
        {
            public int Applied { get; set; }

            public List<string> Addresses { get; } = new List<string>();

            /// <summary>Отказ: первая неприменившаяся правка, <c>null</c> — все применились.</summary>
            public WorkflowEditResult Failure { get; set; }

            public string LastElementId { get; set; }
        }
    }
}
