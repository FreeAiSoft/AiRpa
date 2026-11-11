using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using RPA.Logging;

namespace RPA.Helpers
{
    /// <summary>
    /// Разрешение зависимостей робота из его собственного выходного каталога (`CP-08`).
    /// <para>
    /// Студия живёт на .NET Framework 4.8, робот и его библиотеки — на 4.7.2. Сборки 4.7.2
    /// загружаются в AppDomain студии и работают (проверено `CP-09`), но их <b>зависимости</b>
    /// лежат рядом с роботом (`bin\Debug` или `bin\Debug\net48`), а не в папке студии:
    /// у робота может быть свой `Newtonsoft.Json`, своя библиотека активностей, свой клиент базы.
    /// Без этого обработчика такая сборка не находится и WF падает с <c>FileNotFoundException</c>
    /// при десериализации XAML.
    /// </para>
    /// <para>
    /// Правила (сознательно узкие, чтобы не ломать студию):
    /// <list type="bullet">
    /// <item>из каталога робота обслуживаются только имена, которые не принадлежат хосту
    /// (<see cref="IsHostOwnedAssemblyName"/>) — студийные `System.*`, WF, `Activities*`,
    /// `ActivitiesTracker`, `RPA*` не подменяются версиями робота никогда;</item>
    /// <item>понижение версии запрещено: файл с версией меньше запрошенной не подставляется;</item>
    /// <item>открытый ключ, если он есть в запросе, обязан совпасть;</item>
    /// <item>обработчик никогда не бросает исключений — если ничего не нашлось, возвращается
    /// <c>null</c> и привязка завершается штатной ошибкой;</item>
    /// <item>подписка идемпотентна: повторный <see cref="Install"/> не вешает второй обработчик.</item>
    /// </list>
    /// Порядок с <see cref="AssemblyBindingUnifier"/>: унификатор подписан на старте приложения,
    /// поэтому срабатывает первым (сверка версий внутри домена), а этот обработчик — вторым,
    /// как последний шанс найти сборку в каталоге робота.
    /// </para>
    /// </summary>
    internal static class RobotAssemblyResolver
    {
        /// <summary>
        /// Точные имена сборок хоста: из каталога робота не обслуживаются никогда (иначе одна версия
        /// `System.*`/WF на два рантайма в одном AppDomain).
        /// </summary>
        private static readonly string[] HostOwnedExactNames =
        {
            "mscorlib", "WindowsBase", "PresentationCore", "PresentationFramework", "ReachFramework",
            "Accessibility", "UIAutomationClient", "UIAutomationTypes", "WindowsFormsIntegration",
            "ActivityLibraryBase", "ActivitiesTracker", "RpaCommon", "LogControlLibrary", "FW.EasyMvvm", "EasyMvvm",
            "MaterialDesignThemes", "MaterialDesignColors", "Material.Icons", "Material.Icons.WPF"
        };

        /// <summary>Пространства имён хоста: имя с этим префиксом и точкой/подчёркиванием — сборка хоста.</summary>
        private static readonly string[] HostOwnedPrefixes =
        {
            "System", "Microsoft", "Presentation", "Activities", "RPA", "Rpa", "FW",
            "AvalonDock", "ICSharpCode", "MahApps", "Material", "ControlzEx", "NuGet"
        };

        private static readonly object SyncRoot = new object();

        private static int _subscribeCount;
        private static AppDomain _domain;
        private static List<string> _probeDirectories = new List<string>();

        /// <summary>Обработчик установлен в текущем домене.</summary>
        internal static bool IsInstalled => _subscribeCount > 0;

        /// <summary>Количество фактических подписок (для проверки идемпотентности).</summary>
        internal static int SubscribeCount => _subscribeCount;

        /// <summary>Каталоги, в которых ищутся зависимости робота (в порядке приоритета).</summary>
        internal static ReadOnlyCollection<string> ProbeDirectories =>
            new ReadOnlyCollection<string>(_probeDirectories.ToList());

        /// <summary>
        /// Ставит обработчик <see cref="AppDomain.AssemblyResolve"/> для указанных каталогов.
        /// Повторный вызов не вешает второй обработчик: каталоги заменяются.
        /// </summary>
        internal static void Install(AppDomain domain, IEnumerable<string> probeDirectories)
        {
            if (domain == null)
            {
                return;
            }

            List<string> existing = Normalize(probeDirectories);
            if (existing.Count == 0)
            {
                // Нечего искать: подписка без каталогов только вводила бы в заблуждение в логе.
                return;
            }

            lock (SyncRoot)
            {
                _probeDirectories = existing;

                if (_subscribeCount > 0)
                {
                    return;
                }

                _domain = domain;
                domain.AssemblyResolve += ResolveAssembly;
                _subscribeCount++;
            }
        }

        /// <summary>
        /// Ставит обработчик для проекта робота: каталоги вычисляются из каталога проекта
        /// (<c>bin\Debug</c>, <c>bin\Debug\net48</c> и т.д.). Не бросает исключений.
        /// </summary>
        internal static void InstallForProject(string projectDirectory)
        {
            try
            {
                Install(AppDomain.CurrentDomain, GetProbeDirectories(projectDirectory));
            }
            catch (Exception ex)
            {
                Log.Write("Зависимости робота: не удалось поставить обработчик поиска сборок.", ex);
            }
        }

