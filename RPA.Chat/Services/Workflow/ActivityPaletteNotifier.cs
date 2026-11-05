using System;

namespace RPA.Chat.Services.Workflow
{
    /// <summary>
    /// Результат обновления палитры после создания активности (карточка <c>AG-08</c>). Как и у гейта
    /// валидации XAML (<c>AG-07</c>), «не выполнено» названо отдельно: молчание здесь означало бы
    /// «активность видна», хотя без загрузки сборки в домен её не видит ни каталог, ни панель.
    /// </summary>
    public sealed class ActivityPaletteRefreshResult
    {
        private ActivityPaletteRefreshResult(bool refreshed, string assemblyName, int activityTypeCount, string reason)
        {
            Refreshed = refreshed;
            AssemblyName = assemblyName;
            ActivityTypeCount = activityTypeCount;
            Reason = reason;
        }

        /// <summary>Сборка загружена, кэши сброшены, панель пересобрана.</summary>
        public bool Refreshed { get; }

        /// <summary>Имя сборки, из которой добавлены активности.</summary>
        public string AssemblyName { get; }

        /// <summary>Сколько типов активностей добавлено в палитру и каталог.</summary>
        public int ActivityTypeCount { get; }

        /// <summary>Почему палитра не обновлена; <c>null</c> — обновлена.</summary>
        public string Reason { get; }

        /// <summary>Палитра обновлена.</summary>
        public static ActivityPaletteRefreshResult From(string assemblyName, int activityTypeCount)
        {
            return new ActivityPaletteRefreshResult(true, assemblyName, activityTypeCount < 0 ? 0 : activityTypeCount, null);
        }

        /// <summary>Палитра не обновлена: причина обязательна.</summary>
        public static ActivityPaletteRefreshResult NotPerformed(string reason)
        {
            return new ActivityPaletteRefreshResult(
                false,
                null,
                0,
                string.IsNullOrWhiteSpace(reason) ? "обновление палитры не выполнено" : reason);
        }
    }

    /// <summary>
    /// Обновление палитры «Активности» и каталога агента после создания активности (карточка <c>AG-08</c>).
    /// Контракт живёт в чате, реализация — в студии: только она владеет доменом сборок, WF-типами и
    /// панелью. Обязанности реализации: загрузить сборку в домен, добавить её типы в палитру, сбросить оба
    /// кэша (<c>StudioActivityPalette</c>/<c>StudioActivityCatalog</c>) и пересобрать панель на UI-потоке.
    /// </summary>
    public interface IActivityPaletteNotifier
    {
        /// <summary>Делает типы собранной сборки видимыми агенту и человеку без перезапуска студии.</summary>
        ActivityPaletteRefreshResult Notify(string assemblyPath);
    }

    /// <summary>
    /// Null-object: студия не подключила обновление палитры. Инструмент честно пишет, что активность
    /// собрана, но в палитре появится только после перезапуска, вместо ложного «всё видно».
    /// </summary>
    public sealed class NullActivityPaletteNotifier : IActivityPaletteNotifier
    {
        public static readonly NullActivityPaletteNotifier Instance = new NullActivityPaletteNotifier();

        private const string UnavailableMessage =
            "обновление палитры не подключено хостом студии: активность собрана, но тип появится в каталоге и панели «Активности» " +
            "только после перезапуска студии — либо откройте панель заново";

        public ActivityPaletteRefreshResult Notify(string assemblyPath)
        {
            return ActivityPaletteRefreshResult.NotPerformed(UnavailableMessage);
        }
    }
}
