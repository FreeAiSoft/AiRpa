using System;
using System.IO;
using RPA.Helpers;

namespace RPA.Core.Generation
{
    /// <summary>
    /// Куда и в каком проекте сохраняется сгенерированный процесс (этапы 5–6 плана
    /// <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// Code-First и JSON AST обязаны выводить <c>x:Class</c>, имя сборки проекта и путь одинаково:
    /// два независимых вывода дают «в одном инструменте открывается, в другом нет» на одинаковом
    /// по смыслу запросе. Поэтому проверка пути и сборка параметров канонизации живут здесь, а не
    /// копируются в каждом сервисе генерации.
    /// </para>
    /// </summary>
    internal static class GenerationTarget
    {
        /// <summary>Подсказка «откуда взялось имя проекта», если его не передали явно.</summary>
        internal const string ProjectHint =
            "имя проекта выводится из имени папки файла процесса; если папка не проект — передайте project явно " +
            "(например RPA-1)";

        /// <summary>
        /// Проверяет путь к файлу процесса. Возвращает текст ошибки или <c>null</c>;
        /// <paramref name="fullPath"/> — полный путь (или <c>null</c>, если путь не задан).
        /// </summary>
        internal static string ValidateFilePath(string filePath, out string fullPath)
        {
            fullPath = null;

            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                fullPath = Path.GetFullPath(filePath.Trim());
            }
            catch (Exception ex)
            {
                return ErrorText.Describe($"xaml.path: путь «{filePath}» не принят", ex);
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".xaml", StringComparison.OrdinalIgnoreCase))
            {
                return "xaml.path: ожидался файл процесса с расширением .xaml";
            }

            return null;
        }

        /// <summary>
        /// Собирает параметры канонизации: приоритет — явные аргументы инструмента, затем значения
        /// из описания процесса (JSON AST), затем вывод из пути к файлу.
        /// </summary>
        /// <param name="projectName">Имя проекта (сборки) из аргументов инструмента.</param>
        /// <param name="namespaceName">Пространство имён класса процесса из аргументов инструмента.</param>
        /// <param name="className">Имя класса процесса из аргументов инструмента.</param>
        /// <param name="fullPath">Полный путь к файлу процесса (может быть <c>null</c>).</param>
        /// <param name="options">Параметры канонизации либо <c>null</c> при ошибке.</param>
        internal static string BuildOptions(
            string projectName,
            string namespaceName,
            string className,
            string fullPath,
            out WorkflowCanonicalizationOptions options)
        {
            options = null;

            string project = !string.IsNullOrWhiteSpace(projectName)
                ? projectName.Trim()
                : fullPath == null
                    ? null
                    : new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? ".").Name;

            if (string.IsNullOrWhiteSpace(project))
            {
                return "xaml.project: имя проекта не задано и не выводится из папки файла";
            }

            try
            {
                string resolvedNamespace = string.IsNullOrWhiteSpace(namespaceName)
                    ? WorkflowCanonicalizationOptions.NamespaceFromProjectName(project)
                    : namespaceName.Trim();

                string resolvedClass = !string.IsNullOrWhiteSpace(className)
                    ? className.Trim()
                    : fullPath == null
                        ? "Main"
                        : WorkflowCanonicalizationOptions.ClassNameFromFilePath(fullPath);

                options = new WorkflowCanonicalizationOptions(resolvedNamespace, resolvedClass, project);
                return null;
            }
            catch (Exception ex)
            {
                return ErrorText.Describe($"параметры канонизации не приняты: проект «{project}»", ex);
            }
        }
    }
}
