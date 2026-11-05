using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using RPA.Orchestrator.Contracts.Packages;

namespace RPA.Packaging
{
    /// <summary>Чем закончилась упаковка (`OR-03`).</summary>
    public enum RobotPackStatus
    {
        /// <summary>Пакет собран и записан.</summary>
        Created = 0,

        /// <summary>Пакет этой версии уже есть и содержимое совпало — переиспользован.</summary>
        Reused = 1,

        /// <summary>Сборка проекта не прошла.</summary>
        BuildFailed = 2,

        /// <summary>Проект собран не был (нет сборки в <c>bin</c>), а сборку не просили.</summary>
        NotBuilt = 3,

        /// <summary>Из проекта нельзя получить робота (нет проекта, XAML или точки входа).</summary>
        ProjectInvalid = 4,

        /// <summary>Собранный манифест не прошёл правила формата.</summary>
        PackageInvalid = 5,

        /// <summary>Версия уже опубликована с другим содержимым.</summary>
        VersionConflict = 6,

        /// <summary>Ошибка ввода-вывода при записи пакета.</summary>
        IoFailed = 7
    }

    /// <summary>Запрос на упаковку робота.</summary>
    public sealed class RobotPackageRequest
    {
        /// <summary>Путь к <c>*.csproj</c> робота (обязателен).</summary>
        public string ProjectPath { get; set; }

        /// <summary>Конфигурация сборки; по умолчанию <c>Release</c> — пакет для эксплуатации.</summary>
        public string Configuration { get; set; } = "Release";

        /// <summary>Версия пакета; по умолчанию берётся из проекта.</summary>
        public string Version { get; set; }

        /// <summary>Имя робота для человека; по умолчанию — идентификатор.</summary>
        public string Name { get; set; }

        /// <summary>Куда положить пакет; по умолчанию — в локальный кэш.</summary>
        public string OutputPath { get; set; }

        /// <summary>Корень локального кэша пакетов; по умолчанию <see cref="RobotPackageCache.DefaultRoot"/>.</summary>
        public string CacheRoot { get; set; }

        /// <summary>Собирать ли проект перед упаковкой (по умолчанию — да).</summary>
        public bool Build { get; set; } = true;

        /// <summary>Куда писать ход упаковки.</summary>
        public Action<string> Log { get; set; }
    }

    /// <summary>Итог упаковки: статус, путь, манифест и причина отказа.</summary>
    public sealed class RobotPackageResult
    {
        /// <summary>Чем закончилось.</summary>
        public RobotPackStatus Status { get; internal set; }

        /// <summary>Путь к пакету (если он есть).</summary>
        public string PackagePath { get; internal set; }

        /// <summary>Манифест пакета (если он есть).</summary>
        public RobotPackageManifest Manifest { get; internal set; }

        /// <summary>sha256 файла пакета.</summary>
        public string PackageSha256 { get; internal set; }

        /// <summary>Причина отказа (для успешных статусов — пусто).</summary>
        public string Error { get; internal set; } = string.Empty;

        /// <summary>Строки журнала упаковки в порядке появления.</summary>
        public List<string> Log { get; } = new List<string>();

        /// <summary>Пакет готов к публикации.</summary>
        public bool IsSuccess
        {
            get { return Status == RobotPackStatus.Created || Status == RobotPackStatus.Reused; }
        }

        /// <summary>Однострочный итог.</summary>
        public string Summary
        {
            get
            {
                if (!IsSuccess)
                {
                    return "упаковка не удалась (" + Status + "): " + Error;
                }

                string manifestText = Manifest == null ? string.Empty : Manifest.Describe();

                return "пакет " + manifestText + ": " + PackagePath
                    + " (хэш " + (Manifest?.ContentHash ?? string.Empty) + ")";
            }
        }
    }

