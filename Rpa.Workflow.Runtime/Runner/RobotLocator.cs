using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

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

        /// <summary>Способ указания робота: <c>project</c>, <c>workflow</c> или <c>assembly</c>.</summary>
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

        /// <summary>Каталоги, в которых искали сборку (в порядке поиска).</summary>
        public IList<string> Candidates { get; }

        /// <summary>Пояснения: какой каталог выбран, что ещё нашлось, чего нет.</summary>
        public IList<string> Notes { get; }

        /// <summary>Однострочное описание найденного робота — для лога и отчёта.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("источник: ").Append(SourceKind);

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
    /// Поиск того, что нужно прогнать (`OR-02`, шаг `W3`).
    /// <para>
    /// Раннер не собирает робота: он находит уже собранное. Правила разрешения `--project`:
    /// <list type="number">
    /// <item>имя сборки — <c>AssemblyName</c> из <c>csproj</c> (иначе <c>RootNamespace</c>, иначе имя файла);</item>
    /// <item>имя типа — <c>x:Class</c> из <c>Main.xaml</c> (или <c>--type</c>);</item>
    /// <item>каталоги-кандидаты — <c>bin\&lt;Config&gt;</c>, <c>bin\&lt;Config&gt;\&lt;TFM&gt;</c>,
    /// <c>bin\&lt;Platform&gt;\&lt;Config&gt;</c>, <c>bin\&lt;Platform&gt;\&lt;Config&gt;\&lt;TFM&gt;</c>:
    /// студия собирает в <c>bin\x64\Debug</c>, SDK-style роботы — в <c>bin\Debug\net472</c>,
    /// legacy — в <c>bin\Debug</c>;</item>
    /// <item>если подходящих каталогов несколько, берётся самый свежий по времени записи сборки,
    /// а все кандидаты перечисляются в <see cref="RobotLocation.Candidates"/> — без молчаливой «угадайки»;</item>
    /// <item>сборки нет ни в одном кандидате — это <see cref="RunnerExitCode.RobotNotFound"/> (6)
    /// с перечнем проверенных каталогов и подсказкой «соберите проект в студии».</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class RobotLocator
    {
        /// <summary>Конфигурации сборки в порядке предпочтения.</summary>
        private static readonly string[] Configurations = { "Debug", "Release" };

        /// <summary>Платформы сборки в порядке предпочтения (пустая — <c>bin\&lt;Config&gt;</c>).</summary>
        private static readonly string[] Platforms = { string.Empty, "x64", "x86" };

        /// <summary>
        /// Разрешает источник прогона. <c>false</c> — робота нет: текст ошибки уже содержит
        /// причину и подсказку, вызывающая сторона возвращает <see cref="RunnerExitCode.RobotNotFound"/>.
        /// </summary>
        public static bool TryResolve(RunnerOptions options, out RobotLocation location, out string error)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
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
            error = "не указан источник прогона: --project, --workflow или --assembly";

            return false;
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

            string assemblyName = ReadProperty(document, "AssemblyName")
                ?? ReadProperty(document, "RootNamespace")
                ?? Path.GetFileNameWithoutExtension(project);

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

        /// <summary>Каталоги <c>bin\...</c>, в которых студия и MSBuild кладут сборку робота.</summary>
        internal static List<string> BuildCandidateDirectories(string projectDirectory, IList<string> targetFrameworks)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var frameworks = new List<string> { string.Empty };

            if (targetFrameworks != null)
            {
                foreach (string framework in targetFrameworks)
                {
                    if (!string.IsNullOrWhiteSpace(framework) && !frameworks.Contains(framework, StringComparer.OrdinalIgnoreCase))
                    {
                        frameworks.Add(framework);
                    }
                }
            }

            foreach (string configuration in Configurations)
            {
                foreach (string platform in Platforms)
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
            }

            return result;
        }

        private static XDocument LoadProject(string project, out string error)
        {
            error = null;

            try
            {
                return XDocument.Load(project);
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return null;
            }
        }

        /// <summary>Значение свойства MSBuild из <c>csproj</c> (последнее объявление выигрывает).</summary>
        internal static string ReadProperty(XDocument document, string name)
        {
            if (document?.Root == null)
            {
                return null;
            }

            string value = document
                .Descendants()
                .Where(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value)
                .LastOrDefault(v => !string.IsNullOrWhiteSpace(v));

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>Целевые платформы проекта в нормализованном виде (<c>v4.7.2</c> → <c>net472</c>).</summary>
        internal static List<string> ReadTargetFrameworks(XDocument document)
        {
            var result = new List<string>();

            IEnumerable<string> raw = document
                .Descendants()
                .Where(e => e.Name.LocalName == "TargetFramework" || e.Name.LocalName == "TargetFrameworks")
                .Select(e => e.Value);

            foreach (string value in raw)
            {
                foreach (string part in (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string normalized = NormalizeFramework(part.Trim());

                    if (!string.IsNullOrWhiteSpace(normalized) && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(normalized);
                    }
                }
            }

            string legacy = document
                .Descendants()
                .Where(e => e.Name.LocalName == "TargetFrameworkVersion")
                .Select(e => e.Value)
                .LastOrDefault(v => !string.IsNullOrWhiteSpace(v));

            if (!string.IsNullOrWhiteSpace(legacy))
            {
                string normalized = NormalizeFramework(legacy.Trim());

                if (!string.IsNullOrWhiteSpace(normalized) && !result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        /// <summary><c>v4.7.2</c> → <c>net472</c>; <c>net472</c> остаётся как есть.</summary>
        internal static string NormalizeFramework(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string text = value.Trim();

            if (text.StartsWith("net", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                return text.ToLowerInvariant();
            }

            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                && Version.TryParse(text.Substring(1), out Version version))
            {
                var framework = new StringBuilder("net");
                framework.Append(version.Major.ToString(CultureInfo.InvariantCulture));
                framework.Append(version.Minor.ToString(CultureInfo.InvariantCulture));

                // v4.8 — это два числа, а не три: у Version третий компонент равен -1,
                // и без проверки получался каталог «net48-1», которого не бывает.
                if (version.Build >= 0)
                {
                    framework.Append(version.Build.ToString(CultureInfo.InvariantCulture));
                }

                return framework.ToString();
            }

            return null;
        }

        /// <summary>Атрибут <c>x:Class</c> корневого элемента XAML — имя типа активности.</summary>
        internal static string ReadXamlClass(string xamlPath)
        {
            try
            {
                XDocument xaml = XDocument.Load(xamlPath);
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

                return xaml.Root?.Attribute(x + "Class")?.Value?.Trim();
            }
            catch (Exception)
            {
                // Битый XAML — не беда локатора: тип просто не определён, а ошибка загрузки
                // будет показана на шаге прогона с полным стеком.
                return null;
            }
        }
    }
}
