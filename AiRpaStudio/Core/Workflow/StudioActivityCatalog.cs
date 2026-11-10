using System;
using System.Collections.Generic;
using System.Linq;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Каталог активностей студии для агента (карточка <c>AG-01</c>): машинночитаемый справочник
    /// вместо угадывания XAML.
    /// <para>
    /// Источник данных — <see cref="ActivityPaletteRegistry"/>, то есть тот же список типов, из которого
    /// строится панель «Активности» и реестр инструментов <c>xaml.*</c>. Второй список разошёлся бы
    /// с типами, и агент получал бы «валидные» по описанию, но неработающие вызовы.
    /// </para>
    /// <para>
    /// Реализация живёт в студии (у неё есть WF-типы и библиотеки активностей), чат видит только
    /// <see cref="IActivityCatalog"/>: поэтому инструменты <c>activity_search</c>/<c>activity_describe</c>
    /// тестируются без UI.
    /// </para>
    /// </summary>
    internal sealed class StudioActivityCatalog : IActivityCatalog
    {
        /// <summary>Предел результатов поиска, если инструмент не задал свой.</summary>
        internal const int DefaultSearchLimit = 20;

        private static Lazy<StudioActivityCatalog> SharedCatalog =
            new Lazy<StudioActivityCatalog>(() => new StudioActivityCatalog(StudioActivityPalette.Categories()));

        /// <summary>
        /// Читаемые имена категорий: ключи панели «Активности» — это ключи локализации
        /// (<c>lang_toolbox_control_flow</c>), агенту нужен человеческий текст для фильтра.
        /// </summary>
        private static readonly Dictionary<string, string> CategoryNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { StudioActivityPalette.FlowCategory, "Flow" },
                { StudioActivityPalette.DebugCategory, "Debug" },
                { StudioActivityPalette.ControlFlowCategory, "Control Flow" },
                { StudioActivityPalette.FlowchartCategory, "Flowchart" },
                { StudioActivityPalette.CollectionCategory, "Collection" },
                { StudioActivityPalette.StateMachineCategory, "State Machine" },
                { StudioActivityPalette.PrimitivesCategory, "Primitives" },
                { StudioActivityPalette.ErrorHandlingCategory, "Error Handling" },

                { StudioActivityPalette.OsCategory, "OS" }
            };

        private readonly List<ActivityInfo> _activities = new List<ActivityInfo>();
        private readonly List<string> _categories = new List<string>();
        private readonly HashSet<string> _addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ActivityInfo> _lookup =
            new Dictionary<string, ActivityInfo>(StringComparer.OrdinalIgnoreCase);

        internal StudioActivityCatalog(IEnumerable<ActivityPaletteCategory> categories)
        {
            var registry = new ActivityPaletteRegistry(categories);

            // Архитектурное требование: каталог агента обязан покрывать весь тулбокс без исключений.
            // Обходим те же категории и типы, которые читает панель «Активности»; настоящие Activity
            // описываются реестром, элементы диаграммы (FlowDecision/FlowSwitch/State/FinalState/PickBranch) —
            // информационными записями, которые нельзя вставить отдельной активностью.
            foreach (ActivityPaletteCategory category in categories ?? Enumerable.Empty<ActivityPaletteCategory>())
            {
                if (category == null || string.IsNullOrWhiteSpace(category.Key))
                {
                    continue;
                }

                foreach (Type type in category.Types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    PaletteActivityDescriptor descriptor = registry.Describe(type, category.Key);
                    ActivityInfo info = descriptor != null
                        ? Map(descriptor)
                        : MapDiagramElement(type, category.Key);

                    if (info == null || !_addedKeys.Add(info.Key))
                    {
                        continue;
                    }

                    _activities.Add(info);

                    if (!_categories.Contains(info.Category))
                    {
                        _categories.Add(info.Category);
                    }

                    Index(info);
                }
            }
        }

        /// <summary>Каталог студии: встроенные активности WF плюс уже загруженные библиотеки активностей.</summary>
        internal static StudioActivityCatalog Default => SharedCatalog.Value;

        /// <summary>
        /// Сброс кэша каталога агента (карточка <c>AG-08</c>): каталог — <c>Lazy</c> на процесс, поэтому
        /// без сброса <c>activity_search</c> не увидит активность, созданную и собранную только что.
        /// </summary>
        internal static void InvalidateDefault()
        {
            SharedCatalog = new Lazy<StudioActivityCatalog>(() => new StudioActivityCatalog(StudioActivityPalette.Categories()));
        }

        public int Count => _activities.Count;

        public IReadOnlyList<string> Categories => _categories;

        public IReadOnlyList<ActivityInfo> Search(string query, string category = null, int limit = 0)
        {
            IEnumerable<ActivityInfo> items = _activities;

            string categoryFilter = (category ?? string.Empty).Trim();
            if (categoryFilter.Length > 0)
            {
                items = items.Where(info => ContainsIgnoreCase(info.Category, categoryFilter)
                                            || ContainsIgnoreCase(info.CategoryKey, categoryFilter));
            }

            string[] tokens = (query ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                string current = token;
                items = items.Where(info => Matches(info, current));
            }

            ActivityInfo[] found = items.ToArray();
            int effectiveLimit = limit > 0 ? limit : DefaultSearchLimit;

            return found.Length > effectiveLimit ? found.Take(effectiveLimit).ToArray() : found;
        }

        public ActivityInfo Describe(string typeName)
        {
            string normalized = NormalizeName(typeName);
            if (normalized == null)
            {
                return null;
            }

            ActivityInfo info;
            if (_lookup.TryGetValue(normalized, out info))
            {
                return info;
            }

            // Полное имя типа с арностью (<c>System.Activities.Statements.ForEach`1</c>) нормализуется
            // до ключа, но и «сырое» написание проверяем: каталог может знать тип без суффикса.
            return _lookup.TryGetValue((typeName ?? string.Empty).Trim(), out info) ? info : null;
        }

        /// <summary>Строка «активности каталога списком» — для подсказки в ответе инструмента.</summary>
        internal string KnownActivities(int limit = 40)
        {
            return string.Join(", ", _activities.Select(info => info.Key).Take(limit));
        }

        private void Index(ActivityInfo info)
        {
            AddLookup(info.Key, info);
            AddLookup(info.TypeName, info);
            AddLookup(info.DisplayName, info);

            int bracket = info.DisplayName.IndexOf('<');
            if (bracket > 0)
            {
                AddLookup(info.DisplayName.Substring(0, bracket), info);
            }
        }

        private void AddLookup(string name, ActivityInfo info)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string key = name.Trim();
            if (!_lookup.ContainsKey(key))
            {
                _lookup.Add(key, info);
            }
        }

        private static ActivityInfo Map(PaletteActivityDescriptor descriptor)
        {
            ActivityArgumentInfo[] arguments = descriptor.Arguments
                .Select(argument => new ActivityArgumentInfo(
                    argument.Name,
                    DirectionOf(argument.Direction),
                    argument.TypeName,
                    argument.Required,
                    argument.DefaultValue,
                    argument.Description))
                .ToArray();

            ActivityContainerInfo[] containers = descriptor.Containers
                .Select(container => new ActivityContainerInfo(
                    container.Name,
                    KindOf(container.Kind),
                    container.ChildTypeName,
                    container.Description))
                .ToArray();

            return new ActivityInfo(
                descriptor.Key,
                descriptor.TypeName,
                descriptor.DisplayName,
                CategoryNameOf(descriptor.Category),
                descriptor.Category,
                descriptor.Description,
                descriptor.IsGeneric,
                descriptor.GenericArity,
                arguments,
                containers);
        }

        /// <summary>
        /// Информационная запись элемента диаграммы тулбокса (<c>FlowDecision</c>, <c>FlowSwitch</c>,
        /// <c>State</c>, <c>FinalState</c>, <c>PickBranch</c>): агент видит её в каталоге, но не может
        /// вставить отдельной активностью — такие элементы создаются только внутри своего контейнера.
        /// </summary>
        private static ActivityInfo MapDiagramElement(Type type, string categoryKey)
        {
            return new ActivityInfo(
                ActivityPaletteRegistry.KeyOf(type),
                type.FullName ?? type.Name,
                ActivityPaletteRegistry.DisplayNameOf(type),
                CategoryNameOf(categoryKey),
                categoryKey,
                "Элемент диаграммы: отдельной активностью не вставляется, создаётся только внутри Flowchart/StateMachine/Pick.",
                type.IsGenericTypeDefinition,
                type.IsGenericTypeDefinition ? type.GetGenericArguments().Length : 0,
                null,
                null,
                isStandaloneActivity: false);
        }

        private static string CategoryNameOf(string categoryKey)
        {
            if (string.IsNullOrWhiteSpace(categoryKey))
            {
                return string.Empty;
            }

            string name;
            return CategoryNames.TryGetValue(categoryKey.Trim(), out name) ? name : categoryKey.Trim();
        }

        private static ActivityArgumentDirection DirectionOf(PaletteArgumentDirection direction)
        {
            switch (direction)
            {
                case PaletteArgumentDirection.In:
                    return ActivityArgumentDirection.In;
                case PaletteArgumentDirection.Out:
                    return ActivityArgumentDirection.Out;
                case PaletteArgumentDirection.InOut:
                    return ActivityArgumentDirection.InOut;
                default:
                    return ActivityArgumentDirection.Property;
            }
        }

        private static string KindOf(PaletteContainerKind kind)
        {
            switch (kind)
            {
                case PaletteContainerKind.ActivityCollection:
                    return "collection";
                case PaletteContainerKind.ActivityChild:
                    return "child";
                case PaletteContainerKind.ActivityAction:
                    return "action";
                default:
                    return "flowchart-nodes";
            }
        }

        private static bool Matches(ActivityInfo info, string token)
        {
            return ContainsIgnoreCase(info.Key, token)
                   || ContainsIgnoreCase(info.TypeName, token)
                   || ContainsIgnoreCase(info.DisplayName, token)
                   || ContainsIgnoreCase(info.Category, token)
                   || ContainsIgnoreCase(info.CategoryKey, token)
                   || ContainsIgnoreCase(info.Description, token);
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                   && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Любое написание имени активности приводит к ключу: <c>ForEach&lt;T&gt;</c>,
        /// <c>ForEach`1</c>, <c>System.Activities.Statements.ForEach</c> → <c>ForEach</c>.
        /// </summary>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string value = name.Trim();

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
}
