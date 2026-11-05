using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RPA.Chat.Services.Workflow;

namespace RPA.Chat.Services.Spec
{
    /// <summary>Одна операция плана генерации: что вставить, куда и какой аргумент заполнить (<c>TZ-07</c>).</summary>
    public sealed class SpecGenerationOperation
    {
        public string TaskId { get; set; } = string.Empty;

        public string ActivityType { get; set; } = string.Empty;

        public string Parent { get; set; } = "root";

        public string Container { get; set; } = string.Empty;

        public string ArgumentName { get; set; } = string.Empty;

        public string ArgumentValue { get; set; } = string.Empty;

        public override string ToString()
        {
            string text = ActivityType + " → " + Parent;
            if (!string.IsNullOrWhiteSpace(Container))
            {
                text += "." + Container;
            }

            if (!string.IsNullOrWhiteSpace(ArgumentName))
            {
                text += " · " + ArgumentName + " = " + ArgumentValue;
            }

            return text;
        }
    }

    /// <summary>
    /// План генерации скелета робота из ТЗ (<c>TZ-07</c>, шаг 1): «что будет сделано» до правок
    /// плюс «требует решения» — пункты без задач, задачи без шагов, открытые неоднозначности,
    /// активности, которых нет в каталоге.
    /// </summary>
    public sealed class SpecGenerationPlan
    {
        public string WorkflowPath { get; set; } = string.Empty;

        public List<SpecGenerationOperation> Operations { get; } = new List<SpecGenerationOperation>();

        /// <summary>Что требует решения человека/агента: без этого генерация будет неполной.</summary>
        public List<string> NeedsDecision { get; } = new List<string>();

        /// <summary>Уже сгенерировано ранее — повторный прогон не дублирует.</summary>
        public List<string> Skipped { get; } = new List<string>();

        public bool Ready => Operations.Count > 0;

        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("план генерации: операций ").Append(Operations.Count)
                .Append(", пропущено (уже есть) ").Append(Skipped.Count)
                .Append(", требует решения ").Append(NeedsDecision.Count);

            foreach (var operation in Operations)
            {
                text.AppendLine().Append("- [").Append(operation.TaskId).Append("] ").Append(operation);
            }

            if (Skipped.Count > 0)
            {
                text.AppendLine().AppendLine("пропущено:");
                foreach (string item in Skipped)
                {
                    text.Append("- ").AppendLine(item);
                }
            }

            if (NeedsDecision.Count > 0)
            {
                text.AppendLine().AppendLine("требует решения:");
                foreach (string item in NeedsDecision)
                {
                    text.Append("- ").AppendLine(item);
                }
            }

