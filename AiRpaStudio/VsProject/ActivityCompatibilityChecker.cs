using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RPA.ProjectModel;

namespace RPA.VsProject
{
    /// <summary>
    /// Проверяет проект библиотеки активностей (WF) на совместимость со студией по полному
    /// контракту из <c>AiRpaStudio/Docs/ru/activity-development.md</c>:
    /// TFM = net472, WF-тип проекта, пин-версии пересекающихся зависимостей, запрет
    /// дизайнерской сборки <c>Microsoft.Windows.Design.Interaction</c> и её атрибутов,
    /// точные зависимости в <c>.nuspec</c>.
    /// </summary>
    public class ActivityCompatibilityChecker
    {
        private static readonly string[] SkippedDirectories = { "bin", "obj", "packages", ".git", ".vs", "node_modules" };

        /// <summary>Проверяет проект и возвращает отчёт (без правки файлов).</summary>
        public ActivityCompatibilityReport Check(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("Путь к файлу проекта не указан.", nameof(projectPath));
            }

            string fullPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Файл проекта не найден.", fullPath);
            }

            VsProjectInfo info;
            try
            {
                info = VsProjectParser.Parse(fullPath);
            }
            catch (Exception ex)
            {
                ActivityCompatibilityReport parseFailure = new ActivityCompatibilityReport
                {
                    ProjectPath = fullPath,
                    ProjectName = Path.GetFileNameWithoutExtension(fullPath)
                };
                parseFailure.Issues.Add(Error(
                    "PROJECT_PARSE",
                    "Не удалось разобрать файл проекта: " + ex.Message,
                    "Проверьте, что файл является корректным .csproj.",
                    fullPath));
                return parseFailure;
            }

            ActivityCompatibilityReport report = new ActivityCompatibilityReport
            {
                ProjectPath = fullPath,
                ProjectName = info.Name ?? Path.GetFileNameWithoutExtension(fullPath)
            };

            string projectDirectory = Path.GetDirectoryName(fullPath);

            CheckTargetFramework(info, fullPath, report);
            CheckWorkflowProjectType(info, fullPath, report);
            CheckPackages(info, fullPath, report);
            CheckHintPaths(info, fullPath, report);
            CheckReferences(info, fullPath, report);
            CheckSources(projectDirectory, report);
            CheckNuspecFiles(projectDirectory, report);

