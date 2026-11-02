using System;
using System.IO;
using RPA.Entities;
using RPA.Enums;

namespace RPA.Helpers
{
    /// <summary>
    /// Единая точка определения папки проекта.
    /// <para>
    /// В <c>Properties.Settings.LastLoadedProjectPath</c> и в узлах дерева проекта может лежать как папка,
    /// так и файл проекта (<c>.csproj</c>/<c>.sln</c>/<c>project.json</c>). Пока путь использовался «как есть»,
    /// диалог создания активности получал файл <c>ActivityLibrary5.csproj</c> и падал с ошибкой
    /// «Папка ...ActivityLibrary5.csproj не существует». Поэтому все решения о папке и о принадлежности
    /// пути проекту идут через этот резолвер, а не через строковые сравнения на месте.
    /// </para>
    /// </summary>
    public static class ProjectPathResolver
    {
        /// <summary>Расширения файлов, которые считаются «файлом проекта» (папка — их каталог).</summary>
        private static readonly string[] ProjectFileExtensions = { ".csproj", ".vbproj", ".fsproj", ".sln" };

        /// <summary>
        /// Возвращает папку проекта по пути к папке или к файлу проекта.
        /// <c>null</c>, если путь пустой или непригоден.
        /// </summary>
        public static string GetProjectFolder(string pathOrFile)
        {
            if (string.IsNullOrWhiteSpace(pathOrFile))
            {
                return null;
            }

            string candidate = pathOrFile.Trim().Trim('"').Trim();
            if (candidate.Length == 0)
            {
                return null;
            }

            string fullPath = TryGetFullPath(candidate);
            if (fullPath == null)
            {
                return null;
            }

            if (Directory.Exists(fullPath))
            {
                return Normalize(fullPath);
            }

            if (File.Exists(fullPath))
            {
                return TryGetDirectoryName(fullPath);
            }

            // Файла нет: если по расширению это файл проекта, ошибку нужно показывать про папку,
            // а не про несуществующий «.csproj»-каталог.
            if (LooksLikeProjectFile(fullPath))
            {
                return TryGetDirectoryName(fullPath);
            }

            return Normalize(fullPath);
        }

        /// <summary>
        /// Проверяет, что путь лежит внутри папки проекта (или совпадает с ней).
        /// Сравнение регистронезависимое и по границе каталога, поэтому <c>C:\proj2</c> не считается
        /// частью <c>C:\proj</c> (в отличие от <c>string.Contains</c>).
        /// </summary>
        public static bool IsInsideProject(string path, string projectRootOrFile)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string target = TryGetFullPath(path.Trim().Trim('"').Trim());
            if (target == null)
            {
                return false;
            }

            target = Normalize(target);

            string root = GetProjectFolder(projectRootOrFile);
            if (string.IsNullOrEmpty(root))
            {
                // Проект неизвестен — не ограничиваем пользователя.
                return true;
            }

            root = Normalize(root);
            if (string.Equals(target, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;

            return target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Папка, которой соответствует узел дерева: для папок — сам путь, для файлов
        /// (<c>.csproj</c>, <c>.sln</c>, обычный файл) — содержащий каталог.
        /// </summary>
        public static string GetNodeFolder(FSNode node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.RuntimeFullPath))
            {
                return null;
            }

            string path = Normalize(node.RuntimeFullPath);
            if (path == null)
            {
                return null;
            }

            switch (node.FSNodeType)
            {
                case FSNodeType.Dir:
                case FSNodeType.Project:
                    return path;

                default:
                    return TryGetDirectoryName(path);
            }
        }

        /// <summary>Узел считается контейнером (в него можно добавлять файлы и папки).</summary>
        public static bool IsContainerNode(FSNode node)
        {
            if (node == null)
            {
                return false;
            }

            return node.FSNodeType == FSNodeType.Dir
                   || node.FSNodeType == FSNodeType.Project
                   || node.FSNodeType == FSNodeType.VsProject
                   || node.FSNodeType == FSNodeType.Solution;
        }

        /// <summary>Сравнивает два пути как пути файловой системы (регистронезависимо).</summary>
        public static bool AreSamePath(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            string left = TryGetFullPath(first.Trim());
            string right = TryGetFullPath(second.Trim());
            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string result = path.Trim();
            if (result.Length > 3)
            {
                result = result.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }

            return result.Length == 0 ? null : result;
        }

        private static string TryGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }

        private static string TryGetDirectoryName(string path)
        {
            try
            {
                return Normalize(Path.GetDirectoryName(path));
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool LooksLikeProjectFile(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }

            foreach (string known in ProjectFileExtensions)
            {
                if (string.Equals(known, extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
