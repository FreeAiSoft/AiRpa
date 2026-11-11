using RPA.Core.Generation;
using RPA.Core.Workflow;
using RPA.Logging;

namespace RPA.Core.Services
{
    /// <summary>
    /// Оркестратор сброса кэшей оптимизации студии (кнопка «Сбросить кэши оптимизаций» в меню
    /// «Настройки»). Собирает в одной точке дисковые и in-memory кэши палитры и NuGet.
    /// </summary>
    internal static class StudioCacheReset
    {
        internal static void ResetAll()
        {
            PaletteDiskCache.Invalidate();
            ActivityPaletteRegistry.ResetCaches();
            StudioActivityCatalog.InvalidateDefault();
            RPA.NuGet.Services.NuGetOptimizationCache.InvalidateAll();

            Log.Write("[Cache] Кэши оптимизаций сброшены");
        }
    }
}