            return report;
        }

        private static void CheckTargetFramework(VsProjectInfo info, string projectPath, ActivityCompatibilityReport report)
        {
            if (info.TargetFrameworks.Count == 0)
            {
                report.Issues.Add(Error(
                    "TFM_MISSING",
                    "В проекте не найден целевой фреймворк (ни TargetFramework, ни TargetFrameworkVersion).",
                    "Укажите TargetFrameworkVersion v4.7.2 (net472).",
                    projectPath));
                return;
            }

            if (info.TargetFrameworks.Count > 1)
            {
                report.Issues.Add(Error(
                    "TFM_MULTI",
                    "Проект использует мультитаргетинг (" + string.Join("; ", info.TargetFrameworks) + ").",
                    "Библиотека активностей обязана собираться только под net472.",
                    projectPath));
                return;
            }

            string tfm = info.TargetFrameworks[0];
            if (!string.Equals(tfm, ActivityCompatibilityContract.RequiredTfm, StringComparison.OrdinalIgnoreCase))
            {
                report.Issues.Add(Error(
                    "TFM_WRONG",
                    "Целевой фреймворк " + tfm + " несовместим со студией.",
                    "Приведите TFM к net472 (TargetFrameworkVersion v4.7.2).",
                    projectPath));
            }
        }

        private static void CheckWorkflowProjectType(VsProjectInfo info, string projectPath, ActivityCompatibilityReport report)
        {
            if (!info.IsWorkflowProject)
            {
                report.Issues.Add(Error(
                    "NOT_WORKFLOW_PROJECT",
                    "Проект не является библиотекой активностей Windows Workflow Foundation.",
                    "Создайте проект по шаблону WF Activity Library (ProjectTypeGuids должен содержать " + VsProjectParser.WorkflowProjectTypeGuid + ").",
                    projectPath));
            }
        }

        private static void CheckPackages(VsProjectInfo info, string projectPath, ActivityCompatibilityReport report)
        {
            string packagesConfigPath = Path.Combine(Path.GetDirectoryName(projectPath) ?? ".", "packages.config");

            foreach (VsProjectPackage package in info.Packages)
            {
                string pinned = ActivityCompatibilityContract.TryGetPinnedVersion(package.Id);
                if (pinned == null)
                {
                    continue;
                }

                if (string.Equals(package.Version, pinned, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ActivityCompatibilityFixTarget target;
                string filePath;
                if (package.Source == VsPackageSource.PackagesConfig)
                {
                    target = ActivityCompatibilityFixTarget.PackagesConfigVersion;
                    filePath = packagesConfigPath;
                }
                else if (package.IsCentralVersion)
                {
                    // Central package management: правка в Directory.Packages.props — не точечная,
                    // поэтому автофикс не предлагаем.
                    target = ActivityCompatibilityFixTarget.None;
                    filePath = projectPath;
                }
                else
                {
                    target = ActivityCompatibilityFixTarget.PackageReferenceVersion;
                    filePath = projectPath;
                }

                report.Issues.Add(Issue(
                    "PACKAGE_VERSION",
                    ActivityCompatibilitySeverity.Error,
                    package.Id + " " + (package.Version ?? "<без версии>") + " вместо " + pinned,
                    "Установите версию " + pinned + ".",
                    filePath,
                    package.Id,
                    package.Version,
                    pinned,
                    target));
            }
        }

        private static void CheckHintPaths(VsProjectInfo info, string projectPath, ActivityCompatibilityReport report)
        {
            foreach (VsProjectItem item in info.Items)
            {
                if (!string.Equals(item.ItemType, "Reference", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(item.HintPath))
                {
                    continue;
                }

                if (!TryParsePackageHintPath(item.HintPath, out string packageId, out string version))
                {
                    continue;
                }

                string pinned = ActivityCompatibilityContract.TryGetPinnedVersion(packageId);
                if (pinned == null || string.Equals(version, pinned, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                report.Issues.Add(Issue(
                    "HINTPATH_VERSION",
                    ActivityCompatibilitySeverity.Error,
                    "HintPath ссылается на " + packageId + " " + version + " вместо " + pinned,
                    "Обновите HintPath ссылки на " + packageId + " " + pinned + ".",
                    projectPath,
                    packageId,
                    version,
                    pinned,
                    ActivityCompatibilityFixTarget.HintPathVersion));
            }
        }

        /// <summary>
        /// Разбирает HintPath вида <c>..\packages\Имя.Пакета.1.2.3\lib\...</c> на идентификатор
        /// пакета и версию. Так как id пакета может содержать точки, версию отделяем по самому
        /// длинному совпадающему пин-пакету из эталона (префикс не перекрывает более длинный id).
        /// </summary>
        private static bool TryParsePackageHintPath(string hintPath, out string packageId, out string version)
        {
            packageId = null;
            version = null;

            int libIndex = hintPath.IndexOf("\\lib", StringComparison.OrdinalIgnoreCase);
            if (libIndex < 0)
            {
                return false;
            }

            const string packagesMarker = "packages\\";
            int packagesIndex = hintPath.IndexOf(packagesMarker, StringComparison.OrdinalIgnoreCase);
            if (packagesIndex < 0)
            {
                return false;
            }

            int segmentStart = packagesIndex + packagesMarker.Length;
            string segment = hintPath.Substring(segmentStart, libIndex - segmentStart);

            string best = null;
            foreach (string candidate in ActivityCompatibilityContract.PinnedVersions.Keys)
            {
                if (segment.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase))
                {
                    if (best == null || candidate.Length > best.Length)
                    {
                        best = candidate;
                    }
                }
            }

            if (best == null)
            {
                return false;
            }

            packageId = best;
            version = segment.Substring(best.Length + 1);
            return version.Length > 0;
        }

        private static void CheckReferences(VsProjectInfo info, string projectPath, ActivityCompatibilityReport report)
        {
            foreach (VsProjectItem item in info.Items)
            {
                string include = item.Include ?? string.Empty;

                if (string.Equals(item.ItemType, "Reference", StringComparison.OrdinalIgnoreCase)
                    && include.IndexOf(ActivityCompatibilityContract.ForbiddenDesignerAssembly, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    report.Issues.Add(Error(
                        "DESIGNER_ASSEMBLY_REFERENCE",
                        "Проект ссылается на дизайнерскую сборку " + ActivityCompatibilityContract.ForbiddenDesignerAssembly + ".",
                        "Удалите ссылку на " + ActivityCompatibilityContract.ForbiddenDesignerAssembly + " и не используйте [ToolboxBrowsable]/[ToolboxItem].",
                        projectPath));
                }

                bool referencesLegacyActivities =
                    (string.Equals(item.ItemType, "Reference", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(item.ItemType, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                    && (string.Equals(include, ActivityCompatibilityContract.LegacyActivitiesAssembly, StringComparison.OrdinalIgnoreCase)
                        || include.EndsWith(ActivityCompatibilityContract.LegacyActivitiesAssembly + ".csproj", StringComparison.OrdinalIgnoreCase));

                if (referencesLegacyActivities)
                {
                    report.Issues.Add(Issue(
                        "LEGACY_ACTIVITIES_REFERENCE",
                        ActivityCompatibilitySeverity.Warning,
                        "Проект ссылается на устаревшую сборку Activities.",
                        "Замените ссылку на ActivityLibraryBase — она не тянет конфликтный граф Microsoft.Extensions.* 2.x.",
                        projectPath,
                        null, null, null,
                        ActivityCompatibilityFixTarget.None));
                }
            }
        }

        private static void CheckSources(string projectDirectory, ActivityCompatibilityReport report)
        {
            foreach (string source in EnumerateFiles(projectDirectory, "*.cs"))
            {
                string text;
                try
                {
                    text = File.ReadAllText(source);
                }
                catch (Exception)
                {
                    continue;
                }

                if (text.Contains("[ToolboxBrowsable"))
                {
                    report.Issues.Add(Error(
                        "TOOLBOX_BROWSABLE_ATTRIBUTE",
                        "Исходный файл содержит [ToolboxBrowsable].",
                        "Удалите [ToolboxBrowsable] — дизайнерские атрибуты ломают категорию палитры.",
                        source));
                }

                if (text.Contains("using Microsoft.Windows.Design;"))
                {
                    report.Issues.Add(Error(
                        "DESIGNER_USING",
                        "Исходный файл содержит using Microsoft.Windows.Design;.",
                        "Удалите using и ссылку на дизайнерскую сборку.",
                        source));
                }
            }
        }

        private static void CheckNuspecFiles(string projectDirectory, ActivityCompatibilityReport report)
        {
            foreach (string nuspec in EnumerateFiles(projectDirectory, "*.nuspec"))
            {
                XDocument document;
                try
                {
                    document = XDocument.Load(nuspec);
                }
                catch (Exception ex)
                {
                    report.Issues.Add(Error(
                        "NUSPEC_PARSE",
                        "Не удалось разобрать " + Path.GetFileName(nuspec) + ": " + ex.Message,
                        "Проверьте корректность XML файла .nuspec.",
                        nuspec));
                    continue;
                }

                if (document.Root == null)
                {
                    continue;
                }

                XNamespace ns = document.Root.Name.Namespace;
                foreach (XElement dependency in document.Descendants(ns + "dependency"))
                {
                    string id = (string)dependency.Attribute("id");
                    string version = (string)dependency.Attribute("version");
                    string pinned = ActivityCompatibilityContract.TryGetPinnedVersion(id);
                    if (pinned == null)
                    {
                        continue;
                    }

                    bool isExact = !string.IsNullOrWhiteSpace(version) && !HasVersionRange(version);
                    if (!isExact)
                    {
                        report.Issues.Add(Issue(
                            "NUSPEC_VERSION_RANGE",
                            ActivityCompatibilitySeverity.Error,
                            "В .nuspec зависимость " + id + " имеет неточную версию (" + (version ?? "<не указана>") + ").",
                            "Укажите точную версию " + pinned + ".",
                            nuspec,
                            id,
                            version,
                            pinned,
                            ActivityCompatibilityFixTarget.NuspecDependencyVersion));
                        continue;
                    }

                    if (!string.Equals(version, pinned, StringComparison.OrdinalIgnoreCase))
                    {
                        report.Issues.Add(Issue(
                            "NUSPEC_VERSION",
                            ActivityCompatibilitySeverity.Error,
                            "В .nuspec зависимость " + id + " " + version + " вместо " + pinned,
                            "Укажите точную версию " + pinned + ".",
                            nuspec,
                            id,
                            version,
                            pinned,
                            ActivityCompatibilityFixTarget.NuspecDependencyVersion));
                    }
                }
            }
        }

        private static IEnumerable<string> EnumerateFiles(string directory, string pattern)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return Enumerable.Empty<string>();
            }

            return Directory.GetFiles(directory, pattern, SearchOption.AllDirectories)
                .Where(path => !IsSkipped(path));
        }

        private static bool IsSkipped(string path)
        {
            string[] segments = path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment => SkippedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
        }

        private static bool HasVersionRange(string version)
        {
            return version.IndexOfAny(new[] { '[', '(', ',', '*' }) >= 0;
        }

        private static ActivityCompatibilityIssue Error(string code, string message, string fixHint, string filePath)
        {
            return Issue(code, ActivityCompatibilitySeverity.Error, message, fixHint, filePath, null, null, null, ActivityCompatibilityFixTarget.None);
        }

        private static ActivityCompatibilityIssue Issue(
            string code,
            ActivityCompatibilitySeverity severity,
            string message,
            string fixHint,
            string filePath,
            string packageId,
            string currentVersion,
            string pinnedVersion,
            ActivityCompatibilityFixTarget fixTarget)
        {
            return new ActivityCompatibilityIssue
            {
                Code = code,
                Severity = severity,
                Message = message,
                FixHint = fixHint,
                FilePath = filePath,
                PackageId = packageId,
                CurrentVersion = currentVersion,
                PinnedVersion = pinnedVersion,
                FixTarget = fixTarget
            };
        }
    }
}
