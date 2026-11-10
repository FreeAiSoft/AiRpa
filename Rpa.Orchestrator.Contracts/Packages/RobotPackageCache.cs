using System;
using System.IO;

namespace RPA.Orchestrator.Contracts.Packages
{
    /// <summary>
    /// Локальный кэш пакетов (`OR-03`): «скачал один раз — запускаю сколько нужно».
    /// <para>
    /// Раскладка кэша: <c>&lt;root&gt;\&lt;id&gt;\&lt;version&gt;\&lt;id&gt;-&lt;version&gt;.zip</c>
    /// и рядом распакованные каталоги <c>…\&lt;contentHash&gt;\</c>. Ключ распаковки — <b>хэш
    /// содержимого</b>, а не версия: если пакет версии 1.0.0 пересобрали (а это запрещено, но кэш
    /// мог остаться от старых времён), распаковка не будет переиспользована и не подменит файлы.
    /// </para>
    /// <para>
    /// Готовность распаковки помечается файлом <see cref="RobotPackageFormat.ExtractionMarkerFileName"/>
    /// с тем же хэшем: недописанный каталог (упали в середине) не будет принят за готовый.
    /// </para>
    /// </summary>
    public static class RobotPackageCache
    {
        /// <summary>Переменная окружения, перекрывающая корень кэша (нужна агентам и тестам).</summary>
        public const string EnvironmentVariable = "RPA_PACKAGE_CACHE";

        /// <summary>
        /// Корень кэша по умолчанию: переменная окружения, иначе <c>%LOCALAPPDATA%\RPA\packages</c>
        /// (профиль пользователя доступен на запись всегда, в отличие от каталога программы).
        /// </summary>
        public static string DefaultRoot
        {
            get
            {
                string fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);

                if (!string.IsNullOrWhiteSpace(fromEnvironment))
                {
                    return Path.GetFullPath(fromEnvironment.Trim());
                }

                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                if (string.IsNullOrWhiteSpace(local))
                {
                    local = Path.GetTempPath();
                }

                return Path.Combine(local, "RPA", "packages");
            }
        }

        /// <summary>Путь файла пакета в кэше.</summary>
        public static string PackageFilePath(string root, string id, string version)
        {
            string directory = VersionDirectory(root, id, version);

            return Path.Combine(directory, Sanitize(id) + "-" + Sanitize(version) + ".zip");
        }

        /// <summary>Каталог распакованного пакета: ключ — хэш содержимого.</summary>
        public static string ExtractionPath(string root, string id, string version, string contentHash)
        {
            string key = string.IsNullOrWhiteSpace(contentHash) ? "unknown" : Sanitize(contentHash);

            return Path.Combine(VersionDirectory(root, id, version), key);
        }

        /// <summary>Каталог распаковки готов: маркер на месте и хэш в нём совпадает.</summary>
        public static bool IsExtracted(string extractionPath, string contentHash)
        {
            if (string.IsNullOrWhiteSpace(extractionPath))
            {
                return false;
            }

            string marker = Path.Combine(extractionPath, RobotPackageFormat.ExtractionMarkerFileName);

            try
            {
                if (!File.Exists(marker))
                {
                    return false;
                }

                string recorded = File.ReadAllText(marker).Trim();

                // Пустой маркер — распаковка шла старым кодом или оборвалась: считаем неготовой.
                return !string.IsNullOrWhiteSpace(recorded)
                    && string.Equals(recorded, contentHash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Кладёт пакет в кэш по правилу неизменяемости: нет файла — копируем, есть с тем же
        /// хэшем — переиспользуем, есть с другим — отказ (версию надо поднимать, а не перезаписывать).
        /// </summary>
        public static bool TryPlace(string root, RobotPackage package, out string placedPath, out string error)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            placedPath = null;
            error = null;

            string destination = PackageFilePath(root, package.Id, package.Version);

            try
            {
                string directory = Path.GetDirectoryName(destination);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(destination))
                {
                    string existing = RobotPackageHash.OfFile(destination);

                    if (string.Equals(existing, package.PackageSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        placedPath = destination;
                        return true;
                    }

                    error = "версия «" + package.Version + "» робота «" + package.Id
                        + "» уже лежит в кэше с другим содержимым (sha256 " + existing
                        + " вместо " + package.PackageSha256 + "): пакет неизменяем, поднимите версию"
                        + Environment.NewLine + destination;
                    return false;
                }

                File.Copy(package.PackagePath, destination, false);
                placedPath = destination;

                return true;
            }
            catch (Exception ex)
            {
                error = "не удалось положить пакет в кэш «" + destination + "»: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Готовит распакованный каталог пакета: если маркер на месте — сразу отдаёт путь,
        /// иначе распаковывает во временный каталог и переименовывает (атомарность на уровне каталога).
        /// </summary>
        public static bool TryEnsureExtracted(string root, RobotPackage package, out string extractedPath, out string error)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            error = null;
            extractedPath = ExtractionPath(root, package.Id, package.Version, package.Manifest.ContentHash);

            if (IsExtracted(extractedPath, package.Manifest.ContentHash))
            {
                return true;
            }

            string temporary = extractedPath + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            try
            {
                if (Directory.Exists(temporary))
                {
                    Directory.Delete(temporary, true);
                }

                if (!RobotPackageReader.TryExtract(package, temporary, out error))
                {
                    TryDeleteDirectory(temporary);
                    return false;
                }

                File.WriteAllText(
                    Path.Combine(temporary, RobotPackageFormat.ExtractionMarkerFileName),
                    package.Manifest.ContentHash ?? string.Empty);

                if (Directory.Exists(extractedPath))
                {
                    // Оборванная распаковка прошлого запуска — сносим и ставим готовую.
                    TryDeleteDirectory(extractedPath);
                }

                string parent = Path.GetDirectoryName(extractedPath);

                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                Directory.Move(temporary, extractedPath);

                return true;
            }
            catch (Exception ex)
            {
                TryDeleteDirectory(temporary);
                error = "не удалось подготовить распаковку пакета в «" + extractedPath + "»: " + ex.Message;
                return false;
            }
        }

        private static string VersionDirectory(string root, string id, string version)
        {
            string baseRoot = string.IsNullOrWhiteSpace(root) ? DefaultRoot : Path.GetFullPath(root);

            return Path.Combine(baseRoot, Sanitize(id), Sanitize(version));
        }

        /// <summary>Убирает из имени каталога всё, что нельзя использовать в пути.</summary>
        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var text = new System.Text.StringBuilder(value.Length);

            foreach (char symbol in value.Trim())
            {
                bool allowed = char.IsLetterOrDigit(symbol) || symbol == '.' || symbol == '-' || symbol == '_';

                text.Append(allowed ? symbol : '_');
            }

            return text.ToString();
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception)
            {
                // Кэш — не ценность: не смогли снести — распаковка пойдёт в новый каталог.
            }
        }
    }
}
