using System;
using System.Collections.Generic;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Направление аргумента активности (карточка <c>AG-01</c>: каталог активностей как данные).
    /// <para>
    /// <see cref="Property"/> — обычное свойство активности (строка, число, перечисление), не аргумент WF.
    /// </para>
    /// </summary>
    public enum ActivityArgumentDirection
    {
        In,
        Out,
        InOut,
        Property
    }

    /// <summary>
    /// Машинночитаемое описание аргумента активности: имя, направление, тип, обязательность, значение
    /// по умолчанию, пояснение. Агент получает его вместо угадывания XAML.
    /// </summary>
    public sealed class ActivityArgumentInfo
    {
        public ActivityArgumentInfo(
            string name,
            ActivityArgumentDirection direction,
            string typeName,
            bool required = false,
            string defaultValue = null,
            string description = null)
        {
            Name = name ?? string.Empty;
            Direction = direction;
            TypeName = string.IsNullOrWhiteSpace(typeName) ? "Object" : typeName;
            Required = required;
            DefaultValue = defaultValue;
            Description = description;
        }

        /// <summary>Имя аргумента в объектной модели WF (<c>Text</c>, <c>Condition</c>, <c>To</c>).</summary>
        public string Name { get; }

        public ActivityArgumentDirection Direction { get; }

        /// <summary><c>In</c>, <c>Out</c>, <c>InOut</c> или <c>Property</c>.</summary>
        public string DirectionName => Direction.ToString();

        /// <summary>Тип значения в коротком виде (<c>String</c>, <c>Int32</c>, <c>Object</c>).</summary>
        public string TypeName { get; }

        /// <summary>Аргумент обязателен (<c>[RequiredArgument]</c>): без него WF даёт ошибку валидации.</summary>
        public bool Required { get; }

        public string DefaultValue { get; }

        public string Description { get; }

        /// <summary>Значение обязано быть выражением-ссылкой (<c>Out</c>/<c>InOut</c>).</summary>
        public bool IsReference =>
            Direction == ActivityArgumentDirection.Out || Direction == ActivityArgumentDirection.InOut;

        public override string ToString()
        {
            return $"{Name} ({DirectionName}, {TypeName}{(Required ? ", обязателен" : string.Empty)})";
        }
    }

    /// <summary>
    /// Контейнер активности: свойство, в которое вкладывается дочерняя активность
    /// (<c>Sequence.Activities</c>, <c>If.Then</c>, <c>ForEach.Body</c>).
    /// </summary>
    public sealed class ActivityContainerInfo
    {
        public ActivityContainerInfo(string name, string kind, string childTypeName, string description = null)
        {
            Name = name ?? string.Empty;
            Kind = string.IsNullOrWhiteSpace(kind) ? "child" : kind;
            ChildTypeName = string.IsNullOrWhiteSpace(childTypeName) ? "Activity" : childTypeName;
            Description = description;
        }

        /// <summary>Имя свойства-контейнера.</summary>
        public string Name { get; }

        /// <summary>
        /// <c>collection</c> (коллекция дочерних активностей), <c>child</c> (одна активность),
        /// <c>action</c> (<c>ActivityAction</c>) или <c>flowchart-nodes</c> (узлы блок-схемы).
        /// </summary>
        public string Kind { get; }

        /// <summary>Тип вложенной активности (<c>Activity</c>, <c>Exception</c> для обработчика).</summary>
        public string ChildTypeName { get; }

        public string Description { get; }

        public override string ToString()
        {
            return $"{Name} ({Kind}, {ChildTypeName})";
        }
    }

    /// <summary>
    /// Описание активности каталога: ключ, тип, подпись, категория, аргументы и контейнеры.
    /// <para>
    /// Данные строятся по реальным типам активностей, а не по документации: каталог и панель
    /// «Активности» студии берут состав из одного источника, поэтому «в палитре есть, а инструмент
    /// не знает» не возникает.
    /// </para>
    /// </summary>
    public sealed class ActivityInfo
    {
        private static readonly ActivityArgumentInfo[] NoArguments = new ActivityArgumentInfo[0];
        private static readonly ActivityContainerInfo[] NoContainers = new ActivityContainerInfo[0];

        public ActivityInfo(
            string key,
            string typeName,
            string displayName,
            string category,
            string categoryKey,
            string description = null,
            bool isGeneric = false,
            int genericArity = 0,
            IReadOnlyList<ActivityArgumentInfo> arguments = null,
            IReadOnlyList<ActivityContainerInfo> containers = null)
        {
            Key = key ?? string.Empty;
            TypeName = typeName ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Key : displayName;
            Category = category ?? string.Empty;
            CategoryKey = categoryKey ?? string.Empty;
            Description = description;
            IsGeneric = isGeneric;
            GenericArity = genericArity;
            Arguments = arguments ?? NoArguments;
            Containers = containers ?? NoContainers;
        }

        /// <summary>Короткое имя активности: <c>Sequence</c>, <c>WriteLine</c>, <c>ForEach</c>.</summary>
        public string Key { get; }

        /// <summary>Полное имя типа: <c>System.Activities.Statements.Sequence</c>.</summary>
        public string TypeName { get; }

        /// <summary>Подпись как в панели «Активности» (у обобщённых — с <c>&lt;T&gt;</c>).</summary>
        public string DisplayName { get; }

        /// <summary>Читаемое имя категории (<c>Control Flow</c>) — по нему агент фильтрует поиск.</summary>
        public string Category { get; }

        /// <summary>Ключ локализации категории панели «Активности» (<c>lang_toolbox_control_flow</c>).</summary>
        public string CategoryKey { get; }

        public string Description { get; }

        /// <summary>Обобщённое определение: агенту нужно указать аргументы типа.</summary>
        public bool IsGeneric { get; }

        public int GenericArity { get; }

        public IReadOnlyList<ActivityArgumentInfo> Arguments { get; }

        public IReadOnlyList<ActivityContainerInfo> Containers { get; }

        /// <summary>Активность принимает дочерние активности.</summary>
        public bool CanContainActivities => Containers.Count > 0;

        /// <summary>Аргумент по имени (без учёта регистра) или <c>null</c>.</summary>
        public ActivityArgumentInfo FindArgument(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string trimmed = name.Trim();
            foreach (ActivityArgumentInfo argument in Arguments)
            {
                if (string.Equals(argument.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return argument;
                }
            }

            return null;
        }

        public override string ToString()
        {
            return $"{Key} ({TypeName})";
        }
    }
}
