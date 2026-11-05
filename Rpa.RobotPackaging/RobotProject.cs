using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using RPA.Orchestrator.Contracts.Packages;

namespace RPA.Packaging
{
    /// <summary>
    /// Снимок проекта робота, из которого собирается пакет (`OR-03`).
    /// <para>
    /// Читается без сборки и без студии: имя сборки, целевая платформа, версия, точка входа
    /// и аргументы берутся из <c>csproj</c> и <c>Main.xaml</c> теми же правилами, что у раннера
    /// (<see cref="RobotProjectLayout"/>). Поэтому пакет и прогон всегда говорят об одном и том же
    /// роботе — даже если проект переименовали вчера.
    /// </para>
    /// </summary>
    public sealed class RobotProject
    {
        private RobotProject()
        {
            TargetFrameworks = new List<string>();
            Arguments = new List<RobotPackageArgument>();
            WorkflowFiles = new List<string>();
        }

        /// <summary>Полный путь к <c>*.csproj</c>.</summary>
        public string ProjectPath { get; private set; }

        /// <summary>Каталог проекта.</summary>
        public string ProjectDirectory { get; private set; }

        /// <summary>Имя сборки робота (оно же <c>id</c> пакета).</summary>
        public string AssemblyName { get; private set; }

        /// <summary>Целевые платформы проекта в нормализованном виде.</summary>
        public List<string> TargetFrameworks { get; private set; }

        /// <summary>Первая целевая платформа — она попадает в манифест.</summary>
        public string TargetFramework
        {
            get { return TargetFrameworks.Count > 0 ? TargetFrameworks[0] : "net472"; }
        }

        /// <summary>Версия по умолчанию из проекта (перекрывается параметром упаковки).</summary>
        public string Version { get; private set; }

        /// <summary>Точка входа: полное имя типа активности из <c>x:Class</c>.</summary>
        public string EntryType { get; private set; }

        /// <summary>Файл сценария — точки входа (обычно <c>Main.xaml</c>); может быть <c>null</c>.</summary>
        public string WorkflowPath { get; private set; }

        /// <summary>Все XAML-файлы проекта, попадающие в пакет (без <c>bin</c> и <c>obj</c>).</summary>
        public List<string> WorkflowFiles { get; private set; }

        /// <summary>Аргументы робота из <c>x:Members</c>.</summary>
        public List<RobotPackageArgument> Arguments { get; private set; }

        /// <summary>Замечания разбора проекта, не мешающие упаковке.</summary>
        public List<string> Warnings { get; private set; } = new List<string>();

        /// <summary>
        /// Читает проект робота. <c>false</c> — проект не найден, не читается или из него нельзя
        /// получить робота (нет имени сборки или точки входа); <paramref name="error"/> содержит причину.
        /// </summary>
        public static bool TryLoad(string projectPath, out RobotProject project, out string error)
        {
            project = null;
            error = null;

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                error = "путь к проекту робота не указан";
                return false;
            }

            string full;

            try
            {
                full = Path.GetFullPath(projectPath);
            }
            catch (Exception ex)
            {
                error = "путь к проекту «" + projectPath + "» не разобран: " + ex.Message;
                return false;
            }

            if (!File.Exists(full))
            {
                error = "проект робота не найден: " + full;
                return false;
            }

            XDocument document = RobotProjectLayout.LoadProject(full, out string loadError);

            if (document == null)
            {
                error = "не удалось прочитать проект «" + full + "»: " + loadError;
                return false;
            }

            string directory = Path.GetDirectoryName(full);

            var result = new RobotProject
            {
                ProjectPath = full,
                ProjectDirectory = directory,
                AssemblyName = RobotProjectLayout.ReadAssemblyName(document, full),
                Version = RobotProjectLayout.ReadVersion(document)
            };

            foreach (string framework in RobotProjectLayout.ReadTargetFrameworks(document))
            {
                result.TargetFrameworks.Add(framework);
            }

            if (result.TargetFrameworks.Count == 0)
            {
                result.TargetFrameworks.Add("net472");
                result.Warnings.Add("в проекте не указана целевая платформа — считаем net472");
            }
            else if (!string.Equals(result.TargetFramework, "net472", StringComparison.OrdinalIgnoreCase))
            {
                result.Warnings.Add(
                    "робот целится в " + result.TargetFramework + ", а не в net472: "
                    + "на машине без этой версии .NET Framework запуск не пройдёт");
            }

            result.WorkflowPath = FindDefaultWorkflow(directory, out string workflowWarning);

            if (workflowWarning != null)
            {
                result.Warnings.Add(workflowWarning);
            }

            if (result.WorkflowPath != null)
            {
                result.EntryType = RobotProjectLayout.ReadEntryType(result.WorkflowPath);
                result.Arguments.AddRange(RobotProjectLayout.ReadArguments(result.WorkflowPath));
            }

            foreach (string xaml in FindWorkflowFiles(directory))
            {
                result.WorkflowFiles.Add(xaml);
            }

            if (string.IsNullOrWhiteSpace(result.EntryType))
            {
                error = "в проекте «" + full + "» не найдена точка входа: нет файла "
                    + RobotProjectLayout.DefaultWorkflowFileName + " или в нём нет x:Class"
                    + Environment.NewLine
                    + "Пакет без точки входа запустить нельзя: укажите имя типа в XAML.";
                return false;
            }

            project = result;
            return true;
        }

        /// <summary>Однострочное описание проекта для лога.</summary>
        public string Describe()
        {
            return AssemblyName + " (" + TargetFramework + "), проект " + ProjectPath;
        }

        private static string FindDefaultWorkflow(string directory, out string warning)
        {
            warning = null;

            string main = Path.Combine(directory, RobotProjectLayout.DefaultWorkflowFileName);

            if (File.Exists(main))
            {
                return main;
            }

            string[] candidates = Directory
                .GetFiles(directory, "*.xaml", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
            {
                return null;
            }

            // Точки входа в проекте нет — берём единственный XAML. Несколько XAML без Main.xaml
            // это уже неоднозначность, но угадывать нельзя: пусть автор назовёт точку входа Main.xaml.
            if (candidates.Length == 1)
            {
                warning = "файла " + RobotProjectLayout.DefaultWorkflowFileName
                    + " нет, точкой входа взят единственный XAML: " + Path.GetFileName(candidates[0]);
                return candidates[0];
            }

            warning = "файла " + RobotProjectLayout.DefaultWorkflowFileName
                + " нет, а XAML несколько — точка входа не определена, пакет собран без сценария";
            return null;
        }

        private static IEnumerable<string> FindWorkflowFiles(string directory)
        {
            var result = new List<string>();

            try
            {
                foreach (string file in Directory.GetFiles(directory, "*.xaml", SearchOption.AllDirectories))
                {
                    string relative = GetRelativePath(directory, file);

                    // obj и bin — не исходники: XAML оттуда попадал бы в пакет дважды и раздувал его.
                    if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.Add(file);
                }
            }
            catch (Exception)
            {
                // Каталог не читается — упаковка всё равно скажет об этом на сборе файлов.
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);

            return result;
        }

        /// <summary>Относительный путь без ведущих «.\» — нужен и для XAML, и для сборок.</summary>
        internal static string GetRelativePath(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);

            if (full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(fullRoot.Length);
            }

            return Path.GetFileName(full);
        }
    }
}
