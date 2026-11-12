using System;
using System.IO;

namespace RPA.Core
{
    /// <summary>
    /// Пути персистентности NuGet студии. Всё локальное состояние NuGet (per-project источники,
    /// настройки) живёт под <c>%AppData%\AiRpaStudio\NuGet</c> и никогда не пишется в каталог
    /// открытого проекта. Для юнит-тестов каталог подменяется через <see cref="OverrideDataDirectory"/>.
    /// </summary>
    public static class NuGetPaths
    {
        private static string _overrideDataDirectory;

        public static string DataDirectory => _overrideDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AiRpaStudio",
            "NuGet");

        /// <summary>Per-project список источников пакетов: <c>workspaceFolder → [sources]</c>.</summary>
        public static string ProjectSourcesFile => Path.Combine(DataDirectory, "nuget_sources.json");

        public static void OverrideDataDirectory(string path)
        {
            _overrideDataDirectory = path;
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataDirectory);
        }
    }
}