        /// <summary>Снимает обработчик и очищает каталоги поиска.</summary>
        internal static void Uninstall()
        {
            lock (SyncRoot)
            {
                if (_subscribeCount > 0 && _domain != null)
                {
                    try
                    {
                        _domain.AssemblyResolve -= ResolveAssembly;
                    }
                    catch (Exception)
                    {
                        // Домен уже выгружен — снимать нечего.
                    }
                }

                _subscribeCount = 0;
                _domain = null;
                _probeDirectories = new List<string>();
            }
        }

        /// <summary>
        /// Обработчик <see cref="AppDomain.AssemblyResolve"/>: ищет зависимость робота в его
        /// выходных каталогах. Не бросает исключений.
        /// </summary>
        internal static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                string path = FindAssemblyPath(args?.Name, _probeDirectories);
                if (path == null)
                {
                    return null;
                }

                Assembly assembly = Assembly.LoadFrom(path);
                Log.Write($"Зависимости робота: запрос «{args?.Name}» обслужен сборкой «{path}».");

                return assembly;
            }
            catch (Exception ex)
            {
                Log.Write($"Зависимости робота: обработчик не смог загрузить сборку для запроса «{args?.Name}».", ex);
                return null;
            }
        }

        /// <summary>
        /// Ищет файл сборки для запроса в каталогах робота. Возвращает <c>null</c>, если имя
        /// принадлежит хосту, файла нет, версия в файле ниже запрошенной или не совпал открытый ключ.
        /// </summary>
        internal static string FindAssemblyPath(string requestedFullName, IEnumerable<string> probeDirectories)
        {
            if (string.IsNullOrWhiteSpace(requestedFullName) || probeDirectories == null)
            {
                return null;
            }

            AssemblyName requested;
            try
            {
                requested = new AssemblyName(requestedFullName);
            }
            catch (Exception)
            {
                // «Не имя сборки» — не наша задача, штатная ошибка привязки.
                return null;
            }

            if (string.IsNullOrEmpty(requested.Name) || IsHostOwnedAssemblyName(requested.Name))
            {
                return null;
            }

            foreach (string directory in probeDirectories)
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                string candidatePath = FindFile(directory, requested.Name);
                if (candidatePath != null && Matches(candidatePath, requested))
                {
                    return candidatePath;
                }
            }

            return null;
        }

        /// <summary>
        /// Имя принадлежит хосту (студия, WF, `System.*`): из каталога робота не обслуживается.
        /// </summary>
        internal static bool IsHostOwnedAssemblyName(string simpleName)
        {
            if (string.IsNullOrWhiteSpace(simpleName))
            {
                return true;
            }


            if (HostOwnedExactNames.Contains(simpleName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (string prefix in HostOwnedPrefixes)
            {
                if (string.Equals(simpleName, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (simpleName.Length > prefix.Length
                    && simpleName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && (simpleName[prefix.Length] == '.' || simpleName[prefix.Length] == '_'))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Каталоги, где лежат зависимости робота: `bin\Debug`, `bin\Debug\net48`, `bin\Release`
        /// и т.д. Отсутствующие каталоги отбрасываются.
        /// </summary>
        internal static List<string> GetProbeDirectories(string projectDirectory)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                return result;
            }

            string[] configurations = { "Debug", "Release" };

            foreach (string configuration in configurations)
            {
                string binDirectory = Path.Combine(projectDirectory, "bin", configuration);
                if (!Directory.Exists(binDirectory))
                {
                    continue;
                }

                // SDK-style кладёт сборку в подкаталог TFM, legacy — прямо в bin\<Config>.
                result.Add(binDirectory);

                result.AddRange(Directory.GetDirectories(binDirectory)
                    .Where(directory => Path.GetFileName(directory).StartsWith("net", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase));
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> Normalize(IEnumerable<string> probeDirectories)
        {
            if (probeDirectories == null)
            {
                return new List<string>();
            }

            return probeDirectories
                .Where(directory => !string.IsNullOrWhiteSpace(directory))
                .ToList();
        }

        private static string FindFile(string directory, string assemblyName)
        {
            string expected = Path.Combine(directory, assemblyName + ".dll");
            if (File.Exists(expected))
            {
                return expected;
            }

            try
            {
                return Directory
                    .GetFiles(directory, assemblyName + ".dll", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool Matches(string candidatePath, AssemblyName requested)
        {
            AssemblyName candidate;
            try
            {
                candidate = AssemblyName.GetAssemblyName(candidatePath);
            }
            catch (Exception)
            {
                // Битый или не-.NET файл: подставлять его нельзя.
                return false;
            }

            if (!string.Equals(candidate.Name, requested.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (requested.Version != null && candidate.Version != null && candidate.Version < requested.Version)
            {
                return false;
            }

            byte[] requestedToken = requested.GetPublicKeyToken();
            if (requestedToken == null || requestedToken.Length == 0)
            {
                return true;
            }

            byte[] candidateToken = candidate.GetPublicKeyToken();

            return candidateToken != null
                   && candidateToken.Length == requestedToken.Length
                   && candidateToken.SequenceEqual(requestedToken);
        }
    }
}
