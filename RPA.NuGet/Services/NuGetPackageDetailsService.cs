using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Versioning;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Собирает данные для правой панели деталей: метаданные карточки из источника, список версий и
    /// текст README из самого пакета.
    /// <para>
    /// README читается из <c>.nupkg</c> (запись <c>readme</c> из nuspec, <c>README.md</c> в корне или
    /// README в подпапке — например <c>docs\README.md</c>): отдельного «readme»-ресурса в
    /// NuGet.Protocol 6.3.4 нет, а скачивание пакета у нас уже реализовано.
    /// </para>
    /// <para>
    /// Пакет скачивается <b>из выбранного источника</b> (<see cref="NuGetSearchService.DownloadNupkgAsync"/>):
    /// раньше путь был жёстко зашит на <c>api.nuget.org</c>, и для приватного/корпоративного/offline
    /// источника README не появлялся никогда. Если источник версию не отдал (в проекте зафиксирована
    /// удалённая версия), пробуется другая версия из списка. Причина неудачи попадает в
    /// <see cref="LastReadmeError"/>, а не теряется в пустом <c>catch</c>: иначе пропавшая вкладка
    /// README выглядела как «пакет без README».
    /// </para>
    /// </summary>
    public class NuGetPackageDetailsService
    {
        private const int MaxReadmeLength = 200 * 1024;

        /// <summary>
        /// Кеш прочитанных README: <c>id|version</c> → текст. Пустая строка — «README в пакете нет»
        /// (отрицательный кеш), чтобы повторный выбор пакета не скачивал его заново.
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> ReadmeCache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly NuGetPackageDownloader _downloader;

        public NuGetPackageDetailsService(NuGetPackageDownloader downloader = null)
        {
            _downloader = downloader ?? new NuGetPackageDownloader();
        }

        /// <summary>Причина, по которой README не удалось получить (<c>null</c> — ошибки не было).</summary>
        public string LastReadmeError { get; private set; }

        /// <summary>
        /// Заполняет сведения о пакете. Ошибки сети/источника не пробрасываются: панель обязана
        /// показать хотя бы то, что уже есть в строке списка (параметр <paramref name="fallback"/>).
        /// </summary>
        public async Task<NuGetPackageDetails> LoadAsync(
            NuGetSearchService search,
            NuGetPackageInfo fallback,
            string installedVersion,
            bool includePrerelease)
        {
            if (fallback == null)
                return null;

            NuGetPackageInfo info = fallback;
            if (search != null)
            {
                try
                {
                    NuGetPackageInfo found = await search.GetPackageInfoAsync(fallback.Id, includePrerelease);
                    if (found != null)
                        info = found;
                }
                catch (Exception)
                {
                    // Нет сети/источник недоступен — остаются данные из строки списка.
                }
            }

            var details = new NuGetPackageDetails
            {
                Id = info.Id,
                Version = string.IsNullOrWhiteSpace(installedVersion) ? info.Version : installedVersion,
                Description = info.Description,
                Authors = info.Authors,
                LicenseUrl = info.LicenseUrl,
                ProjectUrl = info.ProjectUrl,
                ReportAbuseUrl = BuildReportAbuseUrl(info.Id, info.Version),
                Tags = info.Tags,
                Published = info.Published,
                TotalDownloads = info.TotalDownloads,
                IconUrl = info.IconUrl,
                IsPrerelease = info.IsPrerelease
            };

            if (search != null)
            {
                try
                {
                    List<NuGetPackageVersionInfo> versions =
                        await search.GetVersionInfosAsync(info.Id, installedVersion, includePrerelease);
                    details.Versions.AddRange(versions);
                }
                catch (Exception)
                {
                    // Список версий недоступен — кнопка действия останется на данных карточки.
                }
            }

            return details;
        }

        /// <summary>
        /// Предварительные сведения из карточки поиска: показываются сразу, не дожидаясь ответа
        /// источника, поэтому в панели деталей всегда есть выбранная версия и активная кнопка
        /// действия (раньше при медленной сети ComboBox версий оставался пустым).
        /// </summary>
        public static NuGetPackageDetails FromPackageInfo(NuGetPackageInfo info, string installedVersion)
        {
            if (info == null)
                return null;

            string version = string.IsNullOrWhiteSpace(installedVersion) ? info.Version : installedVersion;
            bool isInstalled = !string.IsNullOrWhiteSpace(installedVersion);
            bool isPrerelease = NuGetVersion.TryParse(version, out NuGetVersion parsed) && parsed.IsPrerelease;

            var details = new NuGetPackageDetails
            {
                Id = info.Id,
                Version = version,
                Description = info.Description,
                Authors = info.Authors,
                LicenseUrl = info.LicenseUrl,
                ProjectUrl = info.ProjectUrl,
                ReportAbuseUrl = BuildReportAbuseUrl(info.Id, version),
                Tags = info.Tags,
                Published = info.Published,
                TotalDownloads = info.TotalDownloads,
                IconUrl = info.IconUrl,
                IsPrerelease = isPrerelease
            };

            if (!string.IsNullOrWhiteSpace(version))
            {
                details.Versions.Add(new NuGetPackageVersionInfo
                {
                    Version = version,
                    IsPrerelease = isPrerelease,
                    IsInstalled = isInstalled,
                    Label = NuGetPackageStateResolver.VersionLabel(version, false, false, isInstalled)
                });
            }

            return details;
        }

        /// <summary>
        /// Пробует прочитать README из пакета выбранного источника.
        /// <para>
        /// <paramref name="version"/> — версия, показанная в панели; <paramref name="fallbackVersion"/> —
        /// запасная (обычно последняя из списка), если у источника запрошенной версии уже нет.
        /// <c>null</c> — README в пакете отсутствует либо получить его не удалось; причина неудачи
        /// остаётся в <see cref="LastReadmeError"/>.
        /// </para>
        /// </summary>
        public async Task<string> TryReadReadmeAsync(
            NuGetSearchService search,
            string packageId,
            string version,
            string fallbackVersion = null)
        {
            LastReadmeError = null;
            NuGetLog.Info("README load: " + packageId + " " + version + " (fallback=" + fallbackVersion + ", source=" + (search != null ? search.SourceUrl : "direct") + ")");
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            foreach (string candidate in CandidateVersions(version, fallbackVersion))
            {
                string key = ReadmeCacheKey(packageId, candidate);
                if (ReadmeCache.TryGetValue(key, out string cached))
                {
                    if (!string.IsNullOrEmpty(cached))
                        return cached;

                    continue;
                }

                byte[] nupkg = await DownloadAsync(search, packageId, candidate).ConfigureAwait(false);
                if (nupkg == null || nupkg.Length == 0)
                    continue;

                string readme = ReadReadme(nupkg);
                NuGetLog.Info("README " + (string.IsNullOrWhiteSpace(readme) ? "not found" : "found (" + readme.Length + " chars)") + " in " + packageId + " " + candidate);

                // Пакет скачался — результат окончательный: и текст, и «README нет» кешируются.
                ReadmeCache[key] = readme ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(readme))
                    return readme;
            }

            return null;
        }

        /// <summary>Читает README из уже скачанного пакета (без сети — удобно для тестов).</summary>
        public static string ReadReadme(byte[] nupkg)
        {
            if (nupkg == null || nupkg.Length == 0)
                return null;

            using (var stream = new MemoryStream(nupkg))
            using (var reader = new PackageArchiveReader(stream))
            {
                string entry = FindReadmeEntry(reader);
                if (entry == null)
                    return null;

                using (Stream readme = reader.GetStream(entry))
                using (var text = new StreamReader(readme))
                {
                    char[] buffer = new char[MaxReadmeLength];
                    int read = text.Read(buffer, 0, buffer.Length);
                    return read <= 0 ? null : new string(buffer, 0, read).Trim();
                }
            }
        }

        // --- Служебное ---

        /// <summary>Версии, которые имеет смысл спросить: сначала показанная, затем запасная.</summary>
        private static IEnumerable<string> CandidateVersions(string version, string fallbackVersion)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in new[] { version, fallbackVersion })
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (seen.Add(candidate))
                    yield return candidate;
            }
        }

        private async Task<byte[]> DownloadAsync(NuGetSearchService search, string id, string version)
        {
            // 1. Выбранный источник: прокси/авторизация из NuGet.Config, отмена, корректный TLS-путь.
            NuGetLog.Info("Download nupkg " + id + " " + version + " from selected source: " + (search != null ? search.SourceUrl : "direct"));
            if (search != null)
            {
                try
                {
                    byte[] fromSource = await search.DownloadNupkgAsync(id, version).ConfigureAwait(false);
                    if (fromSource != null && fromSource.Length > 0)
                        return fromSource;

                    LastReadmeError = "package " + id + " " + version + " is not available in the selected source";
                }
                catch (Exception ex)
                {
                    LastReadmeError = ex.GetBaseException().Message;
                }
            }

            // 2. Прямая ссылка nuget.org — страховка, если источник версию не отдал.
            try
            {
                byte[] direct = await _downloader.DownloadNupkgAsync(id, version).ConfigureAwait(false);
                if (direct != null && direct.Length > 0)
                    return direct;
            }
            catch (Exception ex)
            {
                LastReadmeError = ex.GetBaseException().Message;
            }

            return null;
        }

        private static string ReadmeCacheKey(string packageId, string version)
        {
            return packageId.ToLowerInvariant() + "|" + version.ToLowerInvariant();
        }

        private static string FindReadmeEntry(PackageArchiveReader reader)
        {
            List<string> files;
            try
            {
                files = reader.GetFiles().ToList();
            }
            catch (Exception)
            {
                return null;
            }

            // 1. Запись, объявленная в nuspec (<readme>docs/README.md</readme>).
            try
            {
                string declared = reader.NuspecReader.GetReadme();
                if (!string.IsNullOrWhiteSpace(declared))
                {
                    string normalized = Normalize(declared);
                    string match = files.FirstOrDefault(f => string.Equals(Normalize(f), normalized, StringComparison.OrdinalIgnoreCase))
                                   ?? files.FirstOrDefault(f => string.Equals(Normalize(f).TrimStart('/'), normalized.TrimStart('/'), StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                        return match;
                }
            }
            catch (Exception)
            {
                // Старый nuspec без <readme> — идём по имени файла.
            }

            // 2. README в корне пакета.
            string root = files.FirstOrDefault(f => Normalize(f).IndexOf('/') < 0 && IsReadmeFile(f));
            if (root != null)
                return root;

            // 3. README в любой папке пакета (docs\README.md и т.п.): ближайший к корню.
            return files
                .Where(IsReadmeFile)
                .OrderBy(f => Normalize(f).Count(c => c == '/'))
                .ThenBy(f => f.Length)
                .FirstOrDefault();
        }

        /// <summary>Файл считается README по имени: <c>readme</c> + <c>.md/.markdown/.txt</c> (или без расширения).</summary>
        private static bool IsReadmeFile(string file)
        {
            string name = Path.GetFileName(Normalize(file));
            if (string.IsNullOrEmpty(name) || !name.StartsWith("readme", StringComparison.OrdinalIgnoreCase))
                return false;

            string extension = Path.GetExtension(name);
            return extension.Length == 0
                   || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            return path == null ? string.Empty : path.Replace('\\', '/');
        }

        private static string BuildReportAbuseUrl(string id, string version)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
                return null;

            return "https://www.nuget.org/packages/" + id + "/" + version + "/ReportAbuse";
        }
    }
}
