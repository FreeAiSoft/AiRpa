using System;
using System.IO;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Фасад сброса кэшей оптимизации NuGet. Студия не знает внутренние классы панели, поэтому
    /// кнопка «Сбросить кэши» дёргает этот public-API, а мемоизацию конкретного сервиса очищает
    /// <see cref="NuGetPanelViewModel.ResetCaches"/>.
    /// </summary>
    public static class NuGetOptimizationCache
    {
        /// <summary>Удаляет все файлы дискового индекса локальных источников NuGet.</summary>
        public static void InvalidateAll()
        {
            string directory = LocalPackageIndexCache.RootDirectory;
            try
            {
                if (!Directory.Exists(directory))
                {
                    return;
                }

                foreach (string file in Directory.GetFiles(directory, "*.json"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Занятый/заблокированный файл не должен ронять сброс.
                    }
                }
            }
            catch
            {
                // Ошибка перечисления каталога кэша не критична.
            }
        }
    }
}
