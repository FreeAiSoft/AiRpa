using System;
using System.Collections.Generic;
using System.IO;

namespace RPA.Core
{
    /// <summary>
    /// Чистый резолвер стартового скрипта (Ф1, шаг 2): выбирает XAML-файл, который должна запустить
    /// кнопка «Старт», без UI и без обращений к живой студии. Активная вкладка и маппинг открытого
    /// дизайнера приходят аргументами, поэтому класс тестируется в изоляции.
    /// </summary>
    public static class RunTargetResolver
    {
        private static readonly string[] FallbackCandidates = { "Main.xaml", "main.xaml" };

        /// <summary>
        /// Возвращает путь XAML для запуска либо <c>null</c>, если ни один кандидат не существует.
        /// </summary>
        /// <param name="projectDir">Каталог проекта (для относительных кандидатов).</param>
        /// <param name="configuredDefault">Настройка «Стартовый скрипт» (<c>DefaultRunScript</c>).</param>
        /// <param name="projectMainActivity">Главная активность проекта (<c>Project.MainActivity</c>).</param>
        /// <param name="activeDocumentPath">Путь активной вкладки панели документов (дизайнер или редактор кода/XAML).</param>
        /// <param name="getOpenWorkflowPath">По пути активной вкладки возвращает файл открытого WF-дизайнера или <c>null</c>.</param>
        public static string Resolve(
            string projectDir,
            string configuredDefault,
            string projectMainActivity,
            string activeDocumentPath,
            Func<string, string> getOpenWorkflowPath)
        {
            // 1-2. Активная вкладка — открытый WF-дизайнер: запускается именно её файл.
            if (!string.IsNullOrWhiteSpace(activeDocumentPath))
            {
                string openWorkflowPath = getOpenWorkflowPath?.Invoke(activeDocumentPath);
                if (!string.IsNullOrWhiteSpace(openWorkflowPath))
                {
                    return openWorkflowPath;
                }
            }

            // 3. Кандидаты без дублей и регистронезависимо: настройка → главная активность → Main.xaml/main.xaml.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string candidate in EnumerateCandidates(configuredDefault, projectMainActivity))
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string path = MakePath(projectDir, candidate.Trim());
                if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
                {
                    continue;
                }

                // 4. Первый существующий .xaml.
                if (IsExistingXaml(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static IEnumerable<string> EnumerateCandidates(string configuredDefault, string projectMainActivity)
        {
            yield return configuredDefault;
            yield return projectMainActivity;

            foreach (string fallback in FallbackCandidates)
            {
                yield return fallback;
            }
        }

        private static string MakePath(string projectDir, string candidate)
        {
            if (Path.IsPathRooted(candidate))
            {
                return candidate;
            }

            return Path.Combine(projectDir ?? string.Empty, candidate);
        }

        private static bool IsExistingXaml(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            return string.Equals(Path.GetExtension(path), ".xaml", StringComparison.OrdinalIgnoreCase);
        }
    }
}
