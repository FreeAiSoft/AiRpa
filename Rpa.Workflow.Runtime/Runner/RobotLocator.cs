using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

using RPA.Orchestrator.Contracts.Packages;

namespace RPA.Runner
{
    /// <summary>
    /// Куда раннер будет прогонять: сценарий, сборка, тип и каталог зависимостей.
    /// <para>
    /// <see cref="Candidates"/> и <see cref="Notes"/> нужны не «для красоты»: если сборка не
    /// нашлась, пользователь обязан увидеть, <b>где</b> раннер искал (иначе «не собрано» —
    /// это гадание), а если нашлось несколько сборок — какую выбрали и почему.
    /// </para>
    /// </summary>
    public sealed class RobotLocation
    {
        internal RobotLocation()
        {
            Candidates = new List<string>();
            Notes = new List<string>();
        }

        /// <summary>Способ указания робота: <c>project</c>, <c>workflow</c>, <c>assembly</c> или <c>package</c>.</summary>
        public string SourceKind { get; internal set; }

        /// <summary>Проект робота (при <c>--project</c>).</summary>
        public string ProjectPath { get; internal set; }

        /// <summary>Собранная сборка робота; <c>null</c> при прогоне из XAML.</summary>
        public string AssemblyPath { get; internal set; }

        /// <summary>XAML-файл сценария; <c>null</c>, если сценарий берётся из сборки.</summary>
        public string WorkflowPath { get; internal set; }

        /// <summary>Полное имя типа активности в сборке.</summary>
        public string TypeName { get; internal set; }

        /// <summary>Каталог, из которого раннер грузит зависимости робота.</summary>
        public string BinDirectory { get; internal set; }

        /// <summary>Путь к пакету робота (при <c>--package</c>).</summary>
        public string PackagePath { get; internal set; }

        /// <summary>Идентификатор робота из манифеста пакета.</summary>
        public string PackageId { get; internal set; }

        /// <summary>Версия пакета из манифеста.</summary>
        public string PackageVersion { get; internal set; }

        /// <summary>Хэш содержимого пакета из манифеста — по нему опознаётся версия байтов.</summary>
        public string PackageContentHash { get; internal set; }

        /// <summary>Каталоги, в которых искали сборку (в порядке поиска).</summary>
        public IList<string> Candidates { get; }

        /// <summary>Пояснения: какой каталог выбран, что ещё нашлось, чего нет.</summary>
        public IList<string> Notes { get; }

        /// <summary>Однострочное описание найденного робота — для лога и отчёта.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("источник: ").Append(SourceKind);

            if (!string.IsNullOrWhiteSpace(PackagePath))
            {
                text.Append(", пакет: ").Append(PackagePath);
                text.Append(' ').Append(PackageVersion);

                if (!string.IsNullOrWhiteSpace(PackageContentHash))
                {
                    text.Append(" (хэш ").Append(PackageContentHash).Append(')');
                }
            }

            if (!string.IsNullOrWhiteSpace(ProjectPath))
            {
                text.Append(", проект: ").Append(ProjectPath);
            }

            if (!string.IsNullOrWhiteSpace(AssemblyPath))
            {
                text.Append(", сборка: ").Append(AssemblyPath);
            }

            if (!string.IsNullOrWhiteSpace(WorkflowPath))
            {
                text.Append(", сценарий: ").Append(WorkflowPath);
            }

            if (!string.IsNullOrWhiteSpace(TypeName))
            {
                text.Append(", тип: ").Append(TypeName);
            }