    /// <summary>
    /// Упаковка робота в <c>robot.zip</c> с манифестом (`OR-03`): сборка проекта → сбор файлов →
    /// пофайловые хэши → манифест → архив → неизменяемое размещение в кэше.
    /// <para>
    /// Три правила, ради которых всё и делается:
    /// <list type="number">
    /// <item><b>пакет самодостаточен</b> — сборка робота, все зависимости из её каталога и XAML
    /// идут внутрь архива; на машине запуска не нужен ни проект, ни студия;</item>
    /// <item><b>пакет неизменяем</b> — версия, однажды упакованная, не перезаписывается другим
    /// содержимым: раннер и оркестратор должны знать, что «1.0.0» — это ровно те байты;</item>
    /// <item><b>пакет воспроизводим</b> — повторная упаковка того же бинарника даёт тот же
    /// <c>contentHash</c>, поэтому «это та же версия?» решается сравнением, а не глазами.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class RobotPackager
    {
        /// <summary>Имя инструмента упаковки (попадает в манифест).</summary>
        public const string ToolName = "RpaPack";

        /// <summary>Расширения файлов, которые кладутся в <c>lib/</c> рядом со сборкой робота.</summary>
        private static readonly string[] LibraryExtensions = { ".dll", ".exe", ".config", ".pdb" };

        private readonly IRobotProjectBuilder _builder;

        /// <summary>Упаковщик с реальной сборкой через MSBuild.</summary>
        public RobotPackager()
            : this(new MsBuildRobotProjectBuilder())
        {
        }

        /// <summary>Упаковщик с подставной сборкой (тесты и упаковка готового бинарника).</summary>
        public RobotPackager(IRobotProjectBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        /// <summary>Собирает пакет. Исключения не бросает: любой исход — <see cref="RobotPackageResult"/>.</summary>
        public RobotPackageResult Pack(RobotPackageRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var result = new RobotPackageResult();
            Action<string> log = line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    result.Log.Add(line);
                    request.Log?.Invoke(line);
                }
            };

            try
            {
                if (!RobotProject.TryLoad(request.ProjectPath, out RobotProject project, out string projectError))
                {
                    return Fail(result, RobotPackStatus.ProjectInvalid, projectError);
                }

                log("проект: " + project.Describe());

                foreach (string warning in project.Warnings)
                {
                    log("замечание: " + warning);
                }

                string configuration = RobotBuildOutput.NormalizeConfiguration(request.Configuration);
                RobotProjectBuildResult build = null;

                if (request.Build)
                {
                    build = _builder.Build(project, configuration, log);
                    log(build.Describe());

                    if (!build.Success)
                    {
                        string detail = build.Errors.Count == 0
                            ? "MSBuild завершился кодом " + build.ExitCode
                            : string.Join(Environment.NewLine, build.Errors);

                        return Fail(result, RobotPackStatus.BuildFailed, "сборка робота не прошла:" + Environment.NewLine + detail);
                    }
                }
                else
                {
                    log("сборка отключена: упаковывается уже собранный бинарник");
                }

                if (!RobotBuildOutput.TryFind(project, configuration, out RobotBuildOutput output, out string buildOutputError))
                {
                    return Fail(result, RobotPackStatus.NotBuilt, buildOutputError);
                }

                log("сборка робота: " + output.AssemblyPath);

                foreach (string note in output.Notes)
                {
                    log("замечание: " + note);
                }

                if (!TryCollectFiles(project, output, out List<KeyValuePair<string, string>> files, out List<RobotPackageFile> manifestFiles, out string collectError))
                {
                    return Fail(result, RobotPackStatus.ProjectInvalid, collectError);
                }

                RobotPackageManifest manifest = BuildManifest(request, project, output, build, files, manifestFiles);

                foreach (string note in output.Notes)
                {
                    manifest.Warnings.Add(note);
                }

                RobotPackageValidationResult validation = RobotPackageValidator.Validate(manifest);

                if (!validation.IsValid)
                {
                    return Fail(
                        result,
                        RobotPackStatus.PackageInvalid,
                        "собранный манифест не прошёл проверку:" + validation.Describe());
                }

                foreach (RobotPackageIssue issue in validation.Issues.Where(i => i.Severity == RobotPackageIssueSeverity.Warning))
                {
                    manifest.Warnings.Add(issue.Message);
                }

                string destination = ResolveDestination(request, manifest);
                log("пакет: " + destination);

                if (File.Exists(destination))
                {
                    // Существующий пакет читаем как пакет, а не как текст: сверяется хэш содержимого
                    // из манифеста, и заодно проверяется, что лежащие байты ему соответствуют.
                    if (!RobotPackageReader.TryOpen(destination, out RobotPackage existing, out string existingError))
                    {
                        return Fail(
                            result,
                            RobotPackStatus.VersionConflict,
                            "пакет «" + destination + "» уже есть, но не проходит проверку (" + existingError
                            + "): перезаписать его нельзя — поднимите версию или удалите файл");
                    }

                    if (string.Equals(existing.Manifest.ContentHash, manifest.ContentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Status = RobotPackStatus.Reused;
                        result.PackagePath = destination;
                        result.Manifest = existing.Manifest;
                        result.PackageSha256 = existing.PackageSha256;
                        log("пакет этой версии уже есть и содержимое совпало — переиспользован");

                        return result;
                    }

                    return Fail(
                        result,
                        RobotPackStatus.VersionConflict,
                        "версия «" + manifest.Version + "» робота «" + manifest.Id
                        + "» уже упакована с другим содержимым (хэш " + existing.Manifest.ContentHash
                        + " вместо " + manifest.ContentHash + "): пакет неизменяем, поднимите версию"
                        + Environment.NewLine + destination);
                }

                if (!RobotPackageWriter.TryWrite(manifest, files, destination, out string writeError))
                {
                    return Fail(result, RobotPackStatus.IoFailed, writeError);
                }

                result.Status = RobotPackStatus.Created;
                result.PackagePath = destination;
                result.Manifest = manifest;
                result.PackageSha256 = RobotPackageHash.OfFile(destination);

                log("пакет записан: файлов " + manifest.Files.Count.ToString(CultureInfo.InvariantCulture)
                    + ", хэш содержимого " + manifest.ContentHash);

                return result;
            }
            catch (Exception ex)
            {
                return Fail(result, RobotPackStatus.IoFailed, ex.Message);
            }
        }

        private static RobotPackageResult Fail(RobotPackageResult result, RobotPackStatus status, string error)
        {
            result.Status = status;
            result.Error = error ?? string.Empty;

            return result;
        }

        private static bool TryCollectFiles(
            RobotProject project,
            RobotBuildOutput output,
            out List<KeyValuePair<string, string>> files,
            out List<RobotPackageFile> manifestFiles,
            out string error)
        {
            files = new List<KeyValuePair<string, string>>();
            manifestFiles = new List<RobotPackageFile>();
            error = null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string entryAssembly = RobotPackageFormat.LibraryEntry(project.AssemblyName + ".dll");

            if (!File.Exists(output.AssemblyPath))
            {
                error = "сборка робота исчезла до упаковки: " + output.AssemblyPath;
                return false;
            }

            foreach (string file in Directory
                .GetFiles(output.DirectoryPath, "*", SearchOption.AllDirectories)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();

                if (!LibraryExtensions.Contains(extension))
                {
                    continue;
                }

                string relative = RobotProject.GetRelativePath(output.DirectoryPath, file);
                string entryPath = RobotPackageFormat.LibraryEntry(relative.Replace('\\', '/'));

                if (!seen.Add(entryPath))
                {
                    continue;
                }

                files.Add(new KeyValuePair<string, string>(entryPath, file));
                manifestFiles.Add(new RobotPackageFile
                {
                    Path = entryPath,
                    Size = new FileInfo(file).Length,
                    Sha256 = RobotPackageHash.OfFile(file)
                });
            }

            if (!seen.Contains(entryAssembly))
            {
                error = "в каталоге сборки «" + output.DirectoryPath + "» нет «" + project.AssemblyName
                    + ".dll»: упаковывать нечего";
                return false;
            }

            foreach (string xaml in project.WorkflowFiles)
            {
                string relative = RobotProject.GetRelativePath(project.ProjectDirectory, xaml).Replace('\\', '/');
                string entryPath = RobotPackageFormat.WorkflowEntry(relative);

                if (!seen.Add(entryPath))
                {
                    continue;
                }

                files.Add(new KeyValuePair<string, string>(entryPath, xaml));
                manifestFiles.Add(new RobotPackageFile
                {
                    Path = entryPath,
                    Size = new FileInfo(xaml).Length,
                    Sha256 = RobotPackageHash.OfFile(xaml)
                });
            }

            manifestFiles.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

            return true;
        }

        private static RobotPackageManifest BuildManifest(
            RobotPackageRequest request,
            RobotProject project,
            RobotBuildOutput output,
            RobotProjectBuildResult build,
            List<KeyValuePair<string, string>> files,
            List<RobotPackageFile> manifestFiles)
        {
            string version = string.IsNullOrWhiteSpace(request.Version) ? project.Version : request.Version.Trim();
            string entryAssembly = RobotPackageFormat.LibraryEntry(project.AssemblyName + ".dll");

            string entryWorkflow = null;

            if (project.WorkflowPath != null)
            {
                string relative = RobotProject.GetRelativePath(project.ProjectDirectory, project.WorkflowPath).Replace('\\', '/');
                entryWorkflow = RobotPackageFormat.WorkflowEntry(relative);
            }

            var manifest = new RobotPackageManifest
            {
                FormatVersion = RobotPackageFormat.CurrentVersion,
                Id = project.AssemblyName,
                Name = string.IsNullOrWhiteSpace(request.Name) ? project.AssemblyName : request.Name.Trim(),
                Version = version,
                TargetFramework = project.TargetFramework,
                EntryType = project.EntryType,
                EntryAssembly = entryAssembly,
                EntryWorkflow = entryWorkflow,
                CreatedAt = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                Builder = new RobotPackageBuilderInfo
                {
                    Tool = ToolName,
                    Version = typeof(RobotPackager).Assembly.GetName().Version?.ToString() ?? "1.0.0.0",
                    MsBuildVersion = build?.MsBuildVersion
                },
                Source = new RobotPackageSourceInfo
                {
                    Project = Path.GetFileName(project.ProjectPath),
                    Configuration = RobotBuildOutput.NormalizeConfiguration(request.Configuration),
                    BinDirectory = output.DirectoryPath
                },
                Arguments = new List<RobotPackageArgument>(project.Arguments),
                Dependencies = BuildDependencies(manifestFiles, files, entryAssembly),
                Files = manifestFiles,
                Warnings = new List<string>(project.Warnings)
            };

            manifest.ContentHash = RobotPackageHash.OfContent(manifestFiles);

            return manifest;
        }

        /// <summary>Зависимости пакета: всё из <c>lib/</c>, кроме самой сборки робота.</summary>
        private static List<RobotPackageDependency> BuildDependencies(
            List<RobotPackageFile> manifestFiles,
            IEnumerable<KeyValuePair<string, string>> files,
            string entryAssembly)
        {
            var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> pair in files)
            {
                sources[RobotPackageFormat.NormalizeEntryPath(pair.Key)] = pair.Value;
            }

            var result = new List<RobotPackageDependency>();

            foreach (RobotPackageFile file in manifestFiles)
            {
                // Зависимость — это сборка, а не любой файл из lib: pdb и .config лежат рядом,
                // но версии в метаданных у них нет, и в списке зависимостей они только шумят.
                bool isAssembly = file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || file.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                if (!isAssembly
                    || !RobotPackageFormat.IsLibraryEntry(file.Path)
                    || string.Equals(file.Path, entryAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file.Path);
                sources.TryGetValue(RobotPackageFormat.NormalizeEntryPath(file.Path), out string source);

                result.Add(new RobotPackageDependency
                {
                    File = file.Path,
                    Name = name,
                    Version = TryReadAssemblyVersion(source)
                });
            }

            return result;
        }

        /// <summary>Версия сборки из метаданных файла; ошибка чтения — не повод не упаковать.</summary>
        private static string TryReadAssemblyVersion(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return AssemblyName.GetAssemblyName(path).Version?.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ResolveDestination(RobotPackageRequest request, RobotPackageManifest manifest)
        {
            if (!string.IsNullOrWhiteSpace(request.OutputPath))
            {
                return Path.GetFullPath(request.OutputPath);
            }

            string root = string.IsNullOrWhiteSpace(request.CacheRoot)
                ? RobotPackageCache.DefaultRoot
                : request.CacheRoot;

            return RobotPackageCache.PackageFilePath(root, manifest.Id, manifest.Version);
        }
    }
}
