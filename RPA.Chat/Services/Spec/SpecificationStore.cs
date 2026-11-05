using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Agent.Core.Services;
using Agent.Core.Services.Documents;
using Newtonsoft.Json;

namespace RPA.Chat.Services.Spec
{
    /// <summary>Раскладка каталога ТЗ в проекте (<c>TZ-01</c>): имена файлов в одном месте.</summary>
    public static class SpecLayout
    {
        public const string RootFolderName = "docs";
        public const string SpecFolderName = "spec";
        public const string TextFileName = "spec.md";
        public const string DocumentFileName = "specification.json";
        public const string MarkdownFileName = "specification.md";
        public const string MetaFileName = "meta.json";
        public const string VersionsFolderName = "versions";
        public const string SourceFolderName = "source";
        public const string RunsFolderName = "runs";

        /// <summary>
        /// Слаг каталога: буквы (включая кириллицу), цифры, '-', '_'. Пробелы становятся '-',
        /// запрещённые символы отбрасываются. Пустой результат невозможен — иначе «spec».
        /// </summary>
        public static string Slugify(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "spec";
            }

            string trimmed = Path.GetFileNameWithoutExtension(name.Trim());
            var sb = new StringBuilder(trimmed.Length);
            foreach (char symbol in trimmed)
            {
                if (char.IsLetterOrDigit(symbol) || symbol == '_')
                {
                    sb.Append(symbol);
                }
                else if (char.IsWhiteSpace(symbol) || symbol == '-' || symbol == '.')
                {
                    if (sb.Length == 0 || sb[sb.Length - 1] != '-')
                    {
                        sb.Append('-');
                    }
                }
            }

            string slug = sb.ToString().Trim('-').ToLowerInvariant();
            if (slug.Length > 80)
            {
                slug = slug.Substring(0, 80).Trim('-');
            }

