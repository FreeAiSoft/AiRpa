using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using RPA.Logging;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Направление аргумента активности (этап 4 конвейера генерации XAML).
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
    /// (этап 4 конвейера генерации XAML).
    /// <para>
    /// Почему по рефлексии, а не вторым списком «как в палитре»: аргументы, направления и контейнеры
    /// обязаны совпадать с реальными типами активностей. Второй список рано или поздно разойдётся
    /// с типами, и агент начнёт получать «валидные» по описанию, но неработающие вызовы.
    /// </para>
    /// </summary>
    internal sealed class ActivityPaletteRegistry
    {
        private static Lazy<ActivityPaletteRegistry> DefaultRegistry =
            new Lazy<ActivityPaletteRegistry>(() => new ActivityPaletteRegistry(StudioActivityPalette.Categories()));

        private readonly Dictionary<string, PaletteActivityDescriptor> _byKey =
            new Dictionary<string, PaletteActivityDescriptor>(StringComparer.OrdinalIgnoreCase);

        private readonly List<PaletteActivityDescriptor> _descriptors = new List<PaletteActivityDescriptor>();
        private readonly List<string> _categories = new List<string>();

        /// <summary>Ключ дискового кэша для текущего состава палитры (см. <see cref="PaletteDiskCache"/>).</summary>
        private readonly string _diskCacheKey;

        /// <summary>Дескрипторы, прочитанные из дискового кэша (пустой словарь, если кэша нет).</summary>
        private readonly Dictionary<Type, PaletteActivityDescriptor> _diskCacheDescriptors;

        /// <summary>Дисковый кэш был прочитан успешно (файл существовал и прошёл валидацию).</summary>
        private readonly bool _diskCacheLoaded;

        /// <summary>Число типов, для которых дисковый кэш не дал дескриптор (пришлось рефлексировать).</summary>
        private int _diskCacheMisses;

        /// <summary>Кэш метаданных типа на процесс: рефлексия выполняется один раз на тип.</summary>
        private static readonly ConcurrentDictionary<Type, PaletteActivityDescriptor> DescriptorCache =
            new ConcurrentDictionary<Type, PaletteActivityDescriptor>();

        /// <summary>Кэш локализованных строк атрибутов: (тип ресурсов, ключ) → строка.</summary>
        private static readonly ConcurrentDictionary<string, string> LocalizedStringCache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>Кэш <see cref="ResourceManager"/> по типу ресурсов: создаётся один раз и переиспользуется.</summary>
        private static readonly ConcurrentDictionary<string, ResourceManager> ResourceManagerCache =
            new ConcurrentDictionary<string, ResourceManager>(StringComparer.Ordinal);
        /// <summary>Кэш <see cref="CustomAttributeData"/> по члену типа: рефлексия атрибутов выполняется один раз на член.</summary>
        private static readonly ConditionalWeakTable<MemberInfo, IList<CustomAttributeData>> AttributeDataCache =
            new ConditionalWeakTable<MemberInfo, IList<CustomAttributeData>>();

        internal ActivityPaletteRegistry(IEnumerable<ActivityPaletteCategory> categories)
        {
            ActivityPaletteCategory[] categoryList = (categories ?? Enumerable.Empty<ActivityPaletteCategory>())
                .Where(category => category != null && !string.IsNullOrWhiteSpace(category.Key))
                .ToArray();

            _diskCacheKey = PaletteDiskCache.ComputeKey(categoryList);
            Dictionary<Type, PaletteActivityDescriptor> diskCache = PaletteDiskCache.Load(_diskCacheKey);
            _diskCacheLoaded = diskCache != null;
            _diskCacheDescriptors = diskCache ?? new Dictionary<Type, PaletteActivityDescriptor>();
            _diskCacheMisses = 0;

            foreach (ActivityPaletteCategory category in categoryList)
            {
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

        /// <summary>
        /// Записывает построенные дескрипторы в дисковый кэш. Вызывается на «полной» сборке палитры
        /// (<c>MainView.BuildPaletteModel</c>). Если все дескрипторы уже взяты из кэша, запись пропускается.
        /// </summary>
        internal void PersistDiskCache()
        {
            if (_diskCacheMisses == 0)
            {
                return;
            }

            PaletteDiskCache.Save(_diskCacheKey, _descriptors);
        }

        /// <summary>Реестр студии: встроенные активности WF плюс уже загруженные библиотеки активностей.</summary>
        internal static ActivityPaletteRegistry Default => DefaultRegistry.Value;

        /// <summary>
        /// Сброс кэша реестра (карточка <c>AG-08</c>): после создания активности и сборки состав палитры
        /// изменился, а Lazy живёт на процесс — без сброса каталог и описание инструментов правки видели бы
        /// прежний набор типов до перезапуска студии.
        /// </summary>
        internal static void InvalidateDefault()
        {
            // Кэш дескрипторов целиком не чистим: дескрипторы неизменяемы и привязаны к Type, поэтому
            // встроенные и созданные агентом типы не рефлексируются заново. Устаревшие типы удаляются
            // точечно через InvalidateTypes.
            DefaultRegistry = new Lazy<ActivityPaletteRegistry>(() => new ActivityPaletteRegistry(StudioActivityPalette.Categories()));
        }

        /// <summary>
        /// Точечно удаляет из кэша дескрипторы указанных типов (например, удалённых NuGet-пакетов)
        /// и сбрасывает Lazy реестра. Встроенные и созданные агентом типы остаются закэшированными.
        /// </summary>
        internal static void InvalidateTypes(IEnumerable<Type> types)
        {
            Type[] removed = (types ?? Enumerable.Empty<Type>())
                .Where(type => type != null)
                .ToArray();

            foreach (Type type in removed)
            {
                DescriptorCache.TryRemove(type, out _);
            }

            if (removed.Length > 0)
            {
                PaletteDiskCache.Invalidate();
            }

            DefaultRegistry = new Lazy<ActivityPaletteRegistry>(() => new ActivityPaletteRegistry(StudioActivityPalette.Categories()));
        }

        /// <summary>
        /// Устанавливает заранее построенный реестр (фоновая пересборка палитры), чтобы
        /// <see cref="Default"/> вернул его без повторной рефлексии.
        /// </summary>
        internal static void Install(ActivityPaletteRegistry registry)
        {
            DefaultRegistry = new Lazy<ActivityPaletteRegistry>(() => registry ?? new ActivityPaletteRegistry(StudioActivityPalette.Categories()));
        }

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

            // Дисковый кэш холодного старта: дескриптор берётся из файла, рефлексия не выполняется.
            if (_diskCacheDescriptors.TryGetValue(type, out PaletteActivityDescriptor diskCached)
                && string.Equals(diskCached.Category, category, StringComparison.Ordinal))
            {
                DescriptorCache.TryAdd(type, diskCached);
                return diskCached;
            }

            // Диск не покрыл тип (промах или сменилась категория) — отражаем и перепишем кэш.
            _diskCacheMisses++;

            if (DescriptorCache.TryGetValue(type, out PaletteActivityDescriptor cached))
            {
                if (string.Equals(cached.Category, category, StringComparison.Ordinal))
                {
                    return cached;
                }

                // Категория изменилась (перерегистрация библиотеки) — строим заново и перезаписываем кэш.
                DescriptorCache.TryRemove(type, out _);
            }

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            var arguments = new List<PaletteArgumentDescriptor>();
            var containers = new List<PaletteContainerDescriptor>();

            System.Diagnostics.Stopwatch getPropsSw = System.Diagnostics.Stopwatch.StartNew();
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            long getPropertiesMs = getPropsSw.ElapsedMilliseconds;

            foreach (PropertyInfo property in properties)
            {
                System.Diagnostics.Stopwatch typeSw = System.Diagnostics.Stopwatch.StartNew();
                _ = property.PropertyType;
                long propertyTypeMs = typeSw.ElapsedMilliseconds;

                System.Diagnostics.Stopwatch propSw = System.Diagnostics.Stopwatch.StartNew();

                PaletteArgumentDescriptor argument = DescribeArgument(property);
                if (argument != null)
                {
                    arguments.Add(argument);
                }
                else
                {
                    PaletteContainerDescriptor container = DescribeContainer(property);
                    if (container != null)
                    {
                        containers.Add(container);
                    }
                    else
                    {
                        PaletteArgumentDescriptor plain = DescribePlainProperty(property);
                        if (plain != null)
                        {
                            arguments.Add(plain);
                        }
                    }
                }

                long describeMs = propSw.ElapsedMilliseconds;

                if (propertyTypeMs > 0 || describeMs > 0)
                {
                    Log.Write($"[Palette]     Prop {type.Name}.{property.Name}: GetType={propertyTypeMs} ms, Describe={describeMs} ms");
                }
            }

            Log.Write($"[Palette] Describe({type.FullName ?? type.Name}): GetProperties={getPropertiesMs} ms, всего={sw.ElapsedMilliseconds} ms");

            var descriptor = new PaletteActivityDescriptor(
                KeyOf(type),
                DisplayNameOf(type),
                category,
                type,
                DescriptionOf(type),
                arguments,
                containers);

            return DescriptorCache.GetOrAdd(type, descriptor);
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
            string displayName = ReadDisplayName(type, true);

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                name = displayName;
            }

            return type.IsGenericTypeDefinition ? name + "<T>" : name;
        }

        private static string DescriptionOf(Type type)
        {
            return ReadDescription(type, true);
        }

        /// <summary>
        /// Кэш локализованных строк атрибутов <c>[Localized*]</c>: (тип ресурсов, ключ) → строка.
        /// <see cref="ResourceManager"/> дорог в создании, поэтому создаём его один раз на пару.
        /// </summary>
        private static string GetLocalizedString(Type resourceType, string key)
        {
            if (resourceType == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            string resourceKey = resourceType.AssemblyQualifiedName ?? resourceType.FullName ?? resourceType.Name;
            ResourceManager resourceManager = ResourceManagerCache.GetOrAdd(resourceKey, _ =>
            {
                try
                {
                    return new ResourceManager(resourceType);
                }
                catch
                {
                    return null;
                }
            });

            if (resourceManager == null)
            {
                return null;
            }

            string cacheKey = resourceKey + "|" + key;
            return LocalizedStringCache.GetOrAdd(cacheKey, _ =>
            {
                try
                {
                    return resourceManager.GetString(key);
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Определяет, является ли атрибут локализованным (<c>[LocalizedDescription]</c>/
        /// <c>[LocalizedDisplayName]</c>/<c>[LocalizedCategory]</c>). Такие атрибуты не инстанцируются:
        /// их конструктор и геттер создают <see cref="ResourceManager"/> на каждое чтение.
        /// </summary>
        private static bool IsLocalizedAttribute(Type attributeType)
        {
            string fullName = attributeType.FullName;
            return string.Equals(fullName, "ActivityLibraryBase.Attributes.LocalizedDescriptionAttribute", StringComparison.Ordinal)
                   || string.Equals(fullName, "ActivityLibraryBase.Attributes.LocalizedDisplayNameAttribute", StringComparison.Ordinal)
                   || string.Equals(fullName, "ActivityLibraryBase.Attributes.LocalizedCategoryAttribute", StringComparison.Ordinal);
        }

        /// <summary>Читает строку локализованного атрибута из метаданных (ключ + тип ресурсов).</summary>
        private static string ReadLocalizedString(CustomAttributeData data)
        {
            IList<CustomAttributeTypedArgument> args = data.ConstructorArguments;
            if (args.Count >= 2 && args[0].Value is string key)
            {
                Type resourceType = AsType(args[1].Value);
                if (resourceType != null)
                {
                    return GetLocalizedString(resourceType, key);
                }
            }

            return null;
        }

        /// <summary>Извлекает <see cref="Type"/> из значения <c>typeof(...)</c> в метаданных атрибута.</summary>
        private static Type AsType(object value)
        {
            if (value is Type type)
            {
                return type;
            }

            if (value is string typeName && !string.IsNullOrWhiteSpace(typeName))
            {
                try
                {
                    return Type.GetType(typeName);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }


        /// <summary>Читает <see cref="DescriptionAttribute"/> (обычный или локализованный) без дорогого инстанцирования.</summary>
        private static string ReadDescription(MemberInfo member, bool inherit)
        {
            return ReadStringAttribute<DescriptionAttribute>(member, inherit, attribute => attribute.Description);
        }

        /// <summary>Читает <see cref="DisplayNameAttribute"/> (обычный или локализованный) без дорогого инстанцирования.</summary>
        private static string ReadDisplayName(MemberInfo member, bool inherit)
        {
            return ReadStringAttribute<DisplayNameAttribute>(member, inherit, attribute => attribute.DisplayName);
        }

        private static string ReadStringAttribute<T>(MemberInfo member, bool inherit, Func<T, string> valueSelector)
            where T : Attribute
        {
            for (MemberInfo current = member; current != null; )
            {
                foreach (CustomAttributeData data in CustomAttributesOf(current))
                {
                    Type attributeType;
                    try
                    {
                        attributeType = data.AttributeType;
                    }
                    catch
                    {
                        continue;
                    }

                    if (!typeof(T).IsAssignableFrom(attributeType))
                    {
                        continue;
                    }

                    if (IsLocalizedAttribute(attributeType))
                    {
                        return ReadLocalizedString(data);
                    }

                    try
                    {
                        object[] args = data.ConstructorArguments.Select(argument => argument.Value).ToArray();
                        T attribute = (T)data.Constructor.Invoke(args);
                        return valueSelector(attribute);
                    }
                    catch
                    {
                        return null;
                    }
                }

                if (!inherit)
                {
                    break;
                }

                current = (current as Type)?.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Безопасное чтение атрибута без резолва посторонних атрибутов. <see cref="MemberInfo.GetCustomAttribute{T}"/>
        /// инстанцирует/резолвит все атрибуты члена и падает на атрибуте из дизайнерской сборки
        /// (<c>Microsoft.Windows.Design.Interaction</c> — <c>[ToolboxBrowsable]</c>/<c>[ToolboxItem]</c>), которой
        /// нет рядом с пакетом. <see cref="CustomAttributeData"/> читает сырые метаданные и не трогает типы,
        /// поэтому каталог агента строится без исключений.
        /// </summary>
        internal static T GetAttributeSafe<T>(MemberInfo member, bool inherit) where T : Attribute
        {
            for (MemberInfo current = member; current != null; )
            {
                foreach (CustomAttributeData data in CustomAttributesOf(current))
                {
                    Type attributeType;
                    try
                    {
                        attributeType = data.AttributeType;
                    }
                    catch
                    {
                        // Нерезолвимый атрибут (дизайнерская сборка не рядом) — пропускаем, не роняя палитру.
                        continue;
                    }

                    if (!typeof(T).IsAssignableFrom(attributeType))
                    {
                        continue;
                    }

                    try
                    {
                        object[] args = data.ConstructorArguments.Select(argument => argument.Value).ToArray();
                        return (T)data.Constructor.Invoke(args);
                    }
                    catch
                    {
                        return null;
                    }
                }

                if (!inherit)
                {
                    break;
                }

                current = (current as Type)?.BaseType;
            }

            return null;
        }

        /// <summary>
        /// Безопасная проверка наличия атрибута — та же причина, что у <see cref="GetAttributeSafe{T}"/>:
        /// сырые метаданные вместо резолва типов атрибутов.
        /// </summary>
        internal static bool HasAttributeSafe(MemberInfo member, Type attributeType, bool inherit)
        {
            for (MemberInfo current = member; current != null; )
            {
                foreach (CustomAttributeData data in CustomAttributesOf(current))
                {
                    Type type;
                    try
                    {
                        type = data.AttributeType;
                    }
                    catch
                    {
                        continue;
                    }

                    if (attributeType.IsAssignableFrom(type))
                    {
                        return true;
                    }
                }

                if (!inherit)
                {
                    break;
                }

                current = (current as Type)?.BaseType;
            }

            return false;
        }

        private static IList<CustomAttributeData> CustomAttributesOf(MemberInfo member)
        {
            return AttributeDataCache.GetValue(member, _ =>
            {
                try
                {
                    return CustomAttributeData.GetCustomAttributes(member);
                }
                catch
                {
                    return Array.Empty<CustomAttributeData>();
                }
            });
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
                    required: HasAttributeSafe(property, typeof(RequiredArgumentAttribute), true),
                    GetAttributeSafe<DefaultValueAttribute>(property, true)?.Value?.ToString(),
                    ReadDescription(property, true));
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

            bool required = HasAttributeSafe(property, typeof(RequiredArgumentAttribute), true);
            DefaultValueAttribute defaultAttribute = GetAttributeSafe<DefaultValueAttribute>(property, true);
            string description = ReadDescription(property, true);

            return new PaletteArgumentDescriptor(
                property.Name,
                propertyType,
                direction,
                ValueTypeOf(propertyType),
                required,
                defaultAttribute?.Value?.ToString(),
                description);
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
                GetAttributeSafe<DefaultValueAttribute>(property, true)?.Value?.ToString(),
                ReadDescription(property, true));
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
        internal const string CollectionCategory = "lang_toolbox_collection";
        internal const string StateMachineCategory = "lang_toolbox_state_machine";
        internal const string PrimitivesCategory = "lang_toolbox_primitives";
        internal const string ErrorHandlingCategory = "lang_toolbox_error_handling";

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

        };

        /// <summary>
        /// Встроенные категории палитры (активности WF без библиотек студии).
        /// <para>
        /// Список совпадает с панелью «Активности», поэтому в нём есть и элементы диаграммы
        /// (<c>FlowDecision</c>, <c>FlowSwitch</c>, <c>State</c>, <c>FinalState</c>): они не являются
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
                    typeof(ParallelForEach<>),
                    typeof(Pick),
                    typeof(PickBranch)
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
                    typeof(State),
                    typeof(System.Activities.Core.Presentation.FinalState)
                }),
                new ActivityPaletteCategory(PrimitivesCategory, new[]
                {
                    typeof(Assign),
                    typeof(Delay),
                    typeof(InvokeDelegate),
                    typeof(InvokeMethod),
                    typeof(WriteLine)
                }),
                new ActivityPaletteCategory(CollectionCategory, new[]
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

        /// <summary>
        /// Библиотеки активностей, созданные агентом (карточка <c>AG-08</c>) и загруженные в домен.
        /// Живут отдельно от пакетных (<see cref="PackageLibraries"/>), потому что пересинхронизация
        /// NuGet-пакетов (install/uninstall/update) обязана удалять только пакетные активности.
        /// </summary>
        private static readonly List<ActivityPaletteCategory> CreatedLibraries = new List<ActivityPaletteCategory>();

        /// <summary>
        /// Библиотеки активностей из NuGet-пакетов проекта. Полностью заменяются при каждом
        /// сканировании пакетов (<see cref="ReplacePackageLibraries"/>), поэтому удаление пакета
        /// или смена проекта не оставляют «мёртвые» активности в палитре.
        /// </summary>
        private static readonly List<ActivityPaletteCategory> PackageLibraries = new List<ActivityPaletteCategory>();

        /// <summary>
        /// Добавляет типы собранной библиотеки в палитру (источник — <c>activity_create</c>).
        /// Возвращает число добавленных типов; повторная регистрация того же типа дубля не создаёт
        /// (иначе панель показала бы две одинаковые строки).
        /// </summary>
        internal static int RegisterUserLibrary(string categoryKey, IEnumerable<Type> types)
        {
            int added = MergeLibrary(CreatedLibraries, categoryKey, types);
            if (added > 0)
            {
                PaletteDiskCache.Invalidate();
            }

            return added;
        }

        /// <summary>
        /// Атомарно заменяет пакетные библиотеки точным набором категорий, полученным при сканировании
        /// NuGet-пакетов. Категории с одинаковым ключом сливаются, дубли типов убираются.
        /// </summary>
        internal static void ReplacePackageLibraries(IEnumerable<ActivityPaletteCategory> categories)
        {
            // Дисковый кэш здесь не чистим: на холодном старте ReplacePackageLibraries вызывается при
            // первом сканировании пакетов, и удаление валидного кэша откатило бы выигрыш холодного старта.
            // Изменение состава пакетов меняет ключ кэша (MVID сборок + имена типов), поэтому устаревший
            // файл просто не читается; новый пишется после пересборки в PersistDiskCache.
            PackageLibraries.Clear();

            foreach (ActivityPaletteCategory category in categories ?? Enumerable.Empty<ActivityPaletteCategory>())
            {
                if (category == null || string.IsNullOrWhiteSpace(category.Key) || category.Types.Count == 0)
                {
                    continue;
                }

                MergeLibrary(PackageLibraries, category.Key, category.Types);
            }
        }

        /// <summary>Снапшот текущих пакетных категорий (для сравнения added/removed при пересканировании).</summary>
        internal static IReadOnlyList<ActivityPaletteCategory> PackageCategories()
        {
            return PackageLibraries.ToArray();
        }

        private static int MergeLibrary(List<ActivityPaletteCategory> libraries, string categoryKey, IEnumerable<Type> types)
        {
            Type[] activities = (types ?? Enumerable.Empty<Type>())
                .Where(type => type != null && typeof(Activity).IsAssignableFrom(type) && !type.IsAbstract)
                .ToArray();

            if (activities.Length == 0)
            {
                return 0;
            }

            string key = string.IsNullOrWhiteSpace(categoryKey) ? FlowCategory : categoryKey;
            ActivityPaletteCategory existing = libraries
                .FirstOrDefault(category => string.Equals(category.Key, key, StringComparison.Ordinal));

            Type[] merged = activities;
            if (existing != null)
            {
                libraries.Remove(existing);
                merged = existing.Types.Concat(activities).Distinct().ToArray();
            }

            int added = merged.Length - (existing?.Types.Count ?? 0);
            libraries.Add(new ActivityPaletteCategory(key, merged));

            return added < 0 ? 0 : added;
        }

        /// <summary>Сброс пользовательских библиотек (тесты и перезагрузка проекта).</summary>
        internal static void ResetUserLibraries()
        {
            CreatedLibraries.Clear();
            PackageLibraries.Clear();
        }

        /// <summary>Сброс только пакетных библиотек (тесты и перезагрузка проекта без пакетов).</summary>
        internal static void ResetPackageLibraries()
        {
            PackageLibraries.Clear();
        }

        /// <summary>Пользовательские категории палитры (для панели «Активности», карточка <c>AG-08</c>).</summary>
        internal static IReadOnlyList<ActivityPaletteCategory> UserCategories()
        {
            return CreatedLibraries.Concat(PackageLibraries).ToArray();
        }

        /// <summary>Полный состав палитры: встроенные категории плюс уже загруженные библиотеки студии.</summary>
        internal static IReadOnlyList<ActivityPaletteCategory> Categories()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            EnsureStudioLibrariesLoaded();
            Log.Write($"[Palette] EnsureStudioLibrariesLoaded: +{sw.ElapsedMilliseconds} ms");

            var categories = new List<ActivityPaletteCategory>(BuiltinCategories());
            Log.Write($"[Palette] BuiltinCategories: +{sw.ElapsedMilliseconds} ms, категорий={categories.Count}");

            foreach (string assemblyName in LibraryAssemblyNames)
            {
                long before = sw.ElapsedMilliseconds;
                ActivityPaletteCategory category = CategoryFromLoadedAssembly(CategoryKeyOf(assemblyName), assemblyName);
                Log.Write($"[Palette] CategoryFromLoadedAssembly({assemblyName}): +{sw.ElapsedMilliseconds - before} ms, типов={category?.Types.Count ?? 0}, найдена={category != null}");
                if (category != null)
                {
                    categories.Add(category);
                }
            }

            categories.AddRange(CreatedLibraries);
            categories.AddRange(PackageLibraries);

            Log.Write($"[Palette] StudioActivityPalette.Categories: всего +{sw.ElapsedMilliseconds} ms, категорий={categories.Count}, типов={categories.Sum(c => c?.Types.Count ?? 0)}");
            return categories;
        }

        /// <summary>
        /// Загружает базовые сборки палитры (<c>System.Activities</c> и библиотеки студии Flow/Debug/OS)
        /// до построения состава, чтобы тулбокс и каталог агента получали одинаковый набор независимо
        /// от порядка вызовов. Ошибка загрузки одной сборки не роняет палитру и каталог.
        /// </summary>
        private static void EnsureStudioLibrariesLoaded()
        {
            LoadAssemblySafe("System.Activities");
            foreach (string assemblyName in LibraryAssemblyNames)
            {
                LoadAssemblySafe(assemblyName);
            }
        }

        private static void LoadAssemblySafe(string assemblyName)
        {
            try
            {
                Assembly.Load(assemblyName);
            }
            catch (Exception)
            {
                // Библиотека недоступна в домене — её категория будет пропущена, но палитра обязана построиться.
            }
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
            catch (Exception)
            {
                // FileNotFoundException/FileLoadException/BadImageFormatException из-за неразрешённых
                // зависимостей сборки (например, пакет с транзитивными зависимостями ещё не в packages\).
                // Один «битый» пакет не должен ронять палитру и тем более установку пакета.
                return Array.Empty<Type>();
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
        /// Whitelist типов переменных и аргументов типа (этап 6 конвейера генерации XAML):
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
