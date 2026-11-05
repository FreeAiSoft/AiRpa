using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RPA.Chat.Services.Workflow;
using RPA.Core.Workflow;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Обновление палитры «Активности» после создания активности (карточка <c>AG-08</c>, вариант B).
    /// <para>
    /// Порядок обязателен и один: загрузить сборку в домен → проверить, что активность в ней есть →
    /// добавить типы в <see cref="StudioActivityPalette"/> → сбросить оба кэша
    /// (<see cref="ActivityPaletteRegistry"/>, <c>StudioActivityCatalog</c>) → пересобрать панель на UI-потоке.
    /// Пропущенный шаг даёт ровно те симптомы, из-за которых заведена карточка: тип собран, но его не видит
    /// ни <c>activity_search</c>, ни человек (кэши <c>Lazy</c> живут на процесс).
    /// </para>
    /// <para>
    /// Загрузчик и пересборка панели подменяются: тесты проверяют связку без MSBuild и без WPF, а
    /// <c>MainView</c> передаёт настоящую пересборку содержимого панели.
    /// </para>
    /// </summary>
    internal sealed class StudioActivityPaletteNotifier : IActivityPaletteNotifier
    {
        private readonly Func<string, Assembly> _loadAssembly;
        private readonly Action _refreshPanel;

        internal StudioActivityPaletteNotifier(Func<string, Assembly> loadAssembly = null, Action refreshPanel = null)
        {
            _loadAssembly = loadAssembly ?? Assembly.LoadFrom;
            _refreshPanel = refreshPanel;
        }

        /// <summary>
        /// Обновление для студии: <c>refreshPanel</c> вызывается после сброса кэшей и должен сам позаботиться
        /// о UI-потоке (панель «Активности» — элемент управления WPF).
        /// </summary>
        internal static StudioActivityPaletteNotifier Create(Action refreshPanel)
        {
            return new StudioActivityPaletteNotifier(null, refreshPanel);
        }

        public ActivityPaletteRefreshResult Notify(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                return ActivityPaletteRefreshResult.NotPerformed("путь к собранной сборке не задан");
            }

            if (!File.Exists(assemblyPath))
            {
                return ActivityPaletteRefreshResult.NotPerformed("собранная сборка не найдена: " + assemblyPath);
            }

            string expectedName = Path.GetFileNameWithoutExtension(assemblyPath);

            Assembly assembly;
            try
            {
                assembly = _loadAssembly(assemblyPath);
            }
            catch (Exception ex)
            {
                return ActivityPaletteRefreshResult.NotPerformed(
                    "не удалось загрузить сборку " + expectedName + " в домен: " + ex.Message);
            }

            if (assembly == null)
            {
                return ActivityPaletteRefreshResult.NotPerformed("загрузчик сборок вернул null для " + expectedName);
            }

            string actualName = assembly.GetName().Name;
            if (!string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                // Имя файла и имя сборки разошлись: «обновили палитру» не тем, что просили.
                return ActivityPaletteRefreshResult.NotPerformed(
                    "загруженная сборка " + actualName + " не совпадает с именем файла " + expectedName);
            }

            IReadOnlyList<Type> activityTypes = StudioActivityPalette.ActivityTypesOf(assembly);
            if (activityTypes.Count == 0)
            {
                return ActivityPaletteRefreshResult.NotPerformed(
                    "в сборке " + actualName + " нет активностей: обновлять палитру нечем");
            }

            int added = StudioActivityPalette.RegisterUserLibrary(StudioActivityPalette.FlowCategory, activityTypes);
            if (added <= 0)
            {
                return ActivityPaletteRefreshResult.NotPerformed(
                    "типы сборки " + actualName + " уже есть в палитре: обновление не требуется");
            }

            ActivityPaletteRegistry.InvalidateDefault();
            StudioActivityCatalog.InvalidateDefault();

            _refreshPanel?.Invoke();

            return ActivityPaletteRefreshResult.From(actualName, added);
        }
    }
}