            return slug.Length == 0 ? "spec" : slug;
        }
    }

    /// <summary>Запрос импорта ТЗ: файл либо готовый текст (<c>TZ-01</c>).</summary>
    public sealed class SpecImportRequest
    {
        /// <summary>Путь к файлу ТЗ (md, txt, docx, pdf, xlsx).</summary>
        public string Path { get; set; }

        /// <summary>Готовый текст ТЗ (вставлен в чат).</summary>
        public string Text { get; set; }

        /// <summary>Имя источника: от него берётся слаг, если он не задан явно.</summary>
        public string Name { get; set; }

        /// <summary>Слаг каталога ТЗ; пусто — из <see cref="Name"/>.</summary>
        public string Slug { get; set; }

        /// <summary>Комментарий к версии.</summary>
        public string Note { get; set; }
    }

    /// <summary>Чем закончился импорт.</summary>
    public enum SpecImportOutcome
    {
        Created,
        Updated,
        Unchanged,
        Failed
    }

    /// <summary>Итог импорта: где лежит, какая версия, что изменилось.</summary>
    public sealed class SpecImportResult
    {
        public SpecImportOutcome Outcome { get; set; }

        public string Slug { get; set; } = string.Empty;

        public int Version { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        public string SpecDirectory { get; set; } = string.Empty;

        public string TextPath { get; set; } = string.Empty;

        public string VersionPath { get; set; } = string.Empty;

        public string SourcePath { get; set; } = string.Empty;

        public string Diff { get; set; } = string.Empty;

        public int Chars { get; set; }

        public bool Truncated { get; set; }

        public string Format { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public bool Ok => Outcome != SpecImportOutcome.Failed;

        /// <summary>Ответ инструмента: «версия N, sha, символов/страниц» + дифф.</summary>
        public string Describe()
        {
            if (Outcome == SpecImportOutcome.Failed)
            {
                return "ТЗ не импортировано: " + Reason;
            }

            var text = new StringBuilder();
            text.Append(Outcome == SpecImportOutcome.Unchanged
                ? "ТЗ без изменений, новая версия не создана (версия " + Version + ")"
                : (Outcome == SpecImportOutcome.Created ? "ТЗ принято, версия " : "ТЗ обновлено, версия ") + Version);
            text.Append(", sha: ").Append(ShortSha(Sha256)).Append(", символов: ").Append(Chars);
            if (!string.IsNullOrWhiteSpace(Format)) text.Append(", формат: ").Append(Format);
            if (Truncated) text.Append(", усечено по лимиту");
            text.AppendLine();
            text.Append("каталог: ").Append(SpecDirectory);
            text.AppendLine().Append("версия: ").Append(VersionPath);
            if (!string.IsNullOrWhiteSpace(SourcePath)) text.AppendLine().Append("исходник: ").Append(SourcePath);
            if (!string.IsNullOrWhiteSpace(Diff)) text.AppendLine().AppendLine(Diff);
            return text.ToString();
        }

        internal static string ShortSha(string sha)
        {
            return string.IsNullOrWhiteSpace(sha) ? "?" : (sha.Length > 8 ? sha.Substring(0, 8) : sha);
        }
    }

    /// <summary>Итог сохранения модели ТЗ.</summary>
    public sealed class SpecSaveResult
    {
        public bool Ok { get; set; }

        public string Error { get; set; } = string.Empty;

        public string JsonPath { get; set; } = string.Empty;

        public string MarkdownPath { get; set; } = string.Empty;

        public SpecValidationReport Report { get; set; }

        public static SpecSaveResult Failure(string error, SpecValidationReport report = null)
        {
            return new SpecSaveResult { Ok = false, Error = error ?? "не сохранено", Report = report };
        }
    }

    /// <summary>Краткое описание ТЗ проекта — для списка в ответе инструмента.</summary>
    public sealed class SpecSummary
    {
        public string Slug { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public int Version { get; set; }

        public string ImportedAtUtc { get; set; } = string.Empty;

        public bool HasModel { get; set; }
    }

    /// <summary>Метаданные каталога ТЗ (<c>meta.json</c>): версия, sha исходника, размер.</summary>
    public sealed class SpecMeta
    {
        public int SchemaVersion { get; set; } = 1;

        public string Slug { get; set; } = string.Empty;

        public string SourceName { get; set; } = string.Empty;

        public string Sha256 { get; set; } = string.Empty;

        public long Size { get; set; }

        public string ImportedAtUtc { get; set; } = string.Empty;

        public int Version { get; set; }

        public string CurrentVersionFile { get; set; } = string.Empty;

        public int Chars { get; set; }

        public bool Truncated { get; set; }

        public string Format { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Файловое хранилище ТЗ в проекте (<c>TZ-01</c>): <c>&lt;проект&gt;\docs\spec\&lt;slug&gt;\</c>.
    /// <para>Раскладка: <c>spec.md</c> (текущая версия), <c>versions\v0001-&lt;sha8&gt;.md</c>,
    /// <c>source\</c> (исходник байт-в-байт), <c>meta.json</c>, <c>specification.json</c> (модель),
    /// <c>specification.md</c> (производное представление), <c>runs\</c> (прогоны тест-кейсов).</para>
    /// <para>Ничего не пишется в <c>%AppData%</c>: пустой корень проекта — это отказ с причиной,
    /// иначе ТЗ терялось бы вместе с профилем пользователя.</para>
    /// </summary>
    public interface ISpecificationStore
    {
        /// <summary>Корень открытого проекта; пусто — «проект не открыт».</summary>
        string ProjectRoot { get; }

        /// <summary>Каталог ТЗ по слагу.</summary>
        string GetSpecDirectory(string slug);

        /// <summary>Принимает ТЗ: сохраняет исходник, создаёт версию, считает дифф.</summary>
        SpecImportResult Import(SpecImportRequest request);

        /// <summary>Все ТЗ проекта.</summary>
        IReadOnlyList<SpecSummary> List();

        /// <summary>Читает модель ТЗ (<c>specification.json</c>).</summary>
        bool TryLoad(string slug, out RpaSpecification specification, out string error);

        /// <summary>Сохраняет модель: JSON — источник правды, markdown — производное.</summary>
        SpecSaveResult Save(RpaSpecification specification);

        /// <summary>Читает текущую версию текста ТЗ (<c>spec.md</c>).</summary>
        bool TryReadCurrentText(string slug, out string text, out string error);

        /// <summary>Добавляет версию текста (правка по ответу на вопрос, <c>TZ-09</c>).</summary>
        SpecImportResult AppendVersion(string slug, string text, string note);
    }

    /// <summary>Файловая реализация <see cref="ISpecificationStore"/>.</summary>
    public sealed class SpecificationStore : ISpecificationStore
    {
        private readonly Func<string> _projectRoot;
        private readonly Func<DateTime> _clock;

        public SpecificationStore(Func<string> projectRootProvider, Func<DateTime> clock = null)
        {
            _projectRoot = projectRootProvider ?? (() => string.Empty);
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public string ProjectRoot => (_projectRoot() ?? string.Empty).Trim();

        public string GetSpecDirectory(string slug)
        {
            return Path.Combine(ProjectRoot, SpecLayout.RootFolderName, SpecLayout.SpecFolderName,
                SpecLayout.Slugify(slug));
        }

        public SpecImportResult Import(SpecImportRequest request)
        {
            if (request == null)
            {
                return Failed("пустой запрос импорта");
            }

            string root = ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                return Failed("проект не открыт: ТЗ хранится в <проект>\\docs\\spec, " +
                              "откройте проект (в %AppData% ТЗ не пишется)");
            }

            bool fromFile = !string.IsNullOrWhiteSpace(request.Path);
            if (!fromFile && string.IsNullOrWhiteSpace(request.Text))
            {
                return Failed("нечего импортировать: укажите path к файлу ТЗ или text");
            }

            string name = !string.IsNullOrWhiteSpace(request.Name)
                ? request.Name
                : (fromFile ? Path.GetFileName(request.Path) : "spec.md");
            string slug = SpecLayout.Slugify(string.IsNullOrWhiteSpace(request.Slug) ? name : request.Slug);

            byte[] sourceBytes;
            string format;
            DocumentTextResult document;

            if (fromFile)
            {
                string fullPath = ResolvePath(request.Path, root);
                if (!File.Exists(fullPath))
                {
                    return Failed("файл ТЗ не найден: " + fullPath);
                }

                var info = new FileInfo(fullPath);
                if (info.Length > DocumentTextReadOptions.Default.MaxSourceBytes)
                {
                    return Failed("файл больше лимита " + DocumentTextReadOptions.Default.MaxSourceBytes +
                                  " байт (размер " + info.Length + ")");
                }

                try
                {
                    sourceBytes = File.ReadAllBytes(fullPath);
                }
                catch (Exception ex)
                {
                    return Failed("файл не прочитан: " + ex.Message);
                }

                format = DocumentTextReaders.FormatOf(fullPath);
                document = DocumentTextReaders.Read(fullPath, DocumentTextReadOptions.Default);
            }
            else
            {
                sourceBytes = new UTF8Encoding(false).GetBytes(request.Text);
                format = string.IsNullOrWhiteSpace(Path.GetExtension(name)) ? "md" : DocumentTextReaders.FormatOf(name);
                document = DocumentTextReaders.ReadText(request.Text, name, DocumentTextReadOptions.Default);
            }

            string sha = Sha256Of(sourceBytes);
            string directory = GetSpecDirectory(slug);
            string metaPath = Path.Combine(directory, SpecLayout.MetaFileName);
            var meta = ReadMeta(metaPath);

            string sourcePath = WriteSource(directory, name, sourceBytes);
            if (!document.Ok)
            {
                // Исходник уже сохранён: текст не получен, но байты не потеряны.
                return new SpecImportResult
                {
                    Outcome = SpecImportOutcome.Failed,
                    Slug = slug,
                    Sha256 = sha,
                    SpecDirectory = directory,
                    SourcePath = sourcePath,
                    Format = format,
                    Reason = document.Describe()
                };
            }

            if (meta != null && string.Equals(meta.Sha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                return new SpecImportResult
                {
                    Outcome = SpecImportOutcome.Unchanged,
                    Slug = slug,
                    Version = meta.Version,
                    Sha256 = sha,
                    SpecDirectory = directory,
                    TextPath = Path.Combine(directory, SpecLayout.TextFileName),
                    VersionPath = Path.Combine(directory, meta.CurrentVersionFile ?? string.Empty),
                    SourcePath = sourcePath,
                    Chars = meta.Chars,
                    Truncated = meta.Truncated,
                    Format = meta.Format,
                    Reason = "тот же sha256 — версия не создана"
                };
            }

            int version = (meta?.Version ?? 0) + 1;
            string previousText = null;
            string textPath = Path.Combine(directory, SpecLayout.TextFileName);
            if (File.Exists(textPath))
            {
                try
                {
                    previousText = File.ReadAllText(textPath);
                }
                catch (Exception)
                {
                    previousText = null;
                }
            }

            string versionFile = "v" + version.ToString("0000") + "-" + SpecImportResult.ShortSha(sha) + ".md";
            string versionPath = Path.Combine(directory, SpecLayout.VersionsFolderName, versionFile);
            Directory.CreateDirectory(Path.GetDirectoryName(versionPath));

            try
            {
                File.WriteAllText(versionPath, document.Text, new UTF8Encoding(false));
                File.WriteAllText(textPath, document.Text, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                return Failed("версия ТЗ не записана: " + ex.Message);
            }

            string diff = previousText == null
                ? string.Empty
                : "изменения относительно предыдущей версии:" + Environment.NewLine +
                  TextDiff.Diff(previousText, document.Text).Summary;

            var updated = new SpecMeta
            {
                Slug = slug,
                SourceName = name,
                Sha256 = sha,
                Size = sourceBytes.LongLength,
                ImportedAtUtc = _clock().ToString("u"),
                Version = version,
                CurrentVersionFile = Path.Combine(SpecLayout.VersionsFolderName, versionFile),
                Chars = document.Length,
                Truncated = document.Truncated,
                Format = format,
                Note = request.Note ?? string.Empty
            };

            try
            {
                File.WriteAllText(metaPath, JsonConvert.SerializeObject(updated,
                    new JsonSerializerSettings { Formatting = Formatting.Indented }), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                return Failed("meta.json не записан: " + ex.Message);
            }

            return new SpecImportResult
            {
                Outcome = meta == null ? SpecImportOutcome.Created : SpecImportOutcome.Updated,
                Slug = slug,
                Version = version,
                Sha256 = sha,
                SpecDirectory = directory,
                TextPath = textPath,
                VersionPath = versionPath,
                SourcePath = sourcePath,
                Diff = diff,
                Chars = document.Length,
                Truncated = document.Truncated,
                Format = format,
                Reason = document.Reason
            };
        }

        public IReadOnlyList<SpecSummary> List()
        {
            var result = new List<SpecSummary>();
            string root = ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                return result;
            }

            string specsRoot = Path.Combine(root, SpecLayout.RootFolderName, SpecLayout.SpecFolderName);
            if (!Directory.Exists(specsRoot))
            {
                return result;
            }

            foreach (string directory in Directory.GetDirectories(specsRoot))
            {
                var meta = ReadMeta(Path.Combine(directory, SpecLayout.MetaFileName));
                if (meta == null)
                {
                    continue;
                }

                string title = meta.Slug;
                var specification = new RpaSpecification();
                bool hasModel = RpaSpecification.TryLoad(Path.Combine(directory, SpecLayout.DocumentFileName),
                    out specification, out _);
                if (hasModel && !string.IsNullOrWhiteSpace(specification.Title))
                {
                    title = specification.Title;
                }

                result.Add(new SpecSummary
                {
                    Slug = meta.Slug,
                    Title = title,
                    Version = meta.Version,
                    ImportedAtUtc = meta.ImportedAtUtc,
                    HasModel = hasModel
                });
            }

            return result.OrderBy(item => item.Slug, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public bool TryLoad(string slug, out RpaSpecification specification, out string error)
        {
            specification = null;
            error = null;

            string root = ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "проект не открыт: спецификация ТЗ читается из <проект>\\docs\\spec";
                return false;
            }

            string path = Path.Combine(GetSpecDirectory(slug), SpecLayout.DocumentFileName);
            if (!File.Exists(path))
            {
                error = "модель ТЗ не собрана: " + path + " отсутствует — вызовите spec_extract";
                return false;
            }

            return RpaSpecification.TryLoad(path, out specification, out error);
        }

        public SpecSaveResult Save(RpaSpecification specification)
        {
            if (specification == null)
            {
                return SpecSaveResult.Failure("пустая спецификация");
            }

            string root = ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                return SpecSaveResult.Failure("проект не открыт: спецификация пишется в <проект>\\docs\\spec");
            }

            specification.Normalize();
            if (string.IsNullOrWhiteSpace(specification.Slug))
            {
                specification.Slug = SpecLayout.Slugify(specification.Title);
            }

            string directory = GetSpecDirectory(specification.Slug);
            try
            {
                Directory.CreateDirectory(directory);
                string jsonPath = Path.Combine(directory, SpecLayout.DocumentFileName);
                string markdownPath = Path.Combine(directory, SpecLayout.MarkdownFileName);

                File.WriteAllText(jsonPath, specification.ToJson(), new UTF8Encoding(false));
                File.WriteAllText(markdownPath, SpecificationText.RenderMarkdown(specification),
                    new UTF8Encoding(false));

                return new SpecSaveResult
                {
                    Ok = true,
                    JsonPath = jsonPath,
                    MarkdownPath = markdownPath,
                    Report = SpecificationValidator.Validate(specification)
                };
            }
            catch (Exception ex)
            {
                return SpecSaveResult.Failure("спецификация не записана: " + ex.Message);
            }
        }

        public bool TryReadCurrentText(string slug, out string text, out string error)
        {
            text = null;
            error = null;

            string root = ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                error = "проект не открыт: текст ТЗ читается из <проект>\\docs\\spec";
                return false;
            }

            string path = Path.Combine(GetSpecDirectory(slug), SpecLayout.TextFileName);
            if (!File.Exists(path))
            {
                error = "текст ТЗ не найден: " + path;
                return false;
            }

            try
            {
                text = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex)
            {
                error = "текст ТЗ не прочитан: " + ex.Message;
                return false;
            }
        }

        public SpecImportResult AppendVersion(string slug, string text, string note)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Failed("пустой текст новой версии ТЗ");
            }

            string root = ProjectRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                return Failed("проект не открыт: версии ТЗ пишутся в <проект>\\docs\\spec");
            }

            slug = SpecLayout.Slugify(slug);
            string directory = GetSpecDirectory(slug);
            string metaPath = Path.Combine(directory, SpecLayout.MetaFileName);
            var meta = ReadMeta(metaPath);
            if (meta == null)
            {
                return Failed("ТЗ «" + slug + "» не импортировано: сначала spec_import");
            }

            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            string sha = Sha256Of(bytes);
            int version = meta.Version + 1;
            string versionFile = "v" + version.ToString("0000") + "-" + SpecImportResult.ShortSha(sha) + ".md";
            string versionPath = Path.Combine(directory, SpecLayout.VersionsFolderName, versionFile);
            string textPath = Path.Combine(directory, SpecLayout.TextFileName);

            string previousText = null;
            if (File.Exists(textPath))
            {
                previousText = File.ReadAllText(textPath);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(versionPath));
                File.WriteAllText(versionPath, text, new UTF8Encoding(false));
                File.WriteAllText(textPath, text, new UTF8Encoding(false));

                meta.Version = version;
                meta.Sha256 = sha;
                meta.Chars = text.Length;
                meta.CurrentVersionFile = Path.Combine(SpecLayout.VersionsFolderName, versionFile);
                meta.ImportedAtUtc = _clock().ToString("u");
                meta.Note = note ?? string.Empty;
                meta.SourceName = meta.SourceName ?? string.Empty;
                File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta,
                    new JsonSerializerSettings { Formatting = Formatting.Indented }), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                return Failed("версия ТЗ не записана: " + ex.Message);
            }

            return new SpecImportResult
            {
                Outcome = SpecImportOutcome.Updated,
                Slug = slug,
                Version = version,
                Sha256 = sha,
                SpecDirectory = directory,
                TextPath = textPath,
                VersionPath = versionPath,
                Chars = text.Length,
                Format = meta.Format,
                Diff = previousText == null
                    ? string.Empty
                    : "изменения относительно предыдущей версии:" + Environment.NewLine +
                      TextDiff.Diff(previousText, text).Summary
            };
        }

        // ------------------------------------------------------------------

        private static SpecImportResult Failed(string reason)
        {
            return new SpecImportResult { Outcome = SpecImportOutcome.Failed, Reason = reason ?? "неизвестная ошибка" };
        }

        private static string ResolvePath(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(path) ? path : Path.Combine(root, path);
        }

        private static string WriteSource(string directory, string name, byte[] bytes)
        {
            try
            {
                string sourceDirectory = Path.Combine(directory, SpecLayout.SourceFolderName);
                Directory.CreateDirectory(sourceDirectory);
                string fileName = string.IsNullOrWhiteSpace(name) ? "source.bin" : Path.GetFileName(name);
                string path = Path.Combine(sourceDirectory, fileName);
                File.WriteAllBytes(path, bytes);
                return path;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static SpecMeta ReadMeta(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<SpecMeta>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Sha256Of(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes ?? new byte[0])).Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }
    }
}
