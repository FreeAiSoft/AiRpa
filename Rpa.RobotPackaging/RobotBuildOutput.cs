using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using RPA.Orchestrator.Contracts.Packages;

namespace RPA.Packaging
{
    /// <summary>
    /// Где лежит собранная сборка робота (`OR-03`).
    /// <para>
    /// Каталоги перебираются теми же правилами, что у раннера
    /// (<see cref="RobotProjectLayout.CandidateDirectories"/>): студия кладёт в
    /// <c>bin\x64\Debug</c>, SDK-style проект — в <c>bin\Debug\net472</c>, legacy — в <c>bin\Debug</c>.
    /// Сначала ищется запрошенная конфигурация, и только если её нет — другая, и это попадает
    /// в замечания пакета: тихо упаковать Debug вместо Release нельзя.
    /// </para>
    /// </summary>
    public sealed class RobotBuildOutput
    {
        private RobotBuildOutput()
        {
            Candidates = new List<string>();
            Notes = new List<string>();
        }

        /// <summary>Каталог, из которого берётся сборка.</summary>
        public string DirectoryPath { get; private set; }

        /// <summary>Сборка робота (точка входа).</summary>
        public string AssemblyPath { get; private set; }

        /// <summary>Все проверенные каталоги — для сообщения об ошибке.</summary>
        public List<string> Candidates { get; }

        /// <summary>Пояснения: какой каталог выбран, была ли подмена конфигурации.</summary>
        public List<string> Notes { get; }

        /// <summary>
        /// Находит собранную сборку. <c>false</c> — робот не собран: <paramref name="error"/>
        /// перечисляет проверенные каталоги и подсказывает собрать проект.
        /// </summary>
        public static bool TryFind(
            RobotProject project,
            string configuration,
            out RobotBuildOutput output,
            out string error)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            output = null;
            error = null;

            string requested = NormalizeConfiguration(configuration);
            string alternative = string.Equals(requested, "Release", StringComparison.OrdinalIgnoreCase)
                ? "Debug"
                : "Release";

            var result = new RobotBuildOutput();

            List<string> preferred = Directories(project.ProjectDirectory, requested, project.TargetFrameworks);
            List<string> fallback = Directories(project.ProjectDirectory, alternative, project.TargetFrameworks);

            foreach (string directory in preferred.Concat(fallback))
            {
                if (!result.Candidates.Contains(directory, StringComparer.OrdinalIgnoreCase))
                {
                    result.Candidates.Add(directory);
                }
            }

            List<string> found = FindAssemblies(preferred, project.AssemblyName);

            if (found.Count == 0)
            {
                found = FindAssemblies(fallback, project.AssemblyName);

                if (found.Count > 0)
                {
                    result.Notes.Add(
                        "сборки конфигурации " + requested + " нет — взята " + alternative
                        + ": " + Path.GetDirectoryName(found[0]));
                }
            }

            if (found.Count == 0)
            {
                var text = new System.Text.StringBuilder();
                text.Append("робот не собран: «").Append(project.AssemblyName).Append(".dll» не найдена").AppendLine();
                text.Append("искали в каталогах:");

                foreach (string directory in result.Candidates)
                {
                    text.AppendLine().Append("  ").Append(directory);
                }

                text.AppendLine().Append(
                    "Соберите проект (MSBuild или студия) и повторите упаковку, "
                    + "либо упакуйте уже собранный бинарник параметром «без сборки».");

                error = text.ToString();
                return false;
            }

            string chosen = found
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();

            if (found.Count > 1)
            {
                result.Notes.Add("подходящих сборок " + found.Count.ToString(CultureInfo.InvariantCulture)
                    + ", выбрана самая свежая: " + chosen);
            }

            result.AssemblyPath = chosen;
            result.DirectoryPath = Path.GetDirectoryName(chosen);
            output = result;

            return true;
        }

        /// <summary>Каталоги одной конфигурации: платформы × целевые платформы.</summary>
        internal static List<string> Directories(string projectDirectory, string configuration, IList<string> targetFrameworks)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var frameworks = new List<string> { string.Empty };

            if (targetFrameworks != null)
            {
                foreach (string framework in targetFrameworks)
                {
                    if (!string.IsNullOrWhiteSpace(framework)
                        && !frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
                    {
                        frameworks.Add(framework);
                    }
                }
            }

            foreach (string platform in RobotProjectLayout.Platforms)
            {
                foreach (string framework in frameworks)
                {
                    var parts = new List<string> { projectDirectory, "bin" };

                    if (!string.IsNullOrEmpty(platform))
                    {
                        parts.Add(platform);
                    }

                    parts.Add(configuration);

                    if (!string.IsNullOrEmpty(framework))
                    {
                        parts.Add(framework);
                    }

                    string directory = Path.Combine(parts.ToArray());

                    if (seen.Add(directory))
                    {
                        result.Add(directory);
                    }
                }
            }

            return result;
        }

        private static List<string> FindAssemblies(IEnumerable<string> directories, string assemblyName)
        {
            var result = new List<string>();

            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                string dll = Path.Combine(directory, assemblyName + ".dll");

                if (File.Exists(dll))
                {
                    result.Add(dll);
                }
            }

            return result;
        }

        /// <summary>«release» → «Release»; пусто — Release (пакет для эксплуатации, не для отладки).</summary>
        public static string NormalizeConfiguration(string configuration)
        {
            if (string.IsNullOrWhiteSpace(configuration))
            {
                return "Release";
            }

            string text = configuration.Trim();

            if (string.Equals(text, "debug", StringComparison.OrdinalIgnoreCase))
            {
                return "Debug";
            }

            if (string.Equals(text, "release", StringComparison.OrdinalIgnoreCase))
            {
                return "Release";
            }

            return text;
        }
    }
}
