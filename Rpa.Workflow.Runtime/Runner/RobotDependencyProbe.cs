using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RPA.Runner
{
    /// <summary>
    /// Поиск зависимостей робота в его собственном каталоге (`OR-02`, параметр <c>--bin</c>).
    /// <para>
    /// В студии этим занимается <c>RobotAssemblyResolver</c> со сложными правилами, потому что
    /// в одном домене живут и хост 4.8, и робот 4.7.2: там нельзя подменять <c>System.*</c>
    /// версиями робота. У раннера хоста-студии нет — в процессе только сам раннер и рантайм,
    /// поэтому правило одно и простое: «имя не наше — ищем файл в каталоге робота». Сборки
    /// рантайма (<c>Rpa.Workflow.Runtime</c>, <c>ActivitiesTracker</c>, <c>System.*</c>) из
    /// каталога робота не подменяются никогда.
    /// </para>
    /// <para>
    /// Обработчик не бросает исключений: не нашли — возвращаем <c>null</c> и даём штатно упасть
    /// привязке с внятным <c>FileNotFoundException</c>. Подписка идемпотентна.
    /// </para>
    /// </summary>
    public static class RobotDependencyProbe
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<string> Directories = new List<string>();
        private static bool _installed;

        /// <summary>Имена сборок рантайма: из каталога робота не обслуживаются.</summary>
        private static readonly string[] RuntimeOwnedNames =
        {
            "Rpa.Workflow.Runtime", "ActivitiesTracker", "RpaRunner"
        };

        /// <summary>Добавляет каталог в список поиска зависимостей и ставит обработчик (один раз на процесс).</summary>
        public static void Install(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string full = Path.GetFullPath(directory);

            lock (SyncRoot)
            {
                if (!Directories.Contains(full, StringComparer.OrdinalIgnoreCase))
                {
                    Directories.Add(full);
                }

                if (_installed)
                {
                    return;
                }

                _installed = true;
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            }
        }

        /// <summary>Каталоги поиска (для диагностики и тестов).</summary>
        public static IReadOnlyList<string> SearchDirectories
        {
            get
            {
                lock (SyncRoot)
                {
                    return Directories.ToArray();
                }
            }
        }

        /// <summary>Ищет файл сборки по простому имени: используется обработчиком и тестами.</summary>
        public static string FindAssemblyFile(string simpleName)
        {
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return null;
            }

            string[] directories;

            lock (SyncRoot)
            {
                directories = Directories.ToArray();
            }

            for (int index = directories.Length - 1; index >= 0; index--)
            {
                string candidate = Path.Combine(directories[index], simpleName + ".dll");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string simpleName = new AssemblyName(args.Name).Name;

                if (IsRuntimeOwned(simpleName))
                {
                    return null;
                }

                string file = FindAssemblyFile(simpleName);

                return file == null ? null : Assembly.LoadFrom(file);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Сборка принадлежит рантайму раннера и из каталога робота не берётся.</summary>
        public static bool IsRuntimeOwned(string simpleName)
        {
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return false;
            }

            if (simpleName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                || simpleName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(simpleName, "mscorlib", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (string owned in RuntimeOwnedNames)
            {
                if (string.Equals(simpleName, owned, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
