using System;
using System.Activities;
using System.Activities.Validation;
using System.Collections.Generic;
using System.IO;
using RPA.Chat.Services.Workflow;
using RPA.Core;
using RPA.Core.Generation;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using RPA.Helpers;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Структура процесса для агента (карточка <c>AG-03</c>, инструмент <c>workflow_outline</c>).
    /// <para>
    /// Источник — тот же <see cref="WorkflowModelReader"/>, что и у инструментов <c>xaml.*</c>: адреса
    /// узлов (<c>root/Activities[0]</c>) совпадают с путями <c>ModelItem</c> в конструкторе, поэтому одна
    /// строка годится и для чтения, и для правок <c>AG-04</c>.
    /// </para>
    /// <para>
    /// Ошибки WF-валидации привязываются к узлам: валидация запускается на том же экземпляре дерева,
    /// который прочитал ридер, и <c>ValidationError.Source</c> сопоставляется с адресом по ссылке.
    /// Иначе агент видел бы «ошибка есть», но не знал, какой элемент править.
    /// </para>
    /// <para>
    /// Реализация headless: ни конструктора, ни диспетчера, ни <c>MessageBox</c> — поэтому проверяется
    /// юнит-тестами. Структура открытого (несохранённого) документа появится вместе с мостом
    /// <c>AG-02</c>, когда у правок появится модель конструктора.
    /// </para>
    /// </summary>
    internal sealed class StudioWorkflowOutlineService : IWorkflowOutlineService
    {
        private static readonly Lazy<StudioWorkflowOutlineService> Shared =
            new Lazy<StudioWorkflowOutlineService>(() => new StudioWorkflowOutlineService());

        /// <summary>Служба для хоста чата: <c>ChatEnvironment.WorkflowOutline</c>.</summary>
        internal static StudioWorkflowOutlineService Default => Shared.Value;

        public WorkflowOutline GetOutline(string path, int depth = 0, bool includeArguments = true)
        {
            string fullPath = null;
            string pathError = null;

            try
            {
                fullPath = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                pathError = ErrorText.Describe($"путь к файлу процесса недопустим: {path}", ex);
            }

            // Источник структуры по умолчанию — файл на диске; живой конструктор переопределяет его
            // в FromDesigner, чтобы ответ инструмента всегда говорил, что именно проверено.
            var outline = new WorkflowOutline(fullPath ?? path, depth, includeArguments)
            {
                Source = WorkflowOutlineSource.File
            };

            if (!string.IsNullOrWhiteSpace(pathError))
            {
                outline.AddError(string.Empty, pathError, "xaml.path");
                return outline;
            }

            if (string.IsNullOrWhiteSpace(fullPath))
            {
                outline.AddError(string.Empty, "xaml.path: путь к файлу процесса не задан", "xaml.path");
                return outline;
            }

            if (!File.Exists(fullPath))
            {
                outline.AddError(string.Empty, "xaml.path: файл процесса не найден — " + fullPath, "xaml.path");
                return outline;
            }

            // Открытый документ важнее файла: правки моста AG-02 живут в модели конструктора и до
            // сохранения на диск в файл не попадают. Если источник не подключён или вкладки нет —
            // структура читается с диска, как раньше.
            WorkflowOutline live = ReadLiveOutline(fullPath, depth, includeArguments);
            if (live != null)
            {
                return live;
            }

            Activity activity;
            try
            {
                activity = WorkflowModelReader.LoadActivity(fullPath);
            }
            catch (Exception ex)
            {
                outline.AddError(string.Empty, ErrorText.Describe($"процесс не прочитан: {fullPath}", ex), "xaml.load");
                return outline;
            }

            if (activity == null)
            {
                outline.AddError(
                    string.Empty,
                    $"xaml.load: файл {fullPath} не является рабочим процессом WF",
                    "xaml.load");
                return outline;
            }

            WorkflowOutline fromFile = BuildOutline(activity, fullPath, depth, includeArguments);
            fromFile.Source = WorkflowOutlineSource.File;

            return fromFile;
        }

        /// <summary>
        /// Источник структуры открытого документа (мост <c>AG-02</c>). Подключается хостом студии
        /// (<c>MainView</c>); <c>null</c> — источник не подключён, структура читается с диска.
        /// Исключение источника не должно ломать чтение файла, поэтому оно превращается в «нет данных».
        /// </summary>
        internal static Func<string, int, bool, WorkflowOutline> LiveOutlineSource { get; set; }

        private static WorkflowOutline ReadLiveOutline(string fullPath, int depth, bool includeArguments)
        {
            Func<string, int, bool, WorkflowOutline> source = LiveOutlineSource;
            if (source == null)
            {
                return null;
            }

            try
            {
                return source(fullPath, depth, includeArguments);
            }
            catch (Exception ex)
            {
                // Структура открытого документа — уточнение, а не единственный источник: падать нельзя.
                System.Diagnostics.Debug.WriteLine("Структура открытого документа не прочитана: " + ErrorText.Of(ex));
                return null;
            }
        }

        /// <summary>
        /// Структура по уже загруженной активности: дерево, переменные, аргументы и ошибки WF-валидации.
        /// Общая часть файлового чтения и живой модели конструктора (<see cref="FromDesigner"/>), чтобы
        /// адреса узлов у <c>workflow_outline</c> и у правок совпадали всегда.
        /// </summary>
        internal static WorkflowOutline BuildOutline(Activity activity, string filePath, int depth, bool includeArguments)
        {
            var outline = new WorkflowOutline(filePath, depth, includeArguments);

            if (activity == null)
            {
                outline.AddError(string.Empty, $"xaml.load: файл {filePath} не является рабочим процессом WF", "xaml.load");
                return outline;
            }

            WorkflowSnapshot snapshot = WorkflowModelReader.Read(activity, filePath);

            if (!snapshot.Ok || snapshot.Root == null)
            {
                foreach (string error in snapshot.Errors)
                {
                    outline.AddError(string.Empty, error, "xaml.load");
                }

                foreach (string hint in snapshot.Hints)
                {
                    outline.AddWarning(string.Empty, "подсказка: " + hint, "xaml.hint");
                }

                return outline;
            }

            outline.WorkflowId = snapshot.WorkflowId;
            outline.ActivityCount = snapshot.ActivityCount;
            outline.Root = BuildNode(snapshot.Root, outline, level: 1, index: 0);
            outline.Ok = outline.Root != null;
            outline.ShownDepth = outline.Ok ? ShownDepth(outline.Root, 1) : 0;

            AddValidationIssues(outline, snapshot, activity);

            return outline;
        }

        /// <summary>
        /// Структура процесса из модели открытого конструктора, включая несохранённые правки.
        /// <c>null</c> — модель ещё не загружена (вызывать только на UI-потоке дизайнера).
        /// </summary>
        internal static WorkflowOutline FromDesigner(
            WorkflowDesignerEx designer,
            string filePath,
            int depth,
            bool includeArguments)
        {
            Activity activity = RootActivityOf(designer);
            if (activity == null)
            {
                return null;
            }

            WorkflowOutline outline = BuildOutline(activity, filePath, depth, includeArguments);

            if (outline != null)
            {
                outline.Source = WorkflowOutlineSource.Designer;
            }

            return outline;
        }

        /// <summary>Корневая активность модели конструктора: у процесса с <c>x:Class</c> — реализация <c>ActivityBuilder</c>.</summary>
        internal static Activity RootActivityOf(WorkflowDesignerEx designer)
        {
            ModelService modelService = designer?.Context?.Services?.GetService<ModelService>();
            object root = modelService?.Root?.GetCurrentValue();

            if (root is ActivityBuilder builder)
            {
                return builder.Implementation;
            }

            return root as Activity;
        }

        /// <summary>
        /// Узел структуры из снимка. Глубина: <c>Depth &lt;= 0</c> — всё дерево; иначе разворачиваются
        /// уровни <c>1..Depth</c>, а у обрезанных узлов сохраняется <see cref="WorkflowOutlineNode.ChildCount"/>,
        /// чтобы агент видел «здесь есть вложенные» и мог запросить глубину больше.
        /// </summary>
        private static WorkflowOutlineNode BuildNode(
            WorkflowActivitySnapshot source,
            WorkflowOutline outline,
            int level,
            int index)
        {
            var arguments = new List<WorkflowOutlineArgument>();

            if (outline.IncludeArguments)
            {
                foreach (WorkflowArgumentSnapshot argument in source.Arguments)
                {
                    arguments.Add(new WorkflowOutlineArgument(
                        argument.Name,
                        argument.Direction,
                        argument.TypeName,
                        argument.Expression));
                }
            }

            var variables = new List<WorkflowOutlineVariable>();
            foreach (WorkflowVariableSnapshot variable in source.Variables)
            {
                variables.Add(new WorkflowOutlineVariable(
                    variable.Name,
                    variable.TypeName,
                    variable.DefaultExpression));
            }

            var node = new WorkflowOutlineNode(
                source.Id,
                source.ParentId,
                index,
                source.ContainerPath,
                string.IsNullOrWhiteSpace(source.TypeFullName) ? source.TypeName : source.TypeFullName,
                source.DisplayName,
                arguments,
                variables);

            node.SetChildCount(source.Children.Count);

            bool canDescend = outline.Depth <= 0 || level < outline.Depth;
            if (source.Children.Count > 0 && !canDescend)
            {
                node.MarkTruncated();
                return node;
            }

            for (int childIndex = 0; childIndex < source.Children.Count; childIndex++)
            {
                node.AddChild(BuildNode(source.Children[childIndex], outline, level + 1, childIndex));
            }

            return node;
        }

        private static int ShownDepth(WorkflowOutlineNode node, int level)
        {
            int depth = level;

            foreach (WorkflowOutlineNode child in node.Children)
            {
                int childDepth = ShownDepth(child, level + 1);
                if (childDepth > depth)
                {
                    depth = childDepth;
                }
            }

            return depth;
        }

        /// <summary>
        /// Ошибки и предупреждения WF, привязанные к адресам узлов. Валидация идёт на том же экземпляре
        /// дерева, что читал ридер, поэтому сопоставление по ссылке точное; активность вне дерева
        /// (служебная) даёт замечание без адреса, а не теряется.
        /// </summary>
        private static void AddValidationIssues(
            WorkflowOutline outline,
            WorkflowSnapshot snapshot,
            Activity root)
        {
            var ids = new Dictionary<Activity, string>(ReferenceComparer.Instance);

            foreach (WorkflowActivitySnapshot node in snapshot.All())
            {
                if (node.Activity != null && !ids.ContainsKey(node.Activity))
                {
                    ids.Add(node.Activity, node.Id);
                }
            }

            ValidationResults results;
            try
            {
                results = ActivityValidationServices.Validate(root);

                // Валидация отработала: только теперь «ошибок нет» — утверждение, а не отсутствие данных.
                outline.MarkValidationAvailable();
            }
            catch (Exception ex)
            {
                string reason = "валидация WF не выполнена: " +
                    ErrorText.Describe("ActivityValidationServices.Validate", ex);

                outline.AddWarning(string.Empty, reason, "xaml.validate");
                outline.MarkValidationUnavailable(reason);
                return;
            }

            foreach (ValidationError error in results.Errors)
            {
                outline.AddIssue(new WorkflowOutlineIssue(
                    IdOf(ids, error?.Source),
                    WorkflowOutlineIssueSeverity.Error,
                    error?.Message,
                    "wf.validate",
                    error?.PropertyName));
            }

            foreach (ValidationError warning in results.Warnings)
            {
                outline.AddIssue(new WorkflowOutlineIssue(
                    IdOf(ids, warning?.Source),
                    WorkflowOutlineIssueSeverity.Warning,
                    warning?.Message,
                    "wf.validate",
                    warning?.PropertyName));
            }
        }

        private static string IdOf(Dictionary<Activity, string> ids, Activity source)
        {
            if (source == null)
            {
                return string.Empty;
            }

            string id;
            return ids.TryGetValue(source, out id) ? id : string.Empty;
        }

        /// <summary>Сравнение активностей по ссылке: <see cref="Activity"/> не переопределяет <c>Equals</c>, но полагаться на это нельзя.</summary>
        private sealed class ReferenceComparer : IEqualityComparer<Activity>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(Activity x, Activity y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(Activity obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
