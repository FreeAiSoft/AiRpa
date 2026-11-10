using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RPA.VsProject
{
    /// <summary>Результат автофикса совместимости.</summary>
    public class ActivityCompatibilityFixResult
    {
        public List<string> ChangedFiles { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Число проблем, которые удалось исправить автоматически.</summary>
        public int FixedCount { get; set; }

        /// <summary>Число проблем, которые не удалось исправить.</summary>
        public int FailedCount { get; set; }

        /// <summary>Был ли изменён хотя бы один файл.</summary>
        public bool HasChanges => ChangedFiles.Count > 0;
    }

    /// <summary>
    /// Точечно исправляет несовпадения версий пакетов по отчёту проверки совместимости:
    /// <c>packages.config</c>, <c>PackageReference</c> в <c>.csproj</c>, <c>HintPath</c> в <c>.csproj</c>
    /// и <c>&lt;dependency&gt;</c> в <c>.nuspec</c>. Структура проекта и исходники не меняются.
    /// </summary>
    public class ActivityCompatibilityFixer
    {
        /// <summary>
        /// Применяет все автофиксируемые проблемы из отчёта. Возвращает сводку изменений.
        /// Файлы, которые не удалось прочитать или в которых замена не сработала, попадают в
        /// <see cref="ActivityCompatibilityFixResult.Errors"/>.
        /// </summary>
        public ActivityCompatibilityFixResult ApplyFixes(ActivityCompatibilityReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            ActivityCompatibilityFixResult result = new ActivityCompatibilityFixResult();

            var groups = report.FixableIssues
                .Where(issue => !string.IsNullOrWhiteSpace(issue.FilePath))
                .GroupBy(issue => Path.GetFullPath(issue.FilePath), StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, ActivityCompatibilityIssue> group in groups)
            {
                string filePath = group.Key;

                if (!File.Exists(filePath))
                {
                    result.FailedCount += group.Count();
                    result.Errors.Add("Файл не найден: " + filePath);
                    continue;
                }

                bool hasBom;
                string text = ReadAllText(filePath, out hasBom);

                int fixedInFile = 0;
                foreach (ActivityCompatibilityIssue issue in DistinctIssues(group))
                {
                    string replaced = ApplyReplacement(text, issue);
                    if (!string.Equals(text, replaced, StringComparison.Ordinal))
                    {
                        text = replaced;
                        fixedInFile++;
                    }
                    else
                    {
                        result.FailedCount++;
                        result.Errors.Add("Не удалось исправить " + issue.PackageId + " в " + filePath + ".");
                    }
                }

                if (fixedInFile > 0)
                {
                    WriteAllText(filePath, text, hasBom);
                    result.ChangedFiles.Add(filePath);
                    result.FixedCount += fixedInFile;
                }
            }

            return result;
        }

        /// <summary>Убирает дубли проблем с одинаковым местом правки (файл + пакет + способ).</summary>
        private static IEnumerable<ActivityCompatibilityIssue> DistinctIssues(IEnumerable<ActivityCompatibilityIssue> issues)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ActivityCompatibilityIssue issue in issues)
            {
                string key = issue.FixTarget + "|" + issue.PackageId + "|" + issue.CurrentVersion;
                if (seen.Add(key))
                {
                    yield return issue;
                }
            }
        }

        private static string ApplyReplacement(string text, ActivityCompatibilityIssue issue)
        {
            if (string.IsNullOrWhiteSpace(text) || issue == null)
            {
                return text;
            }

            string pinned = issue.PinnedVersion ?? string.Empty;
            string packageId = issue.PackageId ?? string.Empty;

            switch (issue.FixTarget)
            {
                case ActivityCompatibilityFixTarget.PackagesConfigVersion:
                    return ReplacePackageAttribute(text, "package", packageId, issue.CurrentVersion, pinned);

                case ActivityCompatibilityFixTarget.PackageReferenceVersion:
                    return ReplacePackageReference(text, packageId, issue.CurrentVersion, pinned);

                case ActivityCompatibilityFixTarget.HintPathVersion:
                    return ReplaceHintPath(text, packageId, issue.CurrentVersion, pinned);

                case ActivityCompatibilityFixTarget.NuspecDependencyVersion:
                    return ReplacePackageAttribute(text, "dependency", packageId, issue.CurrentVersion, pinned);

                default:
                    return text;
            }
        }

        /// <summary>
        /// Заменяет значение атрибута <c>version</c> у элемента <c>&lt;package&gt;</c> или
        /// <c>&lt;dependency&gt;</c> с указанным <c>id</c> (поддерживает порядок id-version и version-id).
        /// </summary>
        private static string ReplacePackageAttribute(string text, string elementName, string packageId, string currentVersion, string pinned)
        {
            string id = Regex.Escape(packageId);
            string pattern = "(<" + elementName + "\\s+id=\"" + id + "\"\\s+version=\")[^\"]*(\")";
            string replacement = "${1}" + pinned.Replace("$", "$$") + "${2}";
            string replaced = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);

            if (!string.Equals(text, replaced, StringComparison.Ordinal))
            {
                return replaced;
            }

            // Обратный порядок атрибутов: version идёт перед id.
            string reversePattern = "(<" + elementName + "\\s+version=\")[^\"]*(\"\\s+id=\"" + id + "\")";
            string reverseReplacement = "${1}" + pinned.Replace("$", "$$") + "${2}";
            return Regex.Replace(text, reversePattern, reverseReplacement, RegexOptions.IgnoreCase);
        }

        private static string ReplacePackageReference(string text, string packageId, string currentVersion, string pinned)
        {
            string id = Regex.Escape(packageId);
            string pattern = "(<PackageReference\\s+(?:Include|Update)=\"" + id + "\"\\s+Version=\")[^\"]*(\")";
            string replacement = "${1}" + pinned.Replace("$", "$$") + "${2}";
            return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }

        private static string ReplaceHintPath(string text, string packageId, string currentVersion, string pinned)
        {
            string oldPath = "packages\\" + packageId + "." + currentVersion + "\\lib";
            string newPath = "packages\\" + packageId + "." + pinned + "\\lib";
            return text.Replace(oldPath, newPath);
        }

        private static string ReadAllText(string path, out bool hasBom)
        {
            byte[] bytes = File.ReadAllBytes(path);
            hasBom = bytes.Length >= 3
                     && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

            Encoding encoding = hasBom ? new UTF8Encoding(true) : new UTF8Encoding(false);
            return encoding.GetString(bytes);
        }

        private static void WriteAllText(string path, string text, bool hasBom)
        {
            Encoding encoding = hasBom ? new UTF8Encoding(true) : new UTF8Encoding(false);
            File.WriteAllText(path, text, encoding);
        }
    }
}