            return text.ToString().TrimEnd();
        }
    }

    /// <summary>Итог применения плана: что создано, где, сколько ошибок валидации, что не сделано.</summary>
    public sealed class SpecGenerationResult
    {
        public bool Ok { get; set; }

        public string Error { get; set; } = string.Empty;

        public List<string> Created { get; } = new List<string>();

        /// <summary>Адреса созданных элементов по задачам — из них заполняются <c>SpecTask.ElementIds</c>.</summary>
        public Dictionary<string, List<string>> TaskElements { get; } =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public List<string> ArgumentsSet { get; } = new List<string>();

        public List<string> Skipped { get; } = new List<string>();

        public List<string> NeedsDecision { get; } = new List<string>();

        public int Errors { get; set; }

        public string ValidationText { get; set; } = string.Empty;

        /// <summary>Ответ инструмента. «Не выполнено» и «ошибок нет» — разные фразы.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            if (!Ok)
            {
                text.Append("генерация не выполнена: ").AppendLine(string.IsNullOrWhiteSpace(Error)
                    ? "причина не указана"
                    : Error);
            }
            else
            {
                text.Append("создано элементов: ").Append(Created.Count)
                    .Append(", аргументов задано: ").Append(ArgumentsSet.Count)
                    .Append(", ошибок валидации: ").Append(Errors).AppendLine();
            }

            if (Created.Count > 0)
            {
                text.AppendLine("создано:");
                foreach (string address in Created)
                {
                    text.Append("- ").AppendLine(address);
                }
            }

            if (ArgumentsSet.Count > 0)
            {
                text.AppendLine("аргументы:");
                foreach (string item in ArgumentsSet)
                {
                    text.Append("- ").AppendLine(item);
                }
            }

            if (Skipped.Count > 0)
            {
                text.AppendLine("пропущено (уже есть):");
                foreach (string item in Skipped)
                {
                    text.Append("- ").AppendLine(item);
                }
            }

            if (NeedsDecision.Count > 0)
            {
                text.AppendLine("требует решения:");
                foreach (string item in NeedsDecision)
                {
                    text.Append("- ").AppendLine(item);
                }
            }

            if (!string.IsNullOrWhiteSpace(ValidationText))
            {
                text.AppendLine().Append("валидация: ").Append(ValidationText);
            }

            return text.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Генерация скелета робота из ТЗ (<c>TZ-07</c>) поверх инструментов правки <c>AG-04</c>/<c>AG-05</c>:
    /// пачка = одна атомарная правка (<see cref="WorkflowEditRunner"/>), после пачки — валидация,
    /// при ошибке пачка откатывается целиком.
    /// </summary>
    public static class SpecGenerator
    {
        /// <summary>
        /// Строит план: операции по задачам, пропуски (уже сгенерировано) и пункты «требует решения».
        /// </summary>
        public static SpecGenerationPlan BuildPlan(RpaSpecification specification, WorkflowOutline outline,
            IActivityCatalog catalog, string workflowPath = null)
        {
            var plan = new SpecGenerationPlan { WorkflowPath = workflowPath ?? string.Empty };

            if (specification == null)
            {
                plan.NeedsDecision.Add("модель ТЗ не собрана — сначала spec_extract");
                return plan;
            }

            specification.Normalize();

            if (outline == null || outline.Root == null)
            {
                plan.NeedsDecision.Add("нет открытого процесса: откройте XAML в конструкторе или укажите path");
            }

            var knownElements = new HashSet<string>(
                outline?.All().Select(node => node.Id) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var coveredItems = new HashSet<string>(
                specification.Tasks.SelectMany(task => task.SpecRefs.Where(r => !string.IsNullOrWhiteSpace(r)))
                    .Select(r => r.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var step in specification.Steps.Where(step => !string.IsNullOrWhiteSpace(step.Id)))
            {
                if (!coveredItems.Contains(step.Id.Trim()))
                {
                    plan.NeedsDecision.Add("шаг " + step.Id + " не покрыт задачей — генерировать нечего");
                }
            }

            foreach (var ambiguity in specification.Ambiguities.Where(item => item.Status == SpecAmbiguityStatus.Open))
            {
                plan.NeedsDecision.Add("открытый вопрос " + ambiguity.Id +
                                       (string.IsNullOrWhiteSpace(ambiguity.ItemId) ? string.Empty : " (" + ambiguity.ItemId + ")") +
                                       ": " + ambiguity.Question);
            }

            bool catalogAvailable = catalog != null && catalog.Count > 0;
            if (!catalogAvailable)
            {
                plan.NeedsDecision.Add("каталог активностей недоступен — типы активностей не проверены");
            }

            foreach (var task in specification.Tasks)
            {
                if (task.Steps.Count == 0)
                {
                    plan.NeedsDecision.Add("задача " + task.Id + " без шагов workflow");
                    continue;
                }

                bool alreadyGenerated = task.ElementIds.Count > 0 &&
                                        task.ElementIds.All(id => knownElements.Contains(id));
                if (alreadyGenerated)
                {
                    plan.Skipped.Add("задача " + task.Id + " уже сгенерирована: " + string.Join(", ", task.ElementIds));
                    continue;
                }

                foreach (var step in task.Steps)
                {
                    if (string.IsNullOrWhiteSpace(step.ActivityType))
                    {
                        plan.NeedsDecision.Add("задача " + task.Id + ": шаг без типа активности");
                        continue;
                    }

                    if (catalogAvailable && catalog.Describe(step.ActivityType) == null)
                    {
                        plan.NeedsDecision.Add("задача " + task.Id + ": активности «" + step.ActivityType +
                                               "» нет в каталоге студии");
                        continue;
                    }

                    plan.Operations.Add(new SpecGenerationOperation
                    {
                        TaskId = task.Id,
                        ActivityType = step.ActivityType,
                        Parent = string.IsNullOrWhiteSpace(step.Parent) ? WorkflowEditing.RootElementId : step.Parent,
                        Container = step.Container,
                        ArgumentName = step.ArgumentName,
                        ArgumentValue = step.ArgumentValue
                    });
                }
            }

            return plan;
        }

        /// <summary>
        /// Применяет план пачками: одна задача — одна атомарная правка (вставки), затем аргументы.
        /// Ошибка любой пачки останавливает генерацию, уже применённые пачки остаются в истории отмены.
        /// </summary>
        public static SpecGenerationResult Apply(SpecGenerationPlan plan, IWorkflowEditingService editing,
            string workflowPath = null)
        {
            var result = new SpecGenerationResult();
            if (plan == null)
            {
                result.Error = "план генерации не построен";
                return result;
            }

            result.Skipped.AddRange(plan.Skipped);
            result.NeedsDecision.AddRange(plan.NeedsDecision);

            if (!plan.Ready)
            {
                result.Error = "план пуст: ни одной операции к применению";
                return result;
            }

            string path = string.IsNullOrWhiteSpace(workflowPath) ? plan.WorkflowPath : workflowPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                result.Error = "не указан путь процесса";
                return result;
            }

            var service = editing ?? NullWorkflowEditingService.Instance;
            if (!service.IsOpenInDesigner(path))
            {
                result.Error = "процесс не открыт в конструкторе: правки идут в модель открытого процесса, " +
                               "а не в текст файла (откройте " + path + ")";
                return result;
            }

            var runner = new WorkflowEditRunner(service);
            var created = new List<string>();

            foreach (var group in plan.Operations.GroupBy(operation => operation.TaskId))
            {
                var operations = group.ToList();

                var insertRequests = operations.Select(operation => new WorkflowInsertRequest
                {
                    ParentId = operation.Parent,
                    Container = string.IsNullOrWhiteSpace(operation.Container) ? null : operation.Container,
                    ActivityType = operation.ActivityType
                }).ToList();

                var insertResult = runner.Insert(path, insertRequests, WorkflowEditValidation.Report,
                    "ТЗ " + group.Key);
                if (!insertResult.Ok)
                {
                    result.Error = "задача " + group.Key + ": вставка не применена — " + insertResult.Message;
                    CollectErrors(result, insertResult);
                    return result;
                }

                var addresses = insertResult.Addresses.Where(address => !string.IsNullOrWhiteSpace(address)).ToList();
                result.Created.AddRange(addresses);
                created.AddRange(addresses);
                result.TaskElements[group.Key] = addresses;
                CollectErrors(result, insertResult);

                // Аргументы адресуются по порядку применённых вставок: адрес i соответствует запросу i.
                var argumentRequests = new List<WorkflowArgumentSetRequest>();
                for (int i = 0; i < operations.Count && i < addresses.Count; i++)
                {
                    var operation = operations[i];
                    if (string.IsNullOrWhiteSpace(operation.ArgumentName))
                    {
                        continue;
                    }

                    var request = new WorkflowArgumentSetRequest
                    {
                        ElementId = addresses[i],
                        Argument = operation.ArgumentName
                    };

                    if (string.IsNullOrWhiteSpace(operation.ArgumentValue))
                    {
                        request.SetValue(string.Empty);
                    }
                    else
                    {
                        request.SetValue(operation.ArgumentValue);
                    }

                    argumentRequests.Add(request);
                }

                if (argumentRequests.Count == 0)
                {
                    continue;
                }

                var argumentResult = runner.SetArgument(path, argumentRequests, WorkflowEditValidation.Report,
                    "ТЗ " + group.Key + ": аргументы");
                if (!argumentResult.Ok)
                {
                    result.Error = "задача " + group.Key + ": аргументы не заданы — " + argumentResult.Message;
                    CollectErrors(result, argumentResult);
                    return result;
                }

                result.ArgumentsSet.AddRange(argumentRequests.Select(request => request.ToString()));
                CollectErrors(result, argumentResult);
            }

            result.Ok = true;
            result.ValidationText = result.Errors == 0
                ? "ошибок валидации нет"
                : "ошибок валидации: " + result.Errors + " (см. issues в ответе инструментов правки)";
            return result;
        }

        private static void CollectErrors(SpecGenerationResult result, WorkflowEditResult editResult)
        {
            if (editResult?.Issues == null)
            {
                return;
            }

            result.Errors += editResult.Issues.Count(issue => issue.IsError);
        }
    }
}
