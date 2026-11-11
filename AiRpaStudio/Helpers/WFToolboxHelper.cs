using System;
using System.Diagnostics;
using System.Threading;
using System.Activities;
using System.Activities.Core.Presentation;
//using System.Activities;
using System.Activities.Presentation.Toolbox;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Reflection;
using System.Runtime.CompilerServices;
using RPA.Controls;
using RPA.Core.Generation;
using RPA.Logging;

namespace RPA.Helpers
{
    internal class WFToolboxHelper
    {
        public static int ActivitiesCount { get; set; } = 0;

        /// <summary>Кэш отображаемых имён типов: FixName вычисляется один раз на тип.</summary>
        private static readonly ConcurrentDictionary<Type, string> DisplayNameCache =
            new ConcurrentDictionary<Type, string>();

        /// <summary>Подготовленные в фоне <see cref="ToolboxItem"/> до переноса на UI-поток.</summary>
        private static readonly ConditionalWeakTable<ToolboxItemWrapper, ToolboxItemPreload> PreloadedItems =
            new ConditionalWeakTable<ToolboxItemWrapper, ToolboxItemPreload>();

        private static readonly FieldInfo ToolboxItemField =
            typeof(ToolboxItemWrapper).GetField("toolboxItem", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ToolTypeField =
            typeof(ToolboxItemWrapper).GetField("toolType", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultDisplayNameField =
            typeof(ToolboxItemWrapper).GetField("defaultDisplayName", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultBitmapField =
            typeof(ToolboxItemWrapper).GetField("defaultBitmap", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo LoadBitmapMethod =
            typeof(ToolboxItemWrapper).GetMethod("LoadBitmap", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ChangeToolboxDisplayNameMethod =
            typeof(ToolboxItemWrapper).GetMethod("ChangeToolboxDisplayName", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>Данные, вычисленные в фоне и вносимые в <see cref="ToolboxItemWrapper"/> на UI-потоке.</summary>
        private sealed class ToolboxItemPreload
        {
            internal ToolboxItem ToolboxItem;
            internal Type ToolType;
            internal string DefaultDisplayName;
            internal Bitmap DefaultBitmap;
        }

        /// <summary>
        /// Сброс счётчика панели (карточка <c>AG-08</c>): <see cref="Build"/> вызывается не только при старте,
        /// но и после появления новой библиотеки активностей, а счётчик статический и только прибавлялся.
        /// </summary>
        internal static void ResetActivitiesCount()
        {
            ActivitiesCount = 0;
        }

        /// <summary>
        /// Строит панель «Активности». Состав палитры берётся из <see cref="StudioActivityPalette"/> —
        /// того же списка, из которого строится реестр инструментов <c>xaml.*</c>
        /// (этап 4 плана `конвейер генерации XAML`): иначе панель и инструменты разойдутся,
        /// и агент не сможет добавить активность, которую видит пользователь.
        /// </summary>
        internal ToolboxPanel Build()
        {
            Stopwatch sw = Stopwatch.StartNew();
            Log.Write($"[Palette] WFToolboxHelper.Build: начало (поток {Thread.CurrentThread.ManagedThreadId})");

            ToolboxPanel toolboxPanel = new ToolboxPanel();
            ResetActivitiesCount();
            ToolboxControl wfToolbox = new ToolboxControl();


            // System.Activities and studio libraries are loaded by the single palette source
            // (StudioActivityPalette.Categories) so toolbox and agent catalog stay in sync.

            // Единый источник состава палитры: панель «Активности» и каталог агента читают один и тот же
            // список (StudioActivityPalette.Categories) — встроенные активности, библиотеки студии
            // (Flow/Debug/OS/Browser) и пользовательские/пакетные активности.
            IReadOnlyList<ActivityPaletteCategory> categories = StudioActivityPalette.Categories();
            Log.Write($"[Palette] StudioActivityPalette.Categories: +{sw.ElapsedMilliseconds} ms, категорий={categories.Count}, типов={categories.Sum(c => c?.Types.Count ?? 0)}");

            foreach (ActivityPaletteCategory category in categories)
            {
                if (category == null)
                {
                    // Библиотека не загрузилась — категория пропускается, панель обязана построиться.
                    continue;
                }

                long before = sw.ElapsedMilliseconds;
                AddToolboxCategory(wfToolbox, category.Types.ToList(), CategoryDisplayName(category.Key));
                Log.Write($"[Palette] Категория «{CategoryDisplayName(category.Key)}»: +{sw.ElapsedMilliseconds - before} ms, типов={category.Types.Count}");
            }

            toolboxPanel.SetToolboxControl(wfToolbox);
            Log.Write($"[Palette] WFToolboxHelper.Build: завершён +{sw.ElapsedMilliseconds} ms, ActivitiesCount={ActivitiesCount}");
            return toolboxPanel;
        }

        /// <summary>
        /// Фоновая фаза пересборки палитры: строит model-объекты тулбокса.
        /// <see cref="ToolboxCategory"/> и <see cref="ToolboxItemWrapper"/> — обычные классы
        /// (не WPF-элементы), поэтому метод безопасно вызывать из <c>Task.Run</c>.
        /// </summary>
        internal List<ToolboxCategory> BuildModel(IReadOnlyList<ActivityPaletteCategory> categories)
        {
            return BuildModel(categories, null);
        }

        internal List<ToolboxCategory> BuildModel(IReadOnlyList<ActivityPaletteCategory> categories, IReadOnlyDictionary<Type, string> displayNames)
        {
            Stopwatch createSw = Stopwatch.StartNew();
            var result = new List<ToolboxCategory>();
            foreach (ActivityPaletteCategory category in categories ?? Array.Empty<ActivityPaletteCategory>())
            {
                if (category == null)
                {
                    continue;
                }

                result.Add(CreateCategory(category.Types.ToList(), CategoryDisplayName(category.Key), displayNames));
            }
            Log.Write($"[Palette] WFToolboxHelper.BuildModel: CreateCategory +{createSw.ElapsedMilliseconds} ms, категорий={result.Count}");

            Stopwatch preloadSw = Stopwatch.StartNew();
            PreloadToolboxItems(result);
            Log.Write($"[Palette] WFToolboxHelper.BuildModel: preload ToolboxItem +{preloadSw.ElapsedMilliseconds} ms, категорий={result.Count}");
            return result;
        }

        /// <summary>
        /// UI-фаза пересборки палитры: из подготовленных категорий собирает <see cref="ToolboxControl"/>.
        /// <see cref="ToolboxControl"/> — WPF-элемент, поэтому метод вызывается только на UI-потоке.
        /// </summary>
        internal ToolboxControl BuildControl(List<ToolboxCategory> categories)
        {
            Stopwatch sw = Stopwatch.StartNew();
            Log.Write($"[Palette] WFToolboxHelper.BuildControl: начало (поток {Thread.CurrentThread.ManagedThreadId})");

            ResetActivitiesCount();
            ToolboxControl wfToolbox = new ToolboxControl();

            foreach (ToolboxCategory category in categories ?? new List<ToolboxCategory>())
            {
                if (category == null)
                {
                    continue;
                }

                try
                {
                    foreach (ToolboxItemWrapper tool in category.Tools)
                    {
                        CompleteToolboxItem(tool);
                    }

                    wfToolbox.Categories.Add(category);
                    ActivitiesCount += category.Tools.Count;
                }
                catch (Exception ex)
                {
                    Log.Write($"Панель «Активности»: категория пропущена — {ex.GetBaseException().Message}");
                }
            }

            Log.Write($"[Palette] WFToolboxHelper.BuildControl: завершён +{sw.ElapsedMilliseconds} ms, ActivitiesCount={ActivitiesCount}");
            return wfToolbox;
        }

        /// <summary>
        /// Строит одну категорию тулбокса из типов активностей (сортировка по отображаемому имени).
        /// </summary>
        internal ToolboxCategory CreateCategory(List<Type> types, string categoryName)
        {
            return CreateCategory(types, categoryName, null);
        }

        internal ToolboxCategory CreateCategory(List<Type> types, string categoryName, IReadOnlyDictionary<Type, string> displayNames)
        {
            ToolboxCategory toolboxCategory = new ToolboxCategory(categoryName);
            foreach (Type type in types.OrderBy(type => GetDisplayName(type, displayNames), StringComparer.OrdinalIgnoreCase))
            {
                toolboxCategory.Add(new ToolboxItemWrapper(type, GetDisplayName(type, displayNames)));
            }
            return toolboxCategory;
        }

        /// <summary>Берёт имя из заранее подготовленного реестра, иначе вычисляет через <see cref="FixName"/>.</summary>
        private string GetDisplayName(Type type, IReadOnlyDictionary<Type, string> displayNames)
        {
            if (displayNames != null && displayNames.TryGetValue(type, out string name))
            {
                return name;
            }

            return FixName(type);
        }

        /// <summary>Выполняет дорогую часть <c>ResolveToolboxItem</c> (рефлексия + ToolboxItem) в фоне.</summary>
        private static void PreloadToolboxItems(IEnumerable<ToolboxCategory> categories)
        {
            foreach (ToolboxCategory category in categories ?? Enumerable.Empty<ToolboxCategory>())
            {
                foreach (ToolboxItemWrapper tool in category.Tools)
                {
                    PreloadToolboxItem(tool);
                }
            }
        }

        private static void PreloadToolboxItem(ToolboxItemWrapper tool)
        {
            if (tool.IsValid)
            {
                return;
            }

            try
            {
                Type toolType = Assembly.Load(tool.AssemblyName).GetType(tool.ToolName, true, true);
                ValidateTool(toolType);
                ToolboxItem toolboxItem = CreateToolboxItem(toolType);

                PreloadedItems.Add(tool, new ToolboxItemPreload
                {
                    ToolboxItem = toolboxItem,
                    ToolType = toolType,
                    DefaultDisplayName = toolboxItem.DisplayName,
                    // Иконка резолвится лениво при отрисовке (см. CompleteToolboxItem) — не в фоне.
                    DefaultBitmap = null
                });
            }
            catch (Exception ex)
            {
                Log.Write($"Панель «Активности»: фоновая подготовка «{tool.ToolName}» не удалась — {ex.GetBaseException().Message}");
            }
        }

        /// <summary>Доводит подготовленный в фоне элемент до состояния, которое обычно создаёт <c>ResolveToolboxItem</c>.</summary>
        private static void CompleteToolboxItem(ToolboxItemWrapper tool)
        {
            if (tool.IsValid || !PreloadedItems.TryGetValue(tool, out ToolboxItemPreload preload))
            {
                return;
            }

            ToolboxItemField.SetValue(tool, preload.ToolboxItem);
            DefaultDisplayNameField.SetValue(tool, preload.DefaultDisplayName);
            DefaultBitmapField.SetValue(tool, preload.DefaultBitmap);
            ToolTypeField.SetValue(tool, preload.ToolType);

            // WPF-часть (иконки) и подстановка отображаемого имени остаются на UI-потоке.
            _ = LoadBitmapMethod.Invoke(tool, null);
            _ = ChangeToolboxDisplayNameMethod.Invoke(tool, null);
        }

        private static ToolboxItem CreateToolboxItem(Type toolType)
        {
            ToolboxItemAttribute[] attributes =
                toolType.GetCustomAttributes(typeof(ToolboxItemAttribute), true) as ToolboxItemAttribute[];
            if (attributes != null && attributes.Length > 0)
            {
                return Activator.CreateInstance(attributes[0].ToolboxItemType) as ToolboxItem;
            }

            return new ToolboxItem(toolType);
        }

        private static void ValidateTool(Type toolType)
        {
            bool invalid = toolType.IsAbstract
                || toolType.IsInterface
                || !toolType.IsVisible
                || toolType.GetConstructor(Type.EmptyTypes) == null;

            if (invalid)
            {
                throw new NotSupportedException($"Тип «{toolType.FullName}» не может быть активностью тулбокса.");
            }
        }


        /// <summary>
        /// Display name of a category: localization for built-in keys, the key itself
        /// (assembly file name without extension) otherwise.
        /// </summary>
        internal static string CategoryDisplayName(string key)
        {
            string localized = LocalizeHelper.Get(key);
            return string.IsNullOrWhiteSpace(localized) ? key : localized;
        }

        /// <summary>
        /// Добавляет категорию в тулбокс. Активности сортируются по отображаемому имени
        /// (<see cref="FixName"/>) без учёта регистра, чтобы порядок в панели не зависел от
        /// порядка перечисления типов в источнике (встроенный список задаётся вручную).
        /// </summary>
        internal void AddToolboxCategory(ToolboxControl wfToolbox, List<Type> types, string categoryName)
        {
            ToolboxCategory controlFlowToolboxCategory = new ToolboxCategory(categoryName);
            foreach (Type type in types.OrderBy(FixName, StringComparer.OrdinalIgnoreCase))
            {
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                controlFlowToolboxCategory.Add(new ToolboxItemWrapper(type, FixName(type)));
                Log.Write($"[Palette]   Тип «{type.FullName ?? type.Name}» (категория «{categoryName}»): +{sw.ElapsedMilliseconds} ms");
            }
            try
            {
                wfToolbox.Categories.Add(controlFlowToolboxCategory);
                ActivitiesCount += controlFlowToolboxCategory.Tools.Count;
            }
            catch (Exception ex)
            {
                Log.Write($"Панель «Активности»: категория «{categoryName}» пропущена — {ex.GetBaseException().Message}");
            }
        }

        private string FixName(Type type)
        {
            return DisplayNameCache.GetOrAdd(type, ComputeFixName);
        }

        private static string ComputeFixName(Type type)
        {
            string displayName = type.Name;
            string[] splitName = displayName.Split('`');
            displayName = splitName[0];
            DisplayNameAttribute displayNameAttribute = ReadDisplayName(type);
            if (displayNameAttribute != null && !string.IsNullOrWhiteSpace(displayNameAttribute.DisplayName))
            {
                displayName = displayNameAttribute.DisplayName;
            }

            if (splitName.Length > 1)
            {
                displayName = $"{displayName}<T>";
            }

            return displayName;
        }

        /// <summary>
        /// Читает <see cref="DisplayNameAttribute"/> типа по сырым метаданным атрибутов (без резолва
        /// посторонних дизайнерских атрибутов): тот же путь, что и в каталоге агента.
        /// </summary>
        private static DisplayNameAttribute ReadDisplayName(Type type)
        {
            return ActivityPaletteRegistry.GetAttributeSafe<DisplayNameAttribute>(type, true);
        }
    }
}
