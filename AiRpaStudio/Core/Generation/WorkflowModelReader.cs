using System;
using System.Activities;
using System.Activities.Statements;
using System.Activities.XamlIntegration;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>Аргумент активности в снимке процесса (этап 4 плана, инструмент <c>xaml.get_workflow</c>).</summary>
    internal sealed class WorkflowArgumentSnapshot
    {
        internal string Name { get; set; }

        internal string TypeName { get; set; }

        internal string Direction { get; set; }

        /// <summary>Текст C#-выражения; <c>null</c>, если аргумент не задан.</summary>
        internal string Expression { get; set; }

        internal bool IsSet => !string.IsNullOrWhiteSpace(Expression);

        public override string ToString()
        {
            return $"{Name} ({Direction}, {TypeName}): {Expression ?? "(не задан)"}";
        }
    }

    /// <summary>Переменная в снимке процесса: имя, тип и выражение значения по умолчанию.</summary>
    internal sealed class WorkflowVariableSnapshot
    {
        internal string Name { get; set; }

        internal string TypeName { get; set; }

        /// <summary>Выражение значения по умолчанию; <c>null</c>, если оно не задано.</summary>
        internal string DefaultExpression { get; set; }

        public override string ToString()
        {
            return $"{Name} ({TypeName})";
        }
    }

    /// <summary>Узел снимка процесса: активность, её аргументы и вложенные активности.</summary>
    internal sealed class WorkflowActivitySnapshot
    {
        internal string Id { get; set; }

        /// <summary>Идентификатор, который присвоил WF (<see cref="Activity.Id"/>); может быть пустым.</summary>
        internal string RuntimeId { get; set; }

        internal string TypeName { get; set; }

        internal string DisplayName { get; set; }

        internal string ParentId { get; set; }
        /// <summary>Полное имя типа (<c>System.Activities.Statements.Sequence</c>); <c>TypeName</c> — короткое.</summary>
        internal string TypeFullName { get; set; }



        /// <summary>Имя контейнера родителя, в котором лежит эта активность (<c>Activities[1]</c>, <c>Then</c>).</summary>
        internal string ContainerPath { get; set; }

        internal List<WorkflowArgumentSnapshot> Arguments { get; } = new List<WorkflowArgumentSnapshot>();

        /// <summary>
        /// Переменные, объявленные в области видимости активности (карточка <c>AG-03</c>):
        /// агенту они нужны, чтобы писать выражения и не заводить дубли.
        /// </summary>
        internal List<WorkflowVariableSnapshot> Variables { get; } = new List<WorkflowVariableSnapshot>();

        /// <summary>
        /// Активность модели, к которой относится узел. Нужна, чтобы привязать ошибку валидации WF
        /// (<c>ValidationError.Source</c>) к идентификатору узла: сравнение идёт по ссылке.
        /// </summary>
        internal Activity Activity { get; set; }

        internal List<WorkflowActivitySnapshot> Children { get; } = new List<WorkflowActivitySnapshot>();

        /// <summary>Все потомки (в порядке обхода) — для поиска по идентификатору.</summary>
        internal IEnumerable<WorkflowActivitySnapshot> Descendants()
        {
            foreach (WorkflowActivitySnapshot child in Children)
            {
                yield return child;

                foreach (WorkflowActivitySnapshot nested in child.Descendants())
                {
                    yield return nested;
                }
            }
        }

        public override string ToString()
        {
            return $"{TypeName}#{Id} «{DisplayName}»";
        }
    }

    /// <summary>Снимок процесса: корень, число активностей, ошибки чтения.</summary>
    internal sealed class WorkflowSnapshot
    {
        internal bool Ok { get; set; }

        internal string FilePath { get; set; }

        /// <summary><c>x:Class</c> процесса.</summary>
        internal string WorkflowId { get; set; }

        internal WorkflowActivitySnapshot Root { get; set; }

        internal int ActivityCount { get; set; }

        internal List<string> Errors { get; } = new List<string>();

        internal List<string> Hints { get; } = new List<string>();

        /// <summary>Все узлы (корень и потомки) в порядке обхода.</summary>
        internal IEnumerable<WorkflowActivitySnapshot> All()
        {
            if (Root == null)
            {
                return Enumerable.Empty<WorkflowActivitySnapshot>();
            }

            return new[] { Root }.Concat(Root.Descendants());
        }

        /// <summary>
        /// Поиск активности по идентификатору из <c>xaml.get_workflow</c>. Дополнительно принимается
        /// <c>DisplayName</c>, если он уникален, — агенту проще, а неоднозначность возвращается ошибкой.
        /// </summary>
        internal WorkflowActivitySnapshot Find(string activityId)
        {
            if (string.IsNullOrWhiteSpace(activityId) || Root == null)
            {
                return null;
            }

            string key = activityId.Trim();
            WorkflowActivitySnapshot byId = All().FirstOrDefault(node =>
                string.Equals(node.Id, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.RuntimeId, key, StringComparison.Ordinal));

            if (byId != null)
            {
                return byId;
            }

            WorkflowActivitySnapshot[] byName = All()
                .Where(node => string.Equals(node.DisplayName, key, StringComparison.Ordinal))
                .ToArray();

            return byName.Length == 1 ? byName[0] : null;
        }
    }

    /// <summary>
    /// Чтение модели процесса из XAML (этап 4 конвейера генерации XAML,
    /// инструмент <c>xaml.get_workflow</c>): активности, идентификаторы, аргументы и выражения.
    /// <para>
    /// Источник — файл, а не живой дизайнер: после каждого изменения палитра сохраняет процесс штатным
    /// сериализатором конструктора, поэтому файл совпадает с моделью. За счёт этого снимок читается
    /// без WPF и проверяется юнит-тестами.
    /// </para>
    /// <para>
    /// Идентификатор активности — путь в дереве (<c>root/Activities[1]/Then</c>), а не <see cref="Activity.Id"/>:
    /// WF присваивает свои идентификаторы только при материализации дерева, и они не годятся как адрес
    /// для последующих вызовов <c>xaml.set_argument</c>. Путь совпадает с путём <c>ModelItem</c>
    /// в открытом конструкторе, поэтому одна и та же строка работает и для чтения, и для правки.
    /// </para>
    /// </summary>
    internal static class WorkflowModelReader
    {
        /// <summary>Идентификатор корневой активности процесса.</summary>
        internal const string RootId = "root";

        /// <summary>Читает процесс из файла; ошибки возвращаются в снимке, а не исключением.</summary>
        internal static WorkflowSnapshot Read(string filePath)
        {
            var snapshot = new WorkflowSnapshot { FilePath = filePath };

            if (string.IsNullOrWhiteSpace(filePath))
            {
                snapshot.Errors.Add("xaml.path: путь к файлу процесса не задан");
                return snapshot;
            }

            string fullPath;
            try
            {
                fullPath = System.IO.Path.GetFullPath(filePath);
            }
            catch (Exception ex)
            {
                snapshot.Errors.Add(ErrorText.Describe($"путь к файлу процесса недопустим: {filePath}", ex));
                return snapshot;
            }

            snapshot.FilePath = fullPath;

            if (!System.IO.File.Exists(fullPath))
            {
                snapshot.Errors.Add($"xaml.path: файл процесса не найден — {fullPath}");
                snapshot.Hints.Add("сначала соберите процесс (xaml.create_workflow) или откройте существующий");
                return snapshot;
            }

            Activity root;
            try
            {
                root = LoadActivity(fullPath);
            }
            catch (Exception ex)
            {
                snapshot.Errors.Add(ErrorText.Describe($"процесс не прочитан: {fullPath}", ex));
                return snapshot;
            }

            if (root == null)
            {
                snapshot.Errors.Add($"xaml.load: файл {fullPath} не является рабочим процессом WF");
                return snapshot;
            }

            return Read(root, fullPath);
        }

        /// <summary>
        /// Снимок по уже загруженной активности (карточка <c>AG-03</c>): структура и валидация обязаны
        /// работать на одном экземпляре дерева, иначе <c>ValidationError.Source</c> не сопоставить
        /// с идентификатором узла.
        /// </summary>
        internal static WorkflowSnapshot Read(Activity activity, string filePath)
        {
            var snapshot = new WorkflowSnapshot { FilePath = filePath };

            if (activity == null)
            {
                snapshot.Errors.Add($"xaml.load: файл {filePath} не является рабочим процессом WF");
                return snapshot;
            }

            snapshot.WorkflowId = string.IsNullOrWhiteSpace(filePath)
                ? null
                : DesignerOpenService.ReadWorkflowId(filePath);
            snapshot.Root = Walk(activity, RootId, parentId: null, containerPath: null);

            snapshot.ActivityCount = snapshot.Root == null ? 0 : 1 + snapshot.Root.Descendants().Count();
            snapshot.Ok = snapshot.Root != null;

            return snapshot;
        }

        /// <summary>Процесс из файла в виде <see cref="Activity"/> (у процесса с <c>x:Class</c> — реализация).</summary>
        internal static Activity LoadActivity(string filePath)
        {
            object loaded = ActivityXamlServices.Load(filePath);

            if (loaded is ActivityBuilder builder)
            {
                return builder.Implementation;
            }

            if (loaded is DynamicActivity dynamicActivity)
            {
                // Файл процесса с x:Class WF загружает как DynamicActivity: реализация доступна
                // только через Implementation() — иначе дерево активностей не прочитать.
                return dynamicActivity.Implementation?.Invoke();
            }

            return loaded as Activity;
        }

        private static WorkflowActivitySnapshot Walk(Activity activity, string id, string parentId, string containerPath)
        {
            if (activity == null)
            {
                return null;
            }

            var node = new WorkflowActivitySnapshot
            {
                Id = id,
                RuntimeId = activity.Id,
                TypeName = PaletteTypeNames.NameOf(activity.GetType()),
                TypeFullName = activity.GetType().FullName,
                DisplayName = activity.DisplayName,
                ParentId = parentId,
                ContainerPath = containerPath,
                Activity = activity
            };

            PropertyInfo[] properties = activity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo property in properties)
            {
                if (!typeof(Argument).IsAssignableFrom(property.PropertyType))
                {
                    continue;
                }

                WorkflowArgumentSnapshot argument = DescribeArgument(activity, property);
                if (argument != null)
                {
                    node.Arguments.Add(argument);
                }
            }

            ReadVariables(activity, node);


            foreach (PropertyInfo property in properties)
            {
                foreach (KeyValuePair<string, Activity> child in ChildrenOf(activity, property))
                {
                    WorkflowActivitySnapshot childNode = Walk(
                        child.Value, node.Id + "/" + child.Key, node.Id, child.Key);

                    if (childNode != null)
                    {
                        node.Children.Add(childNode);
                    }
                }
            }

            return node;
        }

        /// <summary>
        /// Дочерние активности свойства: ключ — сегмент пути (<c>Activities[0]</c>, <c>Then</c>,
        /// <c>Body/Handler</c>), значение — активность.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, Activity>> ChildrenOf(Activity activity, PropertyInfo property)
        {
            object value;
            try
            {
                value = property.GetValue(activity);
            }
            catch (Exception)
            {
                // Свойство может быть недоступно без контекста выполнения — это не ошибка чтения модели.
                yield break;
            }

            // Блок-схема: шаги лежат в графе узлов (StartNode → FlowStep.Action → Next), а не в коллекции
            // активностей. Без этой ветки у Flowchart в структуре не было ни одного шага.
            if (value is FlowNode flowNode)
            {
                foreach (KeyValuePair<string, Activity> step in ChildrenOfFlowNode(
                    flowNode, property.Name, new HashSet<FlowNode>()))
                {
                    yield return step;
                }

                yield break;
            }

            if (typeof(Collection<Activity>).IsAssignableFrom(property.PropertyType)
                && value is IEnumerable<Activity> collection)
            {
                int index = 0;
                foreach (Activity child in collection.Where(item => item != null))
                {
                    yield return new KeyValuePair<string, Activity>(
                        $"{property.Name}[{index}]", child);
                    index++;
                }

                yield break;
            }

            if (property.PropertyType == typeof(Activity) && value is Activity single)
            {
                yield return new KeyValuePair<string, Activity>(property.Name, single);
                yield break;
            }

            if (property.PropertyType == typeof(ActivityAction) && value is ActivityAction action)
            {
                if (action.Handler != null)
                {
                    yield return new KeyValuePair<string, Activity>(
                        property.Name + "/Handler", action.Handler);
                }

                yield break;
            }

            if (property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(ActivityAction<>)
                && value != null
                && property.PropertyType.GetProperty("Handler")?.GetValue(value) is Activity handler)
            {
                yield return new KeyValuePair<string, Activity>(
                    property.Name + "/Handler", handler);
            }

            // Коллекции «не-активностей» (TryCatch.Catches → Catch, Flowchart.Nodes → FlowNode):
            // адрес указывает на элемент коллекции, а активность лежит в его свойстве
            // (root/Catches[0]/Action/Handler, root/Nodes[0]/Action). Без этой ветки тело
            // обработчика и шаги блок-схемы не попадают в структуру и недостижимы для правок.
            if (value is System.Collections.IEnumerable items && !(value is string))
            {
                // Коллекция узлов блок-схемы (Flowchart.Nodes) ссылается на те же узлы, что и StartNode:
                // обходим их графом, иначе шаги задвоятся, а адреса станут неоднозначными.
                if (property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericArguments()[0] == typeof(FlowNode))
                {
                    yield break;
                }

                int itemIndex = 0;
                foreach (object item in items)
                {
                    string prefix = $"{property.Name}[{itemIndex}]";
                    itemIndex++;

                    // Значение по умолчанию переменной показывается в описании переменной,
                    // а не отдельным узлом дерева: Literal<string> в outline только мешает.
                    if (item == null || item is Variable)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<string, Activity> child in ChildrenOfItem(item, prefix))
                    {
                        yield return child;
                    }
                }
            }
        }

        /// <summary>
        /// Узлы графа блок-схемы: действие узла (<c>FlowStep.Action</c>) и переходы (<c>Next</c>,
        /// <c>True</c>/<c>False</c>, <c>Default</c>, <c>Cases[i]</c>). Посещённые узлы запоминаются:
        /// в графе допустимы циклы, и обход без этого не завершится.
        /// </summary>
        private static IEnumerable<KeyValuePair<string, Activity>> ChildrenOfFlowNode(
            FlowNode node,
            string prefix,
            HashSet<FlowNode> visited)
        {
            if (node == null || !visited.Add(node))
            {
                yield break;
            }

            foreach (KeyValuePair<string, Activity> child in ChildrenOfItem(node, prefix))
            {
                yield return child;
            }

            foreach (PropertyInfo property in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object value;
                try
                {
                    value = property.GetValue(node);
                }
                catch (Exception)
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                var next = value as FlowNode;
                if (next != null)
                {
                    foreach (KeyValuePair<string, Activity> child in ChildrenOfFlowNode(
                        next, prefix + "/" + property.Name, visited))
                    {
                        yield return child;
                    }

                    continue;
                }

                var cases = value as System.Collections.IDictionary;
                if (cases == null)
                {
                    continue;
                }

                int index = 0;
                foreach (System.Collections.DictionaryEntry entry in cases)
                {
                    foreach (KeyValuePair<string, Activity> child in ChildrenOfFlowNode(
                        entry.Value as FlowNode, prefix + "/" + property.Name + "[" + index + "]", visited))
                    {
                        yield return child;
                    }

                    index++;
                }
            }
        }


        /// <summary>
        /// Дочерние активности элемента коллекции, который сам активностью не является
        /// (<c>Catch</c>, <c>FlowStep</c>): ключ — путь от элемента (<c>Action</c>, <c>Action/Handler</c>).
        /// </summary>
        private static IEnumerable<KeyValuePair<string, Activity>> ChildrenOfItem(object item, string prefix)
        {
            PropertyInfo[] properties = item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo property in properties)
            {
                object value;
                try
                {
                    value = property.GetValue(item);
                }
                catch (Exception)
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                if (property.PropertyType == typeof(Activity) && value is Activity activity)
                {
                    yield return new KeyValuePair<string, Activity>(prefix + "/" + property.Name, activity);
                    continue;
                }

                if (property.PropertyType == typeof(ActivityAction) && value is ActivityAction action)
                {
                    if (action.Handler != null)
                    {
                        yield return new KeyValuePair<string, Activity>(
                            prefix + "/" + property.Name + "/Handler", action.Handler);
                    }

                    continue;
                }

                if (property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == typeof(ActivityAction<>)
                    && property.PropertyType.GetProperty("Handler")?.GetValue(value) is Activity handler)
                {
                    yield return new KeyValuePair<string, Activity>(
                        prefix + "/" + property.Name + "/Handler", handler);
                }
            }
        }

        /// <summary>
        /// Переменные, объявленные активностью (<c>Sequence.Variables</c>, <c>Flowchart.Variables</c>).
        /// Читаются рефлексией: свойство объявлено в разных базовых типах, а общий интерфейс WF его не даёт.
        /// </summary>
        private static void ReadVariables(Activity activity, WorkflowActivitySnapshot node)
        {
            PropertyInfo property = activity.GetType()
                .GetProperty("Variables", BindingFlags.Public | BindingFlags.Instance);

            object value;
            try
            {
                value = property?.GetValue(activity);
            }
            catch (Exception)
            {
                return;
            }

            if (!(value is IEnumerable<Variable> variables))
            {
                return;
            }

            foreach (Variable variable in variables)
            {
                if (variable == null)
                {
                    continue;
                }

                node.Variables.Add(new WorkflowVariableSnapshot
                {
                    Name = variable.Name,
                    TypeName = PaletteTypeNames.NameOf(variable.Type ?? typeof(object)),
                    DefaultExpression = ExpressionTextOfActivity(variable.Default)
                });
            }
        }

        /// <summary>Текст выражения активности-выражения (<c>CSharpValue&lt;T&gt;.ExpressionText</c>).</summary>
        private static string ExpressionTextOfActivity(Activity activity)
        {
            if (activity == null)
            {
                return null;
            }

            try
            {
                PropertyInfo property = activity.GetType()
                    .GetProperty("ExpressionText", BindingFlags.Public | BindingFlags.Instance);

                return property?.GetValue(activity) as string;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Текст значения по умолчанию не прочитан: " + ErrorText.Of(ex));
                return null;
            }
        }


        private static WorkflowArgumentSnapshot DescribeArgument(Activity activity, PropertyInfo property)
        {
            Argument argument;
            try
            {
                argument = (Argument)property.GetValue(activity);
            }
            catch (Exception)
            {
                return null;
            }

            if (argument == null)
            {
                // Аргумент не задан: именно такие места и правит xaml.set_argument.
                return new WorkflowArgumentSnapshot
                {
                    Name = property.Name,
                    TypeName = PaletteTypeNames.NameOf(ArgumentValueType(property.PropertyType)),
                    Direction = DirectionOf(property.PropertyType).ToString()
                };
            }

            return new WorkflowArgumentSnapshot
            {
                Name = property.Name,
                TypeName = PaletteTypeNames.NameOf(argument.ArgumentType),
                Direction = argument.Direction.ToString(),
                Expression = ExpressionText(argument)
            };
        }

        private static Type ArgumentValueType(Type argumentType)
        {
            if (argumentType.IsGenericType
                && (argumentType.GetGenericTypeDefinition() == typeof(InArgument<>)
                    || argumentType.GetGenericTypeDefinition() == typeof(OutArgument<>)
                    || argumentType.GetGenericTypeDefinition() == typeof(InOutArgument<>)))
            {
                return argumentType.GetGenericArguments()[0];
            }

            return typeof(object);
        }

        private static ArgumentDirection DirectionOf(Type argumentType)
        {
            if (typeof(OutArgument).IsAssignableFrom(argumentType))
            {
                return ArgumentDirection.Out;
            }

            if (typeof(InOutArgument).IsAssignableFrom(argumentType))
            {
                return ArgumentDirection.InOut;
            }

            return ArgumentDirection.In;
        }

        /// <summary>Текст C#-выражения аргумента (<c>CSharpValue&lt;T&gt;.ExpressionText</c>).</summary>
        internal static string ExpressionText(Argument argument)
        {
            try
            {
                object expression = argument?.Expression;
                if (expression == null)
                {
                    return null;
                }

                PropertyInfo property = expression.GetType()
                    .GetProperty("ExpressionText", BindingFlags.Public | BindingFlags.Instance);

                string text = property?.GetValue(expression) as string;

                // У простых значений (Text="first") выражения нет: WF держит Literal<T>, и без этой
                // ветки структура показывала бы «аргумент задан», но не его значение.
                return string.IsNullOrWhiteSpace(text) ? LiteralText(expression) : text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Текст выражения не прочитан: " + ErrorText.Of(ex));
                return null;
            }
        }

        /// <summary>Текст простого значения (<c>Literal&lt;T&gt;</c>): строка — в кавычках, остальное — как есть.</summary>
        private static string LiteralText(object expression)
        {
            Type type = expression.GetType();

            if (!type.IsGenericType
                || type.GetGenericTypeDefinition() != typeof(System.Activities.Expressions.Literal<>))
            {
                return null;
            }

            object value = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(expression);

            if (value == null)
            {
                return "null";
            }

            var text = value as string;

            return text != null
                ? "\"" + text + "\""
                : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

    }
}
