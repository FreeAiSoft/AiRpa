using System;
using System.Activities;
using System.Activities.Core.Presentation;
//using System.Activities;
using System.Activities.Presentation.Toolbox;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.IO;
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
        /// Читает <see cref="DisplayNameAttribute"/> типа, не роняя панель, если среди атрибутов типа есть
        /// атрибут из дизайнерской сборки, отсутствующей рядом с пакетом (например
        /// <c>Microsoft.Windows.Design.Interaction</c>, откуда берутся <c>[ToolboxBrowsable]</c>/
        /// <c>[ToolboxItem]</c>): <c>GetCustomAttributes</c> резолвит типы всех атрибутов и падает,
        /// хотя панели нужен только <c>DisplayName</c>.
        /// </summary>
        private static DisplayNameAttribute ReadDisplayName(Type type)
        {
            try
            {
                return type.GetCustomAttributes(typeof(DisplayNameAttribute), true)
                    .OfType<DisplayNameAttribute>()
                    .FirstOrDefault();
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is FileLoadException ||
                                       ex is TypeLoadException || ex is ReflectionTypeLoadException)
            {
                return null;
            }
        }
    }
}
