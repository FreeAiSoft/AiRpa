using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>Важность замечания к пакету.</summary>
    public enum RobotPackageIssueSeverity
    {
        /// <summary>Замечание: пакет годен, но о нём стоит знать.</summary>
        Warning = 0,

        /// <summary>Ошибка: пакет непригоден, запускать его нельзя.</summary>
        Error = 1
    }

    /// <summary>Одно замечание к пакету: машиночитаемый код, текст и путь файла (если есть).</summary>
    public sealed class RobotPackageIssue
    {
        /// <summary>Код замечания (<c>id.invalid</c>, <c>hash.mismatch</c>…): по нему пишутся тесты.</summary>
        public string Code { get; set; }

        /// <summary>Текст для человека.</summary>
        public string Message { get; set; }

        /// <summary>Путь внутри пакета, к которому относится замечание.</summary>
        public string Path { get; set; }

        /// <summary>Важность.</summary>
        public RobotPackageIssueSeverity Severity { get; set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            string prefix = Severity == RobotPackageIssueSeverity.Error ? "ошибка" : "замечание";

            return string.IsNullOrWhiteSpace(Path)
                ? prefix + " " + Code + ": " + Message
                : prefix + " " + Code + " («" + Path + "»): " + Message;
        }
    }

    /// <summary>Итог проверки манифеста: список замечаний и признак «годен к запуску».</summary>
    public sealed class RobotPackageValidationResult
    {
        /// <summary>Пустой итог без замечаний (годен).</summary>
        public static RobotPackageValidationResult Valid { get; } = new RobotPackageValidationResult();

        /// <summary>Все замечания в порядке проверки.</summary>
        public List<RobotPackageIssue> Issues { get; } = new List<RobotPackageIssue>();

        /// <summary>Ошибок нет — пакет можно запускать.</summary>
        public bool IsValid
        {
            get
            {
                foreach (RobotPackageIssue issue in Issues)
                {
                    if (issue.Severity == RobotPackageIssueSeverity.Error)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Ошибки отдельным списком.</summary>
        public IEnumerable<RobotPackageIssue> Errors
        {
            get
            {
                foreach (RobotPackageIssue issue in Issues)
                {
                    if (issue.Severity == RobotPackageIssueSeverity.Error)
                    {
                        yield return issue;
                    }
                }
            }
        }

        /// <summary>Многострочный текст всех замечаний — то, что увидит пользователь.</summary>
        public string Describe()
        {
            if (Issues.Count == 0)
            {
                return "замечаний нет";
            }

            var text = new StringBuilder();

            foreach (RobotPackageIssue issue in Issues)
            {
                text.AppendLine().Append("  - ").Append(issue);
            }

            return text.ToString();
        }

        /// <summary>Добавляет ошибку.</summary>
        public void AddError(string code, string message, string path = null)
        {
            Issues.Add(new RobotPackageIssue
            {
                Code = code,
                Message = message,
                Path = path,
                Severity = RobotPackageIssueSeverity.Error
            });
        }

        /// <summary>Добавляет замечание.</summary>
        public void AddWarning(string code, string message, string path = null)
        {
            Issues.Add(new RobotPackageIssue
            {
                Code = code,
                Message = message,
                Path = path,
                Severity = RobotPackageIssueSeverity.Warning
            });
        }
    }

    /// <summary>
    /// Правила годности манифеста (`OR-03`).
    /// <para>
    /// Проверки намеренно строгие и «дешёвые»: пакет, который нельзя запустить без правок, должен
    /// отбраковываться при упаковке, а не на машине оператора в три часа ночи. Поэтому запрещены
    /// абсолютные пути и <c>..</c> (иначе распаковка пишет за пределы каталога), файлы вне
    /// <c>lib/</c> и <c>robot/</c> (иначе в пакет протаскивают что угодно), расхождение
    /// <c>contentHash</c> с перечнем файлов и точка входа, которой нет среди файлов.
    /// </para>
    /// </summary>
    public static class RobotPackageValidator
    {
        private static readonly Regex IdPattern = new Regex(
            @"^[A-Za-z0-9_][A-Za-z0-9_.\-]*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex VersionPattern = new Regex(
            @"^\d+(\.\d+){0,3}(-[0-9A-Za-z.\-]+)?(\+[0-9A-Za-z.\-]+)?$",
            RegexOptions.CultureInvariant);

        /// <summary>Проверяет манифест по всем правилам формата.</summary>
        public static RobotPackageValidationResult Validate(RobotPackageManifest manifest)
        {
            var result = new RobotPackageValidationResult();

            if (manifest == null)
            {
                result.AddError("manifest.missing", "манифест не передан");
                return result;
            }

            if (manifest.FormatVersion != RobotPackageFormat.CurrentVersion)
            {
                result.AddError(
                    "format.unsupported",
                    "версия формата пакета " + manifest.FormatVersion.ToString(CultureInfo.InvariantCulture)
                    + ", поддерживается " + RobotPackageFormat.CurrentVersion.ToString(CultureInfo.InvariantCulture));
            }

            ValidateIdentity(result, manifest);
            ValidateEntryPoint(result, manifest);
            ValidateFiles(result, manifest);

            return result;
        }

        private static void ValidateIdentity(RobotPackageValidationResult result, RobotPackageManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest.Id))
            {
                result.AddError("id.missing", "не указан идентификатор робота (id)");
            }
            else if (!IdPattern.IsMatch(manifest.Id))
            {
                result.AddError(
                    "id.invalid",
                    "идентификатор робота «" + manifest.Id + "» содержит недопустимые символы: "
                    + "разрешены латиница, цифры, точка, дефис и подчёркивание");
            }

            if (string.IsNullOrWhiteSpace(manifest.Version))
            {
                result.AddError("version.missing", "не указана версия пакета (version)");
            }
            else if (!VersionPattern.IsMatch(manifest.Version))
            {
                result.AddError(
                    "version.invalid",
                    "версия «" + manifest.Version + "» не похожа на номер версии: ожидается 1, 1.0, 1.0.0 или 1.0.0-beta");
            }

            if (string.IsNullOrWhiteSpace(manifest.TargetFramework))
            {
                result.AddError("tfm.missing", "не указана целевая платформа (targetFramework)");
            }

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                result.AddWarning("name.empty", "имя робота не указано, будет использован идентификатор «" + manifest.Id + "»");
            }

            if (string.IsNullOrWhiteSpace(manifest.CreatedAt))
            {
                result.AddWarning("createdAt.empty", "время сборки пакета не записано");
            }
        }

        private static void ValidateEntryPoint(RobotPackageValidationResult result, RobotPackageManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest.EntryType))
            {
                result.AddError(
                    "entryType.missing",
                    "не указана точка входа (entryType): без типа активности пакет запустить нельзя");
            }

            if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
            {
                result.AddError("entryAssembly.missing", "не указана сборка робота (entryAssembly)");
            }
            else if (!RobotPackageFormat.IsSafeEntryPath(manifest.EntryAssembly))
            {
                result.AddError(
                    "entryAssembly.unsafe",
                    "путь сборки робота «" + manifest.EntryAssembly + "» не является безопасным относительным путём",
                    manifest.EntryAssembly);
            }
            else if (!RobotPackageFormat.IsLibraryEntry(manifest.EntryAssembly))
            {
                result.AddError(
                    "entryAssembly.outsideLib",
                    "сборка робота должна лежать в «" + RobotPackageFormat.LibraryDirectory + "/», а не в «"
                    + manifest.EntryAssembly + "»",
                    manifest.EntryAssembly);
            }
            else if (manifest.FindFile(manifest.EntryAssembly) == null)
            {
                result.AddError(
                    "entryAssembly.notListed",
                    "сборка робота «" + manifest.EntryAssembly + "» не перечислена в files",
                    manifest.EntryAssembly);
            }

            if (!string.IsNullOrWhiteSpace(manifest.EntryWorkflow))
            {
                if (!RobotPackageFormat.IsSafeEntryPath(manifest.EntryWorkflow))
                {
                    result.AddError(
                        "entryWorkflow.unsafe",
                        "путь сценария «" + manifest.EntryWorkflow + "» не является безопасным относительным путём",
                        manifest.EntryWorkflow);
                }
                else if (!RobotPackageFormat.IsWorkflowEntry(manifest.EntryWorkflow))
                {
                    result.AddError(
                        "entryWorkflow.outsideRobot",
                        "сценарий должен лежать в «" + RobotPackageFormat.WorkflowDirectory + "/», а не в «"
                        + manifest.EntryWorkflow + "»",
                        manifest.EntryWorkflow);
                }
                else if (manifest.FindFile(manifest.EntryWorkflow) == null)
                {
                    result.AddError(
                        "entryWorkflow.notListed",
                        "сценарий «" + manifest.EntryWorkflow + "» не перечислен в files",
                        manifest.EntryWorkflow);
                }
            }

            ValidateArguments(result, manifest);
        }

        private static void ValidateArguments(RobotPackageValidationResult result, RobotPackageManifest manifest)
        {
            if (manifest.Arguments == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RobotPackageArgument argument in manifest.Arguments)
            {
                if (argument == null || string.IsNullOrWhiteSpace(argument.Name))
                {
                    result.AddError("arguments.empty", "в манифесте есть аргумент без имени");
                    continue;
                }

                if (!seen.Add(argument.Name))
                {
                    result.AddError("arguments.duplicate", "аргумент «" + argument.Name + "» объявлен дважды");
                }

                if (string.IsNullOrWhiteSpace(argument.Type))
                {
                    result.AddError("arguments.typeMissing", "у аргумента «" + argument.Name + "» не указан тип");
                }

                if (!string.IsNullOrWhiteSpace(argument.Direction)
                    && argument.Direction != "In"
                    && argument.Direction != "Out"
                    && argument.Direction != "InOut")
                {
                    result.AddError(
                        "arguments.directionInvalid",
                        "у аргумента «" + argument.Name + "» направление «" + argument.Direction
                        + "»: допустимы In, Out, InOut");
                }
            }
        }

        private static void ValidateFiles(RobotPackageValidationResult result, RobotPackageManifest manifest)
        {
            if (manifest.Files == null || manifest.Files.Count == 0)
            {
                result.AddError("files.empty", "в манифесте нет ни одного файла: запускать нечего");
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (RobotPackageFile file in manifest.Files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Path))
                {
                    result.AddError("files.pathMissing", "в манифесте есть файл без пути");
                    continue;
                }

                string path = RobotPackageFormat.NormalizeEntryPath(file.Path);

                if (!RobotPackageFormat.IsSafeEntryPath(path))
                {
                    result.AddError(
                        "files.unsafePath",
                        "путь «" + file.Path + "» не является безопасным относительным путём внутри пакета",
                        file.Path);
                    continue;
                }

                if (!RobotPackageFormat.IsLibraryEntry(path) && !RobotPackageFormat.IsWorkflowEntry(path))
                {
                    result.AddError(
                        "files.outsidePackage",
                        "файлы пакета живут только в «" + RobotPackageFormat.LibraryDirectory + "/» и «"
                        + RobotPackageFormat.WorkflowDirectory + "/», а «" + file.Path + "» — нет",
                        file.Path);
                }

                if (!seen.Add(path))
                {
                    result.AddError("files.duplicate", "файл «" + file.Path + "» перечислен дважды", file.Path);
                }

                if (file.Size < 0)
                {
                    result.AddError("files.sizeInvalid", "у файла «" + file.Path + "» отрицательный размер", file.Path);
                }

                if (!RobotPackageHash.IsSha256Hex(file.Sha256))
                {
                    result.AddError(
                        "files.hashInvalid",
                        "у файла «" + file.Path + "» не sha256 в шестнадцатеричном виде",
                        file.Path);
                }
            }

            ValidateContentHash(result, manifest);
            ValidateDependencies(result, manifest);
        }

        private static void ValidateContentHash(RobotPackageValidationResult result, RobotPackageManifest manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest.ContentHash))
            {
                result.AddError("contentHash.missing", "не указан хэш содержимого пакета (contentHash)");
                return;
            }

            if (!RobotPackageHash.IsSha256Hex(manifest.ContentHash))
            {
                result.AddError("contentHash.invalid", "contentHash не является sha256 в шестнадцатеричном виде");
                return;
            }

            string actual = RobotPackageHash.OfContent(manifest.Files);

            if (!string.Equals(actual, manifest.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError(
                    "contentHash.mismatch",
                    "contentHash «" + manifest.ContentHash + "» не совпал с перечнем файлов «" + actual
                    + "»: манифест правился после упаковки");
            }
        }

        private static void ValidateDependencies(RobotPackageValidationResult result, RobotPackageManifest manifest)
        {
            if (manifest.Dependencies == null)
            {
                return;
            }

            foreach (RobotPackageDependency dependency in manifest.Dependencies)
            {
                if (dependency == null || string.IsNullOrWhiteSpace(dependency.File))
                {
                    result.AddError("dependencies.fileMissing", "у зависимости не указан файл");
                    continue;
                }

                if (manifest.FindFile(dependency.File) == null)
                {
                    result.AddError(
                        "dependencies.notListed",
                        "зависимость «" + dependency.File + "» не перечислена в files",
                        dependency.File);
                }
            }
        }
    }
}
