using System;
using System.Activities;
using System.Activities.Presentation;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Правка модели в живом конструкторе через <c>ModelService</c>/<c>ModelItem</c>
    /// (этап 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>, п. 3).
    /// <para>
    /// Почему модель, а не текст XAML: ViewState и <c>sap2010:WorkflowViewState.IdRef</c> проставляет сам
    /// конструктор, поэтому вставленная активность сразу видна на диаграмме и остаётся редактируемой руками.
    /// Сохранение — штатный <see cref="WorkflowDesigner.Save(string)"/>: тот же сериализатор, что у студии.
    /// </para>
    /// <para>
    /// Работать с этим классом можно только на UI-потоке дизайнера: <see cref="PaletteWorkflowService"/>
    /// вызывает его через <see cref="IDesignerUiThread"/>.
    /// </para>
    /// </summary>
    internal sealed class DesignerModelSession : IWorkflowModelSession
    {
        private readonly WorkflowDesignerEx _designer;
        private readonly ActivityPaletteFactory _factory;

        internal DesignerModelSession(WorkflowDesignerEx designer, ActivityPaletteFactory factory = null)
        {
            _designer = designer ?? throw new ArgumentNullException(nameof(designer));
            _factory = factory ?? new ActivityPaletteFactory();
        }

        public WorkflowEditResult InsertActivity(
            PaletteActivitySpec spec,
            string parentActivityId,
            string containerName,
            int? index)
        {
            if (spec == null)
            {
                return WorkflowEditResult.Failure(new[] { "xaml.activity: не задана активность для вставки" });
            }

            try
            {
                ModelTreeManager treeManager = GetService<ModelTreeManager>();
                ModelService modelService = GetService<ModelService>();

                if (modelService?.Root == null || treeManager == null)
                {
                    return WorkflowEditResult.Failure(
                        new[] { "модель конструктора недоступна (ModelService/ModelTreeManager не найдены)" },
                        new[] { "перезагрузите вкладку процесса и повторите вызов" });
                }

                PaletteBuildResult build = _factory.Build(spec);
                if (!build.Ok)
                {
                    return WorkflowEditResult.Failure(build.Errors, build.Hints);
                }

                ModelItem parent = ResolveItem(modelService.Root, treeManager, parentActivityId);
                if (parent == null)
                {
                    return WorkflowEditResult.Failure(
                        new[] { $"активность «{parentActivityId}» не найдена в модели конструктора" },
                        new[] { "идентификаторы берутся из xaml.get_workflow; для корня процесса оставьте parentId пустым" });
                }

                PaletteActivityDescriptor parentDescriptor = _factory.Registry.Describe(parent.ItemType);
                if (parentDescriptor == null)
                {
                    return WorkflowEditResult.Failure(
                        new[] { $"тип «{parent.ItemType}» не является активностью — вложить нельзя" });
                }

                PaletteContainerDescriptor container = ChooseContainer(parentDescriptor, containerName, out string containerError);
                if (container == null)
                {
                    return WorkflowEditResult.Failure(new[] { containerError });
                }

                ModelItem item = CreateItemForContainer(treeManager, container, build.Activity);
                if (item == null)
                {
                    return WorkflowEditResult.Failure(
                        new[] { $"активность «{parentDescriptor.Key}» не приняла вложенную активность в «{container.Name}»" });
                }

                if (container.Kind == PaletteContainerKind.ActivityCollection)
                {
                    ModelItemCollection collection = parent.Properties[container.Name]?.Collection;
                    if (collection == null)
                    {
                        return WorkflowEditResult.Failure(
                            new[] { $"коллекция «{container.Name}» активности «{parentDescriptor.Key}» недоступна" });
                    }

                    if (index.HasValue && index.Value >= 0 && index.Value < collection.Count)
                    {
                        collection.Insert(index.Value, item);
                    }
                    else
                    {
                        collection.Add(item);
                    }
                }
                else
                {
                    parent.Properties[container.Name].SetValue(item);
                }

                Flush();

                return WorkflowEditResult.Success(
                    build.Activity.Id,
                    new[] { $"вставлено в «{parentDescriptor.Key}.{container.Name}»" });
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(new[]
                {
                    ErrorText.Describe($"активность «{spec.ActivityType}» не вставлена в конструктор", ex)
                });
            }
        }

        public WorkflowEditResult SetArgument(string activityId, string argumentName, PaletteValue value)
        {
            try
            {
                ModelTreeManager treeManager = GetService<ModelTreeManager>();
                ModelService modelService = GetService<ModelService>();

                if (modelService?.Root == null || treeManager == null)
                {
                    return WorkflowEditResult.Failure(
                        new[] { "модель конструктора недоступна (ModelService/ModelTreeManager не найдены)" });
                }

                ModelItem item = ResolveItem(modelService.Root, treeManager, activityId);
                if (item == null)
                {
                    return WorkflowEditResult.Failure(
                        new[] { $"активность «{activityId}» не найдена в модели конструктора" },
                        new[] { "идентификаторы берутся из xaml.get_workflow" });
                }

                PaletteActivityDescriptor descriptor = _factory.Registry.Describe(item.ItemType);
                if (!_factory.TryCreateArgumentValue(
                        descriptor, argumentName, value, out object argumentValue, out List<string> errors, out List<string> hints))
                {
                    return WorkflowEditResult.Failure(errors, hints);
                }

                PaletteArgumentDescriptor argument = descriptor.FindArgument(argumentName);
                ModelProperty property = item.Properties[argument.Name];
                if (property == null)
                {
                    return WorkflowEditResult.Failure(
                        new[] { $"свойство «{argument.Name}» недоступно у активности «{descriptor.Key}»" });
                }

                property.SetValue(argumentValue);
                Flush();

                return WorkflowEditResult.Success(activityId);
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(new[]
                {
                    ErrorText.Describe($"аргумент «{argumentName}» не задан", ex)
                });
            }
        }

        public WorkflowEditResult Save(string filePath)
        {
            try
            {
                _designer.Save(filePath);

                // Файл и модель совпадают: «изменён» снимается, как после сохранения самой студией.
                _designer.IsEdited = false;

                return WorkflowEditResult.Success();
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(new[]
                {
                    ErrorText.Describe($"модель не сохранена в {filePath}", ex)
                });
            }
        }

        /// <summary>
        /// Откат последней правки: <see cref="UndoEngine"/> конструктора + сохранение. Если откатить
        /// нельзя, файл остаётся в состоянии конструктора — об этом сообщает ошибка.
        /// </summary>
        public WorkflowEditResult Rollback(string filePath)
        {
            UndoEngine undoEngine = GetService<UndoEngine>();
            if (undoEngine == null)
            {
                return WorkflowEditResult.Failure(
                    new[] { "откат невозможен: UndoEngine конструктора недоступен" });
            }

            try
            {
                undoEngine.Undo();
                return Save(filePath);
            }
            catch (Exception ex)
            {
                return WorkflowEditResult.Failure(new[]
                {
                    ErrorText.Describe($"откат правки в {filePath} не выполнен", ex)
                });
            }
        }

        private T GetService<T>() where T : class
        {
            return _designer?.Context?.Services?.GetService<T>();
        }

        /// <summary>Сообщает конструктору о завершении правки (обновление панелей и валидации).</summary>
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

        /// <summary>
        /// ModelItem по пути из <c>xaml.get_workflow</c> (<c>root/Activities[1]/Then</c>).
        /// Корень процесса с <c>x:Class</c> — это <c>ActivityBuilder</c>, поэтому работа идёт с его
        /// <c>Implementation</c> (если реализации нет, создаётся пустой <see cref="System.Activities.Statements.Sequence"/>).
        /// </summary>
        private static ModelItem ResolveItem(ModelItem rootItem, ModelTreeManager treeManager, string activityId)
        {
            ModelItem current = ImplementationItem(rootItem, treeManager);
            if (current == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(activityId)
                || string.Equals(activityId.Trim(), WorkflowModelReader.RootId, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            string[] segments = activityId.Trim().Split('/');
            for (int position = 1; position < segments.Length; position++)
            {
                string segment = segments[position];
                if (string.IsNullOrWhiteSpace(segment))
                {
                    continue;
                }

                string name = segment;
                int? index = null;

                int bracket = segment.IndexOf('[');
                if (bracket > 0 && segment.EndsWith("]", StringComparison.Ordinal))
                {
                    name = segment.Substring(0, bracket);
                    if (!int.TryParse(segment.Substring(bracket + 1, segment.Length - bracket - 2), out int parsed))
                    {
                        return null;
                    }

                    index = parsed;
                }

                ModelProperty property = current.Properties[name];
                if (property == null)
                {
                    return null;
                }

                if (index.HasValue)
                {
                    ModelItemCollection collection = property.Collection;
                    if (collection == null || index.Value < 0 || index.Value >= collection.Count)
                    {
                        return null;
                    }

                    current = collection[index.Value];
                }
                else
                {
                    current = property.Value;
                }

                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }

        private static ModelItem ImplementationItem(ModelItem rootItem, ModelTreeManager treeManager)
        {
            if (rootItem == null)
            {
                return null;
            }

            if (!(rootItem.GetCurrentValue() is ActivityBuilder))
            {
                return rootItem;
            }

            ModelProperty property = rootItem.Properties["Implementation"];
            if (property == null)
            {
                return null;
            }

            if (property.Value == null)
            {
                ModelItem sequence = treeManager.CreateModelItem(null, new System.Activities.Statements.Sequence());
                property.SetValue(sequence);
            }

            return property.Value;
        }

        /// <summary>
        /// Контейнер для вставки: явно названный (<c>container</c>) либо единственный подходящий.
        /// Если подходящих несколько, выбор не угадывается — возвращается ошибка со списком.
        /// </summary>
        private static PaletteContainerDescriptor ChooseContainer(
            PaletteActivityDescriptor descriptor,
            string containerName,
            out string error)
        {
            error = null;

            if (!string.IsNullOrWhiteSpace(containerName))
            {
                PaletteContainerDescriptor explicitContainer = descriptor.FindContainer(containerName);
                if (explicitContainer == null)
                {
                    error = $"у активности «{descriptor.Key}» нет контейнера «{containerName}»; " +
                            descriptor.DescribeContainers();
                    return null;
                }

                if (explicitContainer.NeedsFlowStep)
                {
                    error = $"контейнер «{explicitContainer.Name}» активности «{descriptor.Key}» " +
                            "в базовом MVP не поддерживается";
                    return null;
                }

                return explicitContainer;
            }

            PaletteContainerDescriptor[] suitable = descriptor.Containers
                .Where(container => !container.NeedsFlowStep)
                .ToArray();

            if (suitable.Length == 1)
            {
                return suitable[0];
            }

            if (suitable.Length == 0)
            {
                error = $"активность «{descriptor.Key}» не принимает дочерние активности";
                return null;
            }

            error = $"у активности «{descriptor.Key}» несколько контейнеров — укажите container: " +
                    string.Join(", ", suitable.Select(container => container.Name));
            return null;
        }

        /// <summary>
        /// ModelItem для вставки. Для обработчиков (<c>ForEach.Body</c>) оборачиваем активность
        /// в <c>ActivityAction</c>: свойство ждёт обработчик, а не активность.
        /// </summary>
        private static ModelItem CreateItemForContainer(
            ModelTreeManager treeManager,
            PaletteContainerDescriptor container,
            Activity activity)
        {
            if (container.Kind != PaletteContainerKind.ActivityAction)
            {
                return treeManager.CreateModelItem(null, activity);
            }

            object action = CreateActivityAction(container, activity);
            return action == null ? null : treeManager.CreateModelItem(null, action);
        }

        private static object CreateActivityAction(PaletteContainerDescriptor container, Activity child)
        {
            if (container.ChildValueType == null)
            {
                return new ActivityAction { Handler = child };
            }

            Type actionType = typeof(ActivityAction<>).MakeGenericType(container.ChildValueType);
            object action = Activator.CreateInstance(actionType);

            Type delegateType = typeof(DelegateInArgument<>).MakeGenericType(container.ChildValueType);
            object delegateArgument = Activator.CreateInstance(delegateType, "item");

            actionType.GetProperty("Argument", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(action, delegateArgument);
            actionType.GetProperty("Handler", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(action, child);

            return action;
        }
    }
}
