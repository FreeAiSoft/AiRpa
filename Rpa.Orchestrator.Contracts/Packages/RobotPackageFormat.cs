using System;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>
    /// Раскладка пакета робота (`OR-03`) — единственный источник правды о том, что внутри
    /// <c>robot.zip</c> и как называются его части.
    /// <para>
    /// Раскладка намеренно плоская и предсказуемая:
    /// <code>
    /// robot.zip
    /// ├── manifest.json          — манифест (RobotPackageManifest)
    /// ├── lib/                    — сборка робота и все её зависимости
    /// │   ├── Net472Robot.dll
    /// │   └── System.Activities.dll
    /// └── robot/                  — XAML сценария (точка входа — manifest.entryWorkflow)
    ///     └── Main.xaml
    /// </code>
    /// </para>
    /// <para>
    /// Пути внутри пакета всегда относительные и всегда через <c>/</c> — иначе один и тот же
    /// пакет, собранный на Windows и распакованный на другой машине, давал бы разные ключи
    /// в манифесте, а хэш содержимого переставал бы совпадать.
    /// </para>
    /// </summary>
    public static class RobotPackageFormat
    {
        /// <summary>Версия формата пакета, которую понимает этот код.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Имя файла манифеста в корне пакета.</summary>
        public const string ManifestFileName = "manifest.json";

        /// <summary>Каталог сборки робота и зависимостей.</summary>
        public const string LibraryDirectory = "lib";

        /// <summary>Каталог XAML-сценариев.</summary>
        public const string WorkflowDirectory = "robot";

        /// <summary>Имя файла сценария по умолчанию.</summary>
        public const string DefaultWorkflowFileName = "Main.xaml";

        /// <summary>
        /// Маркер готовности распакованного пакета в локальном кэше. Распаковка идёт во временный
        /// каталог и только потом переименовывается, поэтому недописанный кэш не может быть
        /// принят за готовый (см. <see cref="RobotPackageCache"/>).
        /// </summary>
        public const string ExtractionMarkerFileName = ".extracted";

        /// <summary>Путь сборки внутри пакета: <c>lib\&lt;file&gt;</c>.</summary>
        public static string LibraryEntry(string fileName)
        {
            return LibraryDirectory + "/" + RequireFileName(fileName);
        }

        /// <summary>Путь сценария внутри пакета: <c>robot\&lt;file&gt;</c>.</summary>
        public static string WorkflowEntry(string fileName)
        {
            return WorkflowDirectory + "/" + RequireFileName(fileName);
        }

        /// <summary>Приводит путь внутри пакета к каноническому виду: <c>/</c> и без ведущего <c>./</c>.</summary>
        public static string NormalizeEntryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Trim().Replace('\\', '/');

            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            return normalized;
        }

        /// <summary>Путь лежит в каталоге сборок (<c>lib/…</c>).</summary>
        public static bool IsLibraryEntry(string path)
        {
            return NormalizeEntryPath(path).StartsWith(LibraryDirectory + "/", StringComparison.Ordinal);
        }

        /// <summary>Путь лежит в каталоге сценариев (<c>robot/…</c>).</summary>
        public static bool IsWorkflowEntry(string path)
        {
            return NormalizeEntryPath(path).StartsWith(WorkflowDirectory + "/", StringComparison.Ordinal);
        }

        /// <summary>Абсолютный путь (в том числе с диском) — внутри пакета запрещён.</summary>
        public static bool IsAbsoluteEntryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = NormalizeEntryPath(path);

            // «C:/…», «/…», «\\server\share» — всё это пути хоста, а не пакета.
            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                return true;
            }

            return normalized.Length > 1 && normalized[1] == ':';
        }

        /// <summary>
        /// Безопасный относительный путь внутри пакета: не абсолютный, без <c>..</c>,
        /// без пустых сегментов и без символов, которые на Windows означают другое.
        /// </summary>
        public static bool IsSafeEntryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || IsAbsoluteEntryPath(path))
            {
                return false;
            }

            string normalized = NormalizeEntryPath(path);

            if (normalized.Length == 0 || normalized.EndsWith("/", StringComparison.Ordinal) || normalized.Contains("//"))
            {
                return false;
            }

            foreach (string segment in normalized.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    return false;
                }

                if (segment.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Имя файла без каталогов (для путей вида <c>lib/Name.dll</c>).</summary>
        public static string FileNameOf(string entryPath)
        {
            string normalized = NormalizeEntryPath(entryPath);
            int slash = normalized.LastIndexOf('/');

            return slash < 0 ? normalized : normalized.Substring(slash + 1);
        }

        private static string RequireFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Имя файла внутри пакета не может быть пустым.", nameof(fileName));
            }

            return NormalizeEntryPath(fileName);
        }
    }
}
