using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Направление аргумента активности (этап 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <see cref="Property"/> — обычное свойство активности (строка, число, перечисление), не аргумент WF.
    /// </summary>
    internal enum PaletteArgumentDirection
    {
        In,
        Out,
        InOut,
        Property
    }

    /// <summary>Вид контейнера: куда именно внутри активности можно вложить другую активность.</summary>
    internal enum PaletteContainerKind
    {
        /// <summary>Коллекция дочерних активностей (<c>Sequence.Activities</c>, <c>Parallel.Branches</c>).</summary>
        ActivityCollection,

        /// <summary>Свойство-активность (<c>If.Then</c>, <c>While.Body</c>, <c>TryCatch.Try</c>).</summary>
        ActivityChild,

        /// <summary>Обработчик <c>ActivityAction</c>/<c>ActivityAction&lt;T&gt;</c> (<c>ForEach.Body</c>).</summary>
        ActivityAction,

        /// <summary>Узлы <c>Flowchart</c>: в базовом MVP не поддерживаются (нужен <c>FlowStep</c>).</summary>
        FlowchartNodes
    }

    /// <summary>
    /// Метаданные аргумента активности: имя, тип значения, направление, обязательность.
    /// <para>
    /// Те же данные показывают инструменту, что именно можно задать через <c>xaml.set_argument</c>,
    /// и не дают агенту «придумать» имя аргумента: ошибка возвращается до вставки в конструктор.
    /// </para>
    /// </summary>
    internal sealed class PaletteArgumentDescriptor
    {
        internal PaletteArgumentDescriptor(
            string name,
            Type propertyType,
            PaletteArgumentDirection direction,
            Type valueType,
            bool required,
            string defaultValue,
            string description)
        {
            Name = name;
            PropertyType = propertyType;
            Direction = direction;
            ValueType = valueType;
            Required = required;
            DefaultValue = defaultValue;
            Description = description;
        }

        /// <summary>Имя аргумента (как в объектной модели WF: <c>Text</c>, <c>Condition</c>, <c>To</c>).</summary>
        internal string Name { get; }

        /// <summary>Тип свойства активности (<c>InArgument&lt;T&gt;</c>, <c>OutArgument</c>, <c>string</c>).</summary>
        internal Type PropertyType { get; }

        internal PaletteArgumentDirection Direction { get; }

        /// <summary>
        /// Тип значения (для <c>InArgument&lt;T&gt;</c> — <c>T</c>). <c>null</c> у необобщённых
        /// <c>InArgument</c>/<c>OutArgument</c> (<c>Assign.To</c>): тип задаёт вызывающий.
        /// </summary>
        internal Type ValueType { get; }

        /// <summary>Аргумент обязателен (<c>[RequiredArgument]</c>): без него WF даёт ошибку валидации.</summary>
        internal bool Required { get; }

        /// <summary>Значение по умолчанию, если оно объявлено атрибутом (<c>[DefaultValue]</c>).</summary>
        internal string DefaultValue { get; }

        internal string Description { get; }

        /// <summary>Это аргумент-ссылка (<c>Out</c>/<c>InOut</c>): значение обязано быть выражением-ссылкой.</summary>
        internal bool IsReference => Direction == PaletteArgumentDirection.Out
                                     || Direction == PaletteArgumentDirection.InOut;

        /// <summary>Принимает C#-выражение (<see cref="Microsoft.CSharp.Activities.CSharpValue{T}"/>).</summary>
        internal bool AcceptsExpression => Direction != PaletteArgumentDirection.Property;

        /// <summary>
        /// Свойство-выражение (<c>Activity&lt;T&gt;</c>, например <c>While.Condition</c>): значение
        /// подставляется как <c>CSharpValue&lt;T&gt;</c>/<c>Literal&lt;T&gt;</c>, а не как <c>InArgument&lt;T&gt;</c>.
        /// </summary>
        internal bool IsExpressionProperty => ActivityPaletteRegistry.IsExpressionPropertyType(PropertyType);

        internal string TypeName => PaletteTypeNames.NameOf(ValueType);

        internal string DirectionName
        {
            get
            {
                switch (Direction)
                {
                    case PaletteArgumentDirection.In:
                        return "In";
                    case PaletteArgumentDirection.Out:
                        return "Out";
                    case PaletteArgumentDirection.InOut:
                        return "InOut";
                    default:
                        return "Property";
                }
            }
        }

        public override string ToString()
        {
            return $"{Name} ({DirectionName}, {TypeName}{(Required ? ", обязателен" : string.Empty)})";
        }
    }

    /// <summary>Метаданные контейнера: куда вкладывается дочерняя активность.</summary>
    internal sealed class PaletteContainerDescriptor
    {
        internal PaletteContainerDescriptor(
            string name,
            PaletteContainerKind kind,
            Type childValueType,
            string description)
        {
            Name = name;
            Kind = kind;
            ChildValueType = childValueType;
            Description = description;
        }

        /// <summary>Имя свойства-контейнера (<c>Activities</c>, <c>Then</c>, <c>Body</c>).</summary>
        internal string Name { get; }

        internal PaletteContainerKind Kind { get; }

        /// <summary>
        /// Тип вложенной активности: <c>typeof(Activity)</c> для коллекции/свойства, тип параметра
        /// обработчика для <c>ActivityAction&lt;T&gt;</c>, <c>null</c> для необобщённого <c>ActivityAction</c>.
        /// </summary>
        internal Type ChildValueType { get; }

        internal string Description { get; }

        /// <summary>Вложенная активность должна быть обёрнута в <c>FlowStep</c> (пока не поддерживается).</summary>
        internal bool NeedsFlowStep => Kind == PaletteContainerKind.FlowchartNodes;

        internal string KindName
        {
            get
            {
                switch (Kind)
                {
                    case PaletteContainerKind.ActivityCollection:
                        return "коллекция активностей";
                    case PaletteContainerKind.ActivityChild:
                        return "свойство-активность";
                    case PaletteContainerKind.ActivityAction:
                        return "обработчик ActivityAction";
                    default:
                        return "узлы Flowchart";
                }
            }
        }

        internal string ChildTypeName =>
            ChildValueType == null ? "Activity" : PaletteTypeNames.NameOf(ChildValueType);

        public override string ToString()
        {
            return $"{Name} ({KindName})";
        }
    }

    /// <summary>
    /// Описание активности палитры для агента: как называется, какой тип создаётся, какие аргументы
    /// есть и в какие контейнеры её можно вложить.
    /// <para>
    /// Источник — тот же список типов, из которого строится панель «Активности» студии
    /// (<see cref="StudioActivityPalette"/>), поэтому расхождения «в палитре есть, а инструмент не знает»
    /// не возникает (этап 4 плана, п. 3).
    /// </para>
    /// </summary>
    internal sealed class PaletteActivityDescriptor
    {
        internal PaletteActivityDescriptor(
            string key,
            string displayName,
            string category,
            Type type,
            string description,
            IReadOnlyList<PaletteArgumentDescriptor> arguments,
            IReadOnlyList<PaletteContainerDescriptor> containers)
        {
            Key = key;
            DisplayName = displayName;
            Category = category;
            Type = type;
            Description = description;
            Arguments = arguments ?? Array.Empty<PaletteArgumentDescriptor>();
            Containers = containers ?? Array.Empty<PaletteContainerDescriptor>();
        }

        /// <summary>Короткое имя для инструментов: <c>Sequence</c>, <c>ForEach</c>, <c>WriteLine</c>.</summary>
        internal string Key { get; }

        /// <summary>Подпись как в панели «Активности» (у обобщённых — с <c>&lt;T&gt;</c>).</summary>
        internal string DisplayName { get; }

        /// <summary>Категория панели «Активности» (ключ локализации).</summary>
        internal string Category { get; }

        /// <summary>Тип активности (у обобщённых — открытое определение, например <c>ForEach&lt;&gt;</c>).</summary>
        internal Type Type { get; }

        internal string Description { get; }

        internal IReadOnlyList<PaletteArgumentDescriptor> Arguments { get; }

        /// <summary>Контейнеры этой активности: «допустимые контейнеры» из п. 4.1 плана.</summary>
        internal IReadOnlyList<PaletteContainerDescriptor> Containers { get; }

        /// <summary>Обобщённое определение: агенту нужно указать аргументы типа.</summary>
        internal bool IsGeneric => Type.IsGenericTypeDefinition;

        internal int GenericArity => IsGeneric ? Type.GetGenericArguments().Length : 0;

        internal bool CanContainActivities => Containers.Count > 0;

        internal string TypeName => Type.FullName ?? Type.Name;

        internal PaletteArgumentDescriptor FindArgument(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return Arguments.FirstOrDefault(argument =>
                string.Equals(argument.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        internal PaletteContainerDescriptor FindContainer(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return Containers.FirstOrDefault(container =>
                string.Equals(container.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Строка для ответа инструмента: аргументы с типами и обязательностью.</summary>
        internal string DescribeArguments()
        {
            return Arguments.Count == 0
                ? "аргументов нет"
                : "аргументы: " + string.Join(", ", Arguments.Select(argument => argument.ToString()));
        }

        /// <summary>Строка для ответа инструмента: допустимые контейнеры.</summary>
        internal string DescribeContainers()
        {
            return Containers.Count == 0
                ? "дочерние активности не принимает"
                : "контейнеры: " + string.Join(", ", Containers.Select(container => container.ToString()));
        }

        public override string ToString()
        {
            return $"{Key} — {DisplayName}";
        }
    }

    /// <summary>Категория палитры: ключ локализации панели «Активности» и набор типов.</summary>
    internal sealed class ActivityPaletteCategory
    {
        internal ActivityPaletteCategory(string key, IEnumerable<Type> types)
        {
            Key = key;
            Types = (types ?? Enumerable.Empty<Type>()).Where(type => type != null).ToArray();
        }

        internal string Key { get; }

        internal IReadOnlyList<Type> Types { get; }
    }

    /// <summary>
    /// Реестр палитры: метаданные активностей, построенные по объектной модели WF
    /// (этап 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Почему по рефлексии, а не вторым списком «как в палитре»: аргументы, направления и контейнеры
    /// обязаны совпадать с реальными типами активностей. Второй список рано или поздно разойдётся
    /// с типами, и агент начнёт получать «валидные» по описанию, но неработающие вызовы.
    /// </para>
    /// </summary>
    internal sealed class ActivityPaletteRegistry
    {
        private static readonly Lazy<ActivityPaletteRegistry> DefaultRegistry =
            new Lazy<ActivityPaletteRegistry>(() => new ActivityPaletteRegistry(StudioActivityPalette.Categories()));

        private readonly Dictionary<string, PaletteActivityDescriptor> _byKey =
            new Dictionary<string, PaletteActivityDescriptor>(StringComparer.OrdinalIgnoreCase);

        private readonly List<PaletteActivityDescriptor> _descriptors = new List<PaletteActivityDescriptor>();
        private readonly List<string> _categories = new List<string>();

        internal ActivityPaletteRegistry(IEnumerable<ActivityPaletteCategory> categories)
        {
            foreach (ActivityPaletteCategory category in categories ?? Enumerable.Empty<ActivityPaletteCategory>())
            {
                if (string.IsNullOrWhiteSpace(category.Key))
                {
                    continue;
                }

                if (!_categories.Contains(category.Key))
                {
                    _categories.Add(category.Key);
                }

                foreach (Type type in category.Types)
                {
                    Add(type, category.Key);
                }
            }
        }

        /// <summary>Реестр студии: встроенные активности WF плюс уже загруженные библиотеки активностей.</summary>
        internal static ActivityPaletteRegistry Default => DefaultRegistry.Value;

        /// <summary>Все активности палитры (по одной на имя, в порядке категорий).</summary>
        internal IReadOnlyList<PaletteActivityDescriptor> All => _descriptors;

        /// <summary>Активности, принимающие дочерние активности (их имена годятся в <c>parentId</c>).</summary>
        internal IReadOnlyList<PaletteActivityDescriptor> Containers =>
            _descriptors.Where(descriptor => descriptor.CanContainActivities).ToArray();

        internal IReadOnlyList<string> Categories => _categories;

        internal IReadOnlyList<PaletteActivityDescriptor> InCategory(string categoryKey)
        {
            return _descriptors
                .Where(descriptor => string.Equals(descriptor.Category, categoryKey, StringComparison.Ordinal))
                .ToArray();
        }

        /// <summary>
        /// Поиск активности по имени: <c>WriteLine</c>, <c>ForEach&lt;&gt;</c>, <c>ForEach`1</c>,
        /// <c>System.Activities.Statements.WriteLine</c> — всё находит одну и ту же активность.
        /// </summary>
        internal PaletteActivityDescriptor Find(string key)
        {
            string normalized = NormalizeKey(key);
            if (normalized == null)
            {
                return null;
            }

            return _byKey.TryGetValue(normalized, out PaletteActivityDescriptor descriptor) ? descriptor : null;
        }

        /// <summary>Описание типа активности (для реестра и для тестов).</summary>
        internal PaletteActivityDescriptor Describe(Type type, string category = null)
        {
            if (type == null || !typeof(Activity).IsAssignableFrom(type))
            {
                return null;
            }

            var arguments = new List<PaletteArgumentDescriptor>();
            var containers = new List<PaletteContainerDescriptor>();

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                PaletteArgumentDescriptor argument = DescribeArgument(property);
                if (argument != null)
                {
                    arguments.Add(argument);
                    continue;
                }

                PaletteContainerDescriptor container = DescribeContainer(property);
                if (container != null)
                {
                    containers.Add(container);
                    continue;
                }

                PaletteArgumentDescriptor plain = DescribePlainProperty(property);
                if (plain != null)
                {
                    arguments.Add(plain);
                }
            }

            return new PaletteActivityDescriptor(
                KeyOf(type),
                DisplayNameOf(type),
                category,
                type,
                DescriptionOf(type),
                arguments,
                containers);
        }

        /// <summary>Все известные имена — для подсказки в ответе на неизвестную активность.</summary>
        internal string KnownActivities(int limit = 40)
        {
            IEnumerable<string> names = _descriptors.Select(descriptor => descriptor.Key).OrderBy(name => name);
            return string.Join(", ", names.Take(limit));
        }

        /// <summary>Строка «активность с её аргументами и контейнерами» для ответа инструмента.</summary>
        internal string DescribeActivity(PaletteActivityDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return string.Empty;
            }

            return $"{descriptor.Key} ({descriptor.TypeName}): {descriptor.DescribeArguments()}; " +
                   descriptor.DescribeContainers();
        }

        /// <summary>Короткое имя типа без арности: <c>ForEach`1</c> → <c>ForEach</c>.</summary>
        internal static string KeyOf(Type type)
        {
            string name = type.Name;
            int tick = name.IndexOf('`');

            return tick > 0 ? name.Substring(0, tick) : name;
        }

        /// <summary>Подпись как в панели «Активности»: <c>[DisplayName]</c> или имя типа, <c>ForEach&lt;T&gt;</c>.</summary>
        internal static string DisplayNameOf(Type type)
        {
            string name = KeyOf(type);
            var attribute = type.GetCustomAttribute<DisplayNameAttribute>(true);

            if (attribute != null && !string.IsNullOrWhiteSpace(attribute.DisplayName))
            {
                name = attribute.DisplayName;
            }

            return type.IsGenericTypeDefinition ? name + "<T>" : name;
        }

        private static string DescriptionOf(Type type)
        {
            var attribute = type.GetCustomAttribute<DescriptionAttribute>(true);
            return attribute?.Description;
        }

        private void Add(Type type, string category)
        {
            PaletteActivityDescriptor descriptor = Describe(type, category);
            if (descriptor == null || _byKey.ContainsKey(descriptor.Key))
            {
                // Одно имя — одна активность: дубликат из другой категории не должен перекрывать описание.
                return;
            }

            _byKey.Add(descriptor.Key, descriptor);
            _descriptors.Add(descriptor);
        }

        private static PaletteArgumentDescriptor DescribeArgument(PropertyInfo property)
        {
            Type propertyType = property.PropertyType;

            // Условия While/DoWhile объявлены как Activity<bool>, а не InArgument<bool>. Без этой ветки
            // JSON AST не мог задать условие цикла: аргумент «Condition» считался несуществующим.
            if (IsExpressionPropertyType(propertyType))
            {
                return new PaletteArgumentDescriptor(
                    property.Name,
                    propertyType,
                    PaletteArgumentDirection.In,
                    propertyType.GetGenericArguments()[0],
                    required: property.GetCustomAttributes(typeof(RequiredArgumentAttribute), true).Any(),
                    property.GetCustomAttribute<DefaultValueAttribute>(true)?.Value?.ToString(),
                    property.GetCustomAttribute<DescriptionAttribute>(true)?.Description);
            }

            if (!typeof(Argument).IsAssignableFrom(propertyType))
            {
                return null;
            }

            PaletteArgumentDirection direction;
            if (typeof(InOutArgument).IsAssignableFrom(propertyType))
            {
                direction = PaletteArgumentDirection.InOut;
            }
            else if (typeof(OutArgument).IsAssignableFrom(propertyType))
            {
                direction = PaletteArgumentDirection.Out;
            }
            else
            {
                direction = PaletteArgumentDirection.In;
            }

            bool required = property.GetCustomAttributes(typeof(RequiredArgumentAttribute), true).Any();
            var defaultAttribute = property.GetCustomAttribute<DefaultValueAttribute>(true);
            var descriptionAttribute = property.GetCustomAttribute<DescriptionAttribute>(true);

            return new PaletteArgumentDescriptor(
                property.Name,
                propertyType,
                direction,
                ValueTypeOf(propertyType),
                required,
                defaultAttribute?.Value?.ToString(),
                descriptionAttribute?.Description);
        }

        private static PaletteContainerDescriptor DescribeContainer(PropertyInfo property)
        {
            Type propertyType = property.PropertyType;

            if (typeof(Collection<Activity>).IsAssignableFrom(propertyType))
            {
                return new PaletteContainerDescriptor(
                    property.Name,
                    PaletteContainerKind.ActivityCollection,
                    typeof(Activity),
                    "коллекция дочерних активностей");
            }

            if (propertyType == typeof(Activity))
            {
                return new PaletteContainerDescriptor(
                    property.Name,
                    PaletteContainerKind.ActivityChild,
                    typeof(Activity),
                    "одна дочерняя активность");
            }

            if (propertyType == typeof(ActivityAction))
            {
                return new PaletteContainerDescriptor(
                    property.Name,
                    PaletteContainerKind.ActivityAction,
                    null,
                    "обработчик без параметра");
            }

            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(ActivityAction<>))
            {
                Type argumentType = propertyType.GetGenericArguments()[0];
                return new PaletteContainerDescriptor(
                    property.Name,
                    PaletteContainerKind.ActivityAction,
                    argumentType,
                    $"обработчик с параметром {PaletteTypeNames.NameOf(argumentType)}");
            }

            if (string.Equals(property.Name, "Nodes", StringComparison.OrdinalIgnoreCase)
                && typeof(IEnumerable<FlowNode>).IsAssignableFrom(propertyType))
            {
                return new PaletteContainerDescriptor(
                    property.Name,
                    PaletteContainerKind.FlowchartNodes,
                    typeof(Activity),
                    "узлы блок-схемы (в базовом MVP не поддерживаются)");
            }

            return null;
        }

        private static PaletteArgumentDescriptor DescribePlainProperty(PropertyInfo property)
        {
            if (!property.CanWrite || property.GetSetMethod() == null)
            {
                return null;
            }

            Type propertyType = property.PropertyType;
            if (!PaletteTypeNames.IsSimple(propertyType))
            {
                return null;
            }

            // Подпись задаётся отдельным полем инструмента, а Id и служебные свойства менять нельзя.
            if (string.Equals(property.Name, "DisplayName", StringComparison.Ordinal)
                || string.Equals(property.Name, "Id", StringComparison.Ordinal))
            {
                return null;
            }

            return new PaletteArgumentDescriptor(
                property.Name,
                propertyType,
                PaletteArgumentDirection.Property,
                propertyType,
                required: false,
                property.GetCustomAttribute<DefaultValueAttribute>(true)?.Value?.ToString(),
                property.GetCustomAttribute<DescriptionAttribute>(true)?.Description);
        }

        /// <summary>
        /// Свойство-выражение: <see cref="Activity{T}"/> (условие <c>While</c>/<c>DoWhile</c>).
        /// Значение такого свойства — <c>CSharpValue&lt;T&gt;</c> или <c>Literal&lt;T&gt;</c>,
        /// а не <c>InArgument&lt;T&gt;</c>.
        /// </summary>
        internal static bool IsExpressionPropertyType(Type propertyType)
        {
            return propertyType != null
                   && propertyType.IsGenericType
                   && propertyType.GetGenericTypeDefinition() == typeof(Activity<>);
        }


        private static Type ValueTypeOf(Type argumentType)
        {
            if (!argumentType.IsGenericType)
            {
                // Необобщённые InArgument/OutArgument (Assign.To/Value): тип значения задаёт вызывающий.
                return null;
            }

            Type definition = argumentType.GetGenericTypeDefinition();
            if (definition == typeof(InArgument<>)
                || definition == typeof(OutArgument<>)
                || definition == typeof(InOutArgument<>))
            {
                return argumentType.GetGenericArguments()[0];
            }

            return null;
        }

        /// <summary>Приводит любое написание имени активности к ключу реестра.</summary>
        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string value = key.Trim();

            int bracket = value.IndexOf('<');
            if (bracket > 0)
            {
                value = value.Substring(0, bracket);
            }

            int tick = value.IndexOf('`');
            if (tick > 0)
            {
                value = value.Substring(0, tick);
            }

            int dot = value.LastIndexOf('.');
            if (dot >= 0 && dot < value.Length - 1)
            {
                value = value.Substring(dot + 1);
            }

            value = value.Trim();
            return value.Length == 0 ? null : value;
        }
    }

    /// <summary>
    /// Единственный источник состава палитры студии (этап 4 плана, п. 3): из него строится и панель
    /// «Активности» (<c>WFToolboxHelper</c>), и реестр инструментов <c>xaml.*</c>.
    /// </summary>
    internal static class StudioActivityPalette
    {
        /// <summary>Ключи категорий — те же, что использует панель «Активности».</summary>
        internal const string FlowCategory = "lang_toolbox_flow";

        internal const string DebugCategory = "lang_main_debug";
        internal const string ControlFlowCategory = "lang_toolbox_control_flow";
        internal const string FlowchartCategory = "lang_toolbox_flowchart";
        internal const string StateMachineCategory = "lang_toolbox_state_machine";
        internal const string PrimitivesCategory = "lang_toolbox_primitives";
        internal const string ErrorHandlingCategory = "lang_toolbox_error_handling";
        internal const string BrowserCategory = "lang_toolbox_browser";
        internal const string OsCategory = "lang_toolbox_os";

        /// <summary>
        /// Библиотеки активностей студии: подключаются, только если уже загружены. Принудительная
        /// загрузка (как в панели) тянет нативные зависимости и не годится для MCP-потока.
        /// </summary>
        private static readonly string[] LibraryAssemblyNames =
        {
            "Activities.Flow",
            "Activities.Debug",
            "Activities.OS",
            "Activities.Browser"
        };

        /// <summary>
        /// Встроенные категории палитры (активности WF без библиотек студии).
        /// <para>
        /// Список совпадает с панелью «Активности», поэтому в нём есть и элементы диаграммы
        /// (<c>FlowDecision</c>, <c>FlowSwitch</c>, <c>State</c>): они не являются
        /// <see cref="Activity"/> и в реестре не описываются — создать их отдельной активностью нельзя,
        /// они существуют только внутри <c>Flowchart</c>/<c>StateMachine</c>.
        /// </para>
        /// </summary>
        internal static IReadOnlyList<ActivityPaletteCategory> BuiltinCategories()
        {
            return new[]
            {
                new ActivityPaletteCategory(ControlFlowCategory, new[]
                {
                    typeof(DoWhile),
                    typeof(ForEach<>),
                    typeof(If),
                    typeof(Sequence),
                    typeof(Switch<>),
                    typeof(While),
                    typeof(Parallel),
                    typeof(Pick)
                }),
                new ActivityPaletteCategory(FlowchartCategory, new[]
                {
                    typeof(Flowchart),
                    typeof(FlowDecision),
                    typeof(FlowSwitch<>)
                }),
                new ActivityPaletteCategory(StateMachineCategory, new[]
                {
                    typeof(StateMachine),
                    typeof(State)
                }),
                new ActivityPaletteCategory(PrimitivesCategory, new[]
                {
                    typeof(Assign),
                    typeof(Delay),
                    typeof(InvokeMethod),
                    typeof(WriteLine)
                }),
                // Коллекции в панели лежат в категории «Блок-схема» — ключ сохранён как есть.
                new ActivityPaletteCategory(FlowchartCategory, new[]
                {
                    typeof(AddToCollection<>),
                    typeof(ClearCollection<>),
                    typeof(ExistsInCollection<>),
                    typeof(RemoveFromCollection<>)
                }),
                new ActivityPaletteCategory(ErrorHandlingCategory, new[]
                {
                    typeof(Rethrow),
                    typeof(Throw),
                    typeof(TryCatch)
                })
            };
        }

        /// <summary>Полный состав палитры: встроенные категории плюс уже загруженные библиотеки студии.</summary>
        internal static IReadOnlyList<ActivityPaletteCategory> Categories()
        {
            var categories = new List<ActivityPaletteCategory>(BuiltinCategories());

            foreach (string assemblyName in LibraryAssemblyNames)
            {
                ActivityPaletteCategory category = CategoryFromLoadedAssembly(CategoryKeyOf(assemblyName), assemblyName);
                if (category != null)
                {
                    categories.Add(category);
                }
            }

            return categories;
        }

        /// <summary>Активности уже загруженной библиотеки (если её нет в домене — <c>null</c>).</summary>
        internal static ActivityPaletteCategory CategoryFromLoadedAssembly(string categoryKey, string assemblyName)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(loaded => string.Equals(loaded.GetName().Name, assemblyName, StringComparison.Ordinal));

            return assembly == null ? null : CategoryFromAssembly(categoryKey, assembly);
        }

        /// <summary>Активности сборки в виде категории палитры (используется панелью «Активности»).</summary>
        internal static ActivityPaletteCategory CategoryFromAssembly(string categoryKey, Assembly assembly)
        {
            return assembly == null ? null : new ActivityPaletteCategory(categoryKey, ActivityTypesOf(assembly));
        }

        /// <summary>
        /// Типы активностей сборки так, как их показывает панель: неабстрактные наследники
        /// <see cref="Activity"/>, по имени. Ошибки загрузки типов не должны ломать палитру.
        /// </summary>
        internal static IReadOnlyList<Type> ActivityTypesOf(Assembly assembly)
        {
            if (assembly == null)
            {
                return Array.Empty<Type>();
            }

            try
            {
                return assembly.GetTypes()
                    .Where(type => type.IsSubclassOf(typeof(Activity)) && !type.IsAbstract)
                    .OrderBy(type => type.Name, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types
                    .Where(type => type != null && type.IsSubclassOf(typeof(Activity)) && !type.IsAbstract)
                    .OrderBy(type => type.Name, StringComparer.Ordinal)
                    .ToArray();
            }
        }

        internal static string CategoryKeyOf(string assemblyName)
        {
            switch (assemblyName)
            {
                case "Activities.Flow":
                    return FlowCategory;
                case "Activities.Debug":
                    return DebugCategory;
                case "Activities.OS":
                    return OsCategory;
                case "Activities.Browser":
                    return BrowserCategory;
                default:
                    return FlowCategory;
            }
        }
    }

    /// <summary>
    /// Имена типов для инструментов палитры: агент пишет <c>String</c>/<c>Int32</c>, а не
    /// <c>System.String</c>. Список намеренно короткий: типы вне него не должны попадать в XAML.
    /// </summary>
    internal static class PaletteTypeNames
    {
        private static readonly Dictionary<string, Type> KnownTypes =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                { "String", typeof(string) },
                { "Int32", typeof(int) },
                { "Int64", typeof(long) },
                { "Double", typeof(double) },
                { "Decimal", typeof(decimal) },
                { "Boolean", typeof(bool) },
                { "DateTime", typeof(DateTime) },
                { "TimeSpan", typeof(TimeSpan) },
                { "Object", typeof(object) },
                { "String[]", typeof(string[]) },
                { "List<String>", typeof(List<string>) }
            };

        /// <summary>Короткое имя типа для описаний и ответов инструментов.</summary>
        internal static string NameOf(Type type)
        {
            if (type == null)
            {
                return "Object";
            }

            foreach (KeyValuePair<string, Type> pair in KnownTypes)
            {
                if (pair.Value == type)
                {
                    return pair.Key;
                }
            }

            if (type.IsArray)
            {
                return NameOf(type.GetElementType()) + "[]";
            }

            if (type.IsGenericType)
            {
                string name = type.Name;
                int tick = name.IndexOf('`');
                if (tick > 0)
                {
                    name = name.Substring(0, tick);
                }

                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(NameOf)) + ">";
            }

            return type.Name;
        }

        internal static bool TryResolve(string name, out Type type)
        {
            type = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return KnownTypes.TryGetValue(name.Trim(), out type);
        }

        /// <summary>Тип по имени или <paramref name="fallback"/> (с подсказкой, какие имена известны).</summary>
        internal static Type Resolve(string name, Type fallback)
        {
            return TryResolve(name, out Type type) ? type : fallback;
        }

        /// <summary>Все известные имена — для подсказки в ответе инструмента.</summary>
        internal static string KnownNames()
        {
            return string.Join(", ", KnownNamesList);
        }

        /// <summary>
        /// Whitelist типов переменных и аргументов типа (этап 6 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>):
        /// строгая проверка JSON AST идёт до построения модели, поэтому тип вне списка в XAML не попадает.
        /// </summary>
        internal static IReadOnlyList<string> KnownNamesList =>
            KnownTypes.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

        internal static bool IsSimple(Type type)
        {
            if (type == null)
            {
                return false;
            }

            return type.IsEnum
                   || type == typeof(string)
                   || type == typeof(bool)
                   || type == typeof(int)
                   || type == typeof(long)
                   || type == typeof(double)
                   || type == typeof(decimal)
                   || type == typeof(DateTime)
                   || type == typeof(TimeSpan);
        }
    }
}
