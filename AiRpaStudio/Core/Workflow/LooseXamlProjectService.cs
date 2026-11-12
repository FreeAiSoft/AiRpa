using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RPA.Entities;
using RPA.ProjectModel;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Строит loose-сервис для проекта: сканирует XAML-файлы проекта в реестр
    /// <see cref="WorkflowTypeRegistry"/> и решает, включать ли loose-загрузку
    /// (вложенные XAML-процессы без сборки и без DLL).
    /// </summary>
    internal static class LooseXamlProjectService
    {
        /// <summary>
        /// Loose-загрузка включается только для WF-проектов (<c>IsWorkflowProject</c>):
        /// это activity library, где один XAML ссылается на другой по <c>clr-namespace</c>.
        /// Для обычных project.json/class-library проектов оставляем штатный путь дизайнера.
        /// </summary>
        internal static bool ShouldUse(Project project)
        {
            return project != null
                   && project.IsWorkflowProject
                   && !string.IsNullOrWhiteSpace(project.ProjectDirPath);
        }

        /// <summary>
        /// Строит реестр по XAML-файлам проекта. Предпочтительно — из элементов
        /// <c>XamlAppDef</c> в <c>.csproj</c> (ровно те файлы, которые компилируются в workflow);
        /// если их нет — все <c>*.xaml</c> в каталоге проекта вне <c>bin</c>/<c>obj</c>.
        /// </summary>
        internal static WorkflowTypeRegistry BuildRegistry(Project project)
        {
            if (!ShouldUse(project))
            {
                return null;
            }

            WorkflowTypeRegistry registry = WorkflowTypeRegistry.Scan(EnumerateXamlFiles(project));
            return registry.Count > 0 ? registry : null;
        }

        internal static IEnumerable<string> EnumerateXamlFiles(Project project)
        {
            var files = new List<string>();

            if (project.Items != null)
            {
                foreach (VsProjectItem item in project.Items)
                {
                    if (!IsXamlItem(item))
                    {
                        continue;
                    }

                    string fullPath = Path.IsPathRooted(item.Include)
                        ? item.Include
                        : Path.Combine(project.ProjectDirPath, item.Include);

                    files.Add(fullPath);
                }
            }

            if (files.Count == 0)
            {
                files.AddRange(Directory
                    .EnumerateFiles(project.ProjectDirPath, "*.xaml", SearchOption.AllDirectories)
                    .Where(path => !IsUnderIgnoredFolder(project.ProjectDirPath, path)));
            }

            return files;
        }

        private static bool IsXamlItem(VsProjectItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Include))
            {
                return false;
            }

            return string.Equals(item.ItemType, "XamlAppDef", StringComparison.OrdinalIgnoreCase)
                   || item.Include.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnderIgnoredFolder(string projectDir, string filePath)
        {
            string relative = Path.GetDirectoryName(Path.GetFullPath(filePath));

            if (string.IsNullOrWhiteSpace(relative))
            {
                return false;
            }

            string[] segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment =>
                string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "packages", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, ".vs", StringComparison.OrdinalIgnoreCase));
        }
    }
}
