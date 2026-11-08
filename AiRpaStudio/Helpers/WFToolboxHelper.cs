using System;
using System.Activities;
using System.Activities.Core.Presentation;
//using System.Activities;
using System.Activities.Presentation.Toolbox;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RPA.Controls;
using RPA.Core.Generation;

namespace RPA.Helpers
{
    internal class WFToolboxHelper
    {
        public static int ActivitiesCount { get; set; } = 0;

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
            ToolboxPanel toolboxPanel = new ToolboxPanel();
            ResetActivitiesCount();
            ToolboxControl wfToolbox = new ToolboxControl();

            LoadPuppeteer();
            _ = AppDomain.CurrentDomain.Load("System.Activities"); //some time not load
            _ = AppDomain.CurrentDomain.Load("Activities.Flow");
            _ = AppDomain.CurrentDomain.Load("Activities.Debug");
            _ = AppDomain.CurrentDomain.Load("Activities.OS");

            // Единый источник состава палитры: панель «Активности» и каталог агента читают один и тот же
            // список (StudioActivityPalette.Categories) — встроенные активности, библиотеки студии
            // (Flow/Debug/OS/Browser) и пользовательские/пакетные активности.
            foreach (ActivityPaletteCategory category in StudioActivityPalette.Categories())
            {
                if (category == null)
                {
                    // Библиотека не загрузилась — категория пропускается, панель обязана построиться.
                    continue;
                }

                AddToolboxCategory(wfToolbox, category.Types.ToList(), CategoryDisplayName(category.Key));
            }

            toolboxPanel.SetToolboxControl(wfToolbox);
            return toolboxPanel;
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

        private void LoadPuppeteer()
        {
            Assembly assembly = AppDomain.CurrentDomain.Load("Activities.Browser");

            // Вывод всех зависимостей сборки Activities.Browser через Debug
            System.Diagnostics.Debug.WriteLine("=== Activities.Browser Dependencies ===");
            AssemblyName[] referencedAssemblies = assembly.GetReferencedAssemblies();
            foreach (AssemblyName refAssembly in referencedAssemblies)
            {
                System.Diagnostics.Debug.WriteLine($"  - {refAssembly.FullName}");
            }
            System.Diagnostics.Debug.WriteLine($"Total dependencies: {referencedAssemblies.Length}");
            System.Diagnostics.Debug.WriteLine("=====================================");

            // Принудительная загрузка всех зависимостей в память
            System.Diagnostics.Debug.WriteLine("=== Loading Activities.Browser Dependencies ===");
            foreach (AssemblyName refAssembly in referencedAssemblies)
            {
                try
                {
                    Assembly loadedAssembly = AppDomain.CurrentDomain.Load(refAssembly);
                    System.Diagnostics.Debug.WriteLine($"  ✓ Loaded: {refAssembly.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ Failed to load {refAssembly.Name}: {ex}");
                }
            }
            System.Diagnostics.Debug.WriteLine("============================");

            // Принудительная загрузка netstandard и всех его зависимостей
            try
            {
                AssemblyName netstandardName = new AssemblyName("netstandard, Version=2.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
                Assembly netstandardAssembly = AppDomain.CurrentDomain.Load(netstandardName);

                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("=== netstandard Assembly Loaded ===");
                System.Diagnostics.Debug.WriteLine($"  Assembly: {netstandardAssembly.FullName}");
                System.Diagnostics.Debug.WriteLine($"  Location: {netstandardAssembly.Location}");
                System.Diagnostics.Debug.WriteLine("====================================");

                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("=== netstandard Dependencies ===");
                AssemblyName[] netstandardReferences = netstandardAssembly.GetReferencedAssemblies();
                foreach (AssemblyName refAssembly in netstandardReferences)
                {
                    System.Diagnostics.Debug.WriteLine($"  - {refAssembly.FullName}");
                }
                System.Diagnostics.Debug.WriteLine($"Total dependencies: {netstandardReferences.Length}");
                System.Diagnostics.Debug.WriteLine("=================================");

                // Принудительная загрузка всех зависимостей netstandard в память
                System.Diagnostics.Debug.WriteLine("=== Loading netstandard Dependencies ===");
                foreach (AssemblyName refAssembly in netstandardReferences)
                {
                    try
                    {
                        Assembly loadedAssembly = AppDomain.CurrentDomain.Load(refAssembly);
                        System.Diagnostics.Debug.WriteLine($"  ✓ Loaded: {refAssembly.Name}");

                        // Рекурсивная загрузка зависимостей второго уровня
                        AssemblyName[] secondLevelRefs = loadedAssembly.GetReferencedAssemblies();
                        foreach (AssemblyName secondLevelRef in secondLevelRefs)
                        {
                            try
                            {
                                _ = AppDomain.CurrentDomain.Load(secondLevelRef);
                                System.Diagnostics.Debug.WriteLine($"    ✓ Loaded (2nd level): {secondLevelRef.Name}");
                            }
                            catch (Exception ex2)
                            {
                                System.Diagnostics.Debug.WriteLine($"    ✗ Failed to load (2nd level) {secondLevelRef.Name}: {ex2.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  ✗ Failed to load {refAssembly.Name}: {ex}");
                    }
                }
                System.Diagnostics.Debug.WriteLine("=========================================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load netstandard assembly: {ex}");
            }
        }

        /// <summary>
        /// Добавляет категорию в тулбокс. Активности сортируются по отображаемому имени
        /// (<see cref="FixName"/>) без учёта регистра, чтобы порядок в панели не зависел от
        /// порядка перечисления типов в источнике (встроенный список задаётся вручную).
        /// </summary>
        internal void AddToolboxCategory(ToolboxControl wfToolbox, List<Type> types, string categoryName)
        {
            ToolboxCategory controlFlowToolboxCategory = new ToolboxCategory(categoryName);
            IEnumerable<ToolboxItemWrapper> activities = types
                .OrderBy(FixName, StringComparer.OrdinalIgnoreCase)
                .Select(a => new ToolboxItemWrapper(a, FixName(a)));
            activities.ToList().ForEach(controlFlowToolboxCategory.Add);
            wfToolbox.Categories.Add(controlFlowToolboxCategory);
            ActivitiesCount += controlFlowToolboxCategory.Tools.Count;
        }

        private string FixName(Type type)
        {
            string displayName = type.Name;
            string[] splitName = displayName.Split('`');
            displayName = splitName[0];
            if (type.GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), true).FirstOrDefault() is System.ComponentModel.DisplayNameAttribute displayNameAttribute)
            {
                displayName = displayNameAttribute.DisplayName;
            }

            if (splitName.Length > 1)
            {
                displayName = $"{displayName}<T>";
            }

            return displayName;
        }
    }
}