            if (!string.IsNullOrWhiteSpace(BinDirectory))
            {
                text.Append(", зависимости: ").Append(BinDirectory);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Поиск того, что нужно прогнать (`OR-02`, шаг `W3`; пакет — `OR-03`).
    /// <para>
    /// Раннер не собирает робота: он находит уже собранное. Правила разрешения `--project`:
    /// <list type="number">
    /// <item>имя сборки — <c>AssemblyName</c> из <c>csproj</c> (иначе <c>RootNamespace</c>, иначе имя файла);</item>
    /// <item>имя типа — <c>x:Class</c> из <c>Main.xaml</c> (или <c>--type</c>);</item>
    /// <item>каталоги-кандидаты — <c>bin\&lt;Config&gt;</c>, <c>bin\&lt;Config&gt;\&lt;TFM&gt;</c>,
    /// <c>bin\&lt;Platform&gt;\&lt;Config&gt;</c>, <c>bin\&lt;Platform&gt;\&lt;Config&gt;\&lt;TFM&gt;</c>
    /// (общий список — <see cref="RobotProjectLayout"/>, чтобы упаковщик и раннер искали одинаково);</item>
    /// <item>если подходящих каталогов несколько, берётся самый свежий по времени записи сборки,
    /// а все кандидаты перечисляются в <see cref="RobotLocation.Candidates"/> — без молчаливой «угадайки»;</item>
    /// <item>сборки нет ни в одном кандидате — это <see cref="RunnerExitCode.RobotNotFound"/> (6)
    /// с перечнем проверенных каталогов и подсказкой «соберите проект в студии».</item>
    /// </list>
    /// </para>
    /// <para>
    /// Пакет (<c>--package</c>) — третий источник: он проверяется по манифесту и распаковывается
    /// в локальный кэш. «Пакета нет» — это код 6, а «пакет есть, но битый/подменённый» — код 5:
    /// оркестратор по этой разнице решает, публиковать пакет заново или чинить.
    /// </para>
    /// </summary>
    public static class RobotLocator
    {
        /// <summary>Разрешает источник прогона; код отказа считается кодом 6, если не сказано иное.</summary>
        public static bool TryResolve(RunnerOptions options, out RobotLocation location, out string error)
        {
            return TryResolve(options, out location, out error, out _);
        }

        /// <summary>
        /// Разрешает источник прогона и сообщает код отказа: <c>RobotNotFound</c> (6) — робота нет,
        /// <c>LoadFailed</c> (5) — робот есть, но непригоден (битый пакет, нет сборки в пакете).
        /// </summary>
        public static bool TryResolve(
            RunnerOptions options,
            out RobotLocation location,
            out string error,
            out RunnerExitCode failureCode)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            failureCode = RunnerExitCode.RobotNotFound;

            if (!string.IsNullOrWhiteSpace(options.PackagePath))
            {
                return TryResolvePackage(options, out location, out error, out failureCode);
            }

            if (!string.IsNullOrWhiteSpace(options.ProjectPath))
            {
                return TryResolveProject(options, out location, out error);
            }

            if (!string.IsNullOrWhiteSpace(options.AssemblyPath))
            {
                return TryResolveAssembly(options, out location, out error);
            }

            if (!string.IsNullOrWhiteSpace(options.WorkflowPath))
            {
                return TryResolveWorkflow(options, out location, out error);
            }

            location = null;
            error = "не указан источник прогона: --project, --workflow, --assembly или --package";

            return false;
        }

        private static bool TryResolvePackage(
            RunnerOptions options,
            out RobotLocation location,
            out string error,
            out RunnerExitCode failureCode)
        {
            location = null;
            error = null;
            failureCode = RunnerExitCode.RobotNotFound;

            string packagePath = Path.GetFullPath(options.PackagePath);

            if (!File.Exists(packagePath))
            {
                error = "пакет робота не найден: " + packagePath + Environment.NewLine
                    + "Соберите пакет (RpaPack --project <csproj>) или укажите --project / --workflow / --assembly.";
                return false;
            }

            if (!RobotPackageReader.TryOpen(packagePath, out RobotPackage package, out string openError))
            {
                // Пакет есть, но не читается, не проходит правила или не совпал с хэшами:
                // это «робот сломан» (5), а не «робота нет» (6) — публиковать заново не поможет.
                failureCode = RunnerExitCode.LoadFailed;
                error = openError;
                return false;
            }

            string root = string.IsNullOrWhiteSpace(options.PackageCacheRoot)
                ? RobotPackageCache.DefaultRoot
                : options.PackageCacheRoot;

            if (!RobotPackageCache.TryEnsureExtracted(root, package, out string extracted, out string extractError))
            {
                failureCode = RunnerExitCode.LoadFailed;
                error = extractError;
                return false;
            }

            string assemblyPath = Path.Combine(
                extracted,
                RobotPackageFormat.NormalizeEntryPath(package.Manifest.EntryAssembly)
                    .Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(assemblyPath))
            {
                failureCode = RunnerExitCode.LoadFailed;
                error = "в распакованном пакете нет сборки «" + package.Manifest.EntryAssembly + "»: " + assemblyPath;
                return false;
            }

            string workflow = null;

            if (!string.IsNullOrWhiteSpace(package.Manifest.EntryWorkflow))
            {
                string candidate = Path.Combine(
                    extracted,
                    RobotPackageFormat.NormalizeEntryPath(package.Manifest.EntryWorkflow)
                        .Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(candidate))
                {
                    workflow = candidate;
                }
            }

            location = new RobotLocation
            {
                SourceKind = "package",
                PackagePath = packagePath,
                PackageId = package.Id,
                PackageVersion = package.Version,
                PackageContentHash = package.Manifest.ContentHash,
                AssemblyPath = assemblyPath,
                WorkflowPath = workflow,
                TypeName = package.EntryType,
                BinDirectory = Path.GetDirectoryName(assemblyPath)
            };

            location.Notes.Add("пакет: " + package.Describe());
            location.Notes.Add("распакован в кэш: " + extracted);

            return true;
        }

        private static bool TryResolveWorkflow(RunnerOptions options, out RobotLocation location, out string error)
        {
            location = null;
            error = null;

            string workflow = Path.GetFullPath(options.WorkflowPath);

            if (!File.Exists(workflow))
            {
                error = "XAML-файл сценария не найден: " + workflow;
                return false;
            }

            location = new RobotLocation
            {
                SourceKind = "workflow",
                WorkflowPath = workflow,
                BinDirectory = string.IsNullOrWhiteSpace(options.BinDirectory)
                    ? Path.GetDirectoryName(workflow)
                    : Path.GetFullPath(options.BinDirectory)
            };

            location.Notes.Add("прогон из XAML: сборка проекта не нужна");

            return true;
        }

        private static bool TryResolveAssembly(RunnerOptions options, out RobotLocation location, out string error)
        {
            location = null;
            error = null;

            string assembly = Path.GetFullPath(options.AssemblyPath);

            if (!File.Exists(assembly))
            {
                error = "сборка робота не найдена: " + assembly;
                return false;
            }

            if (string.IsNullOrWhiteSpace(options.TypeName))
            {
                error = "для --assembly обязателен --type: имя активности в сборке";
                return false;
            }

            location = new RobotLocation
            {
                SourceKind = "assembly",
                AssemblyPath = assembly,
                TypeName = options.TypeName.Trim(),
                WorkflowPath = string.IsNullOrWhiteSpace(options.WorkflowPath) ? null : Path.GetFullPath(options.WorkflowPath),
                BinDirectory = string.IsNullOrWhiteSpace(options.BinDirectory)
                    ? Path.GetDirectoryName(assembly)
                    : Path.GetFullPath(options.BinDirectory)
            };

            return true;
        }

        private static bool TryResolveProject(RunnerOptions options, out RobotLocation location, out string error)
        {
            location = null;
            error = null;

            string project = Path.GetFullPath(options.ProjectPath);

            if (!File.Exists(project))
            {
                error = "проект робота не найден: " + project;
                return false;
            }

            string projectDirectory = Path.GetDirectoryName(project) ?? ".";
            XDocument document = LoadProject(project, out string projectError);

            if (document == null)
            {
                error = "не удалось прочитать проект робота: " + project + Environment.NewLine + projectError;
                return false;
            }

            string assemblyName = ReadAssemblyName(document, project);

            var notes = new List<string>();
            List<string> targetFrameworks = ReadTargetFrameworks(document);

            string workflow = string.IsNullOrWhiteSpace(options.WorkflowPath)
                ? Path.Combine(projectDirectory, RunnerOptions.DefaultWorkflowFileName)
                : Path.GetFullPath(options.WorkflowPath);

            if (!File.Exists(workflow))
            {
                notes.Add("XAML сценария не найден: " + workflow + " — тип активности берём из --type");
                workflow = null;
            }

            string typeName = options.TypeName;

            if (string.IsNullOrWhiteSpace(typeName) && workflow != null)
            {
                typeName = ReadXamlClass(workflow);
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                error = "не удалось определить тип активности робота: укажите --type"
                    + Environment.NewLine
                    + (workflow == null
                        ? "XAML сценария не найден, а без него x:Class взять неоткуда"
                        : "в файле «" + workflow + "» нет атрибута x:Class");
                return false;
            }

            List<string> candidates = BuildCandidateDirectories(projectDirectory, targetFrameworks);
            var found = new List<string>();

            foreach (string directory in candidates)
            {
                string dll = Path.Combine(directory, assemblyName + ".dll");

                if (File.Exists(dll))
                {
                    found.Add(dll);
                }
            }

            if (found.Count == 0)
            {
                var text = new StringBuilder();
                text.Append("робот не собран: сборка «").Append(assemblyName).Append(".dll» не найдена").AppendLine();
                text.Append("искали в каталогах:");
                foreach (string directory in candidates)
                {
                    text.AppendLine().Append("  ").Append(directory);
                }

                text.AppendLine().Append(
                    "Соберите проект в студии (или MSBuild) и повторите прогон, "
                    + "либо укажите --assembly + --type + --bin для уже собранного робота.");

                error = text.ToString();
                return false;
            }

            string chosen = found
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .First();

            if (found.Count > 1)
            {
                notes.Add("подходящих сборок " + found.Count.ToString(CultureInfo.InvariantCulture)
                    + ", выбрана самая свежая: " + chosen);
            }

            location = new RobotLocation
            {
                SourceKind = "project",
                ProjectPath = project,
                AssemblyPath = chosen,
                WorkflowPath = workflow,
                TypeName = typeName.Trim(),
                BinDirectory = string.IsNullOrWhiteSpace(options.BinDirectory)
                    ? Path.GetDirectoryName(chosen)
                    : Path.GetFullPath(options.BinDirectory)
            };

            foreach (string directory in candidates)
            {
                location.Candidates.Add(directory);
            }

            foreach (string note in notes)
            {
                location.Notes.Add(note);
            }

            return true;
        }

        /// <summary>
        /// Каталоги <c>bin\…</c>, в которых студия и MSBuild кладут сборку робота. Список общий
        /// с упаковщиком (<see cref="RobotProjectLayout.CandidateDirectories"/>): иначе пакет
        /// собирался бы из одного каталога, а прогон искался в другом.
        /// </summary>
        internal static List<string> BuildCandidateDirectories(string projectDirectory, IList<string> targetFrameworks)
        {
            return RobotProjectLayout.CandidateDirectories(projectDirectory, targetFrameworks);
        }

        private static XDocument LoadProject(string project, out string error)
        {
            return RobotProjectLayout.LoadProject(project, out error);
        }

        /// <summary>Значение свойства MSBuild из <c>csproj</c> (последнее объявление выигрывает).</summary>
        internal static string ReadProperty(XDocument document, string name)
        {
            return RobotProjectLayout.ReadProperty(document, name);
        }

        /// <summary>Имя сборки робота: <c>AssemblyName</c>, иначе <c>RootNamespace</c>, иначе имя файла проекта.</summary>
        internal static string ReadAssemblyName(XDocument document, string projectPath)
        {
            return RobotProjectLayout.ReadAssemblyName(document, projectPath);
        }

        /// <summary>Целевые платформы проекта в нормализованном виде (<c>v4.7.2</c> → <c>net472</c>).</summary>
        internal static List<string> ReadTargetFrameworks(XDocument document)
        {
            return RobotProjectLayout.ReadTargetFrameworks(document);
        }

        /// <summary><c>v4.7.2</c> → <c>net472</c>; <c>net472</c> остаётся как есть.</summary>
        internal static string NormalizeFramework(string value)
        {
            return RobotProjectLayout.NormalizeFramework(value);
        }

        /// <summary>Атрибут <c>x:Class</c> корневого элемента XAML — имя типа активности.</summary>
        internal static string ReadXamlClass(string xamlPath)
        {
            return RobotProjectLayout.ReadEntryType(xamlPath);
        }
    }
}
