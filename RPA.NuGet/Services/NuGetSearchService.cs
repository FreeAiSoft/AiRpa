using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Searches a NuGet package source using NuGet.Protocol
    /// and returns lightweight metadata used by the package manager UI.
    /// <para>
    /// Источник задаётся конструктором: панель создаёт сервис заново при смене «Package source».
    /// Набор используемых членов <see cref="IPackageSearchMetadata"/> намеренно ограничен теми, что
    /// есть во всех поддерживаемых версиях NuGet.Protocol (<c>Identity</c>, <c>Description</c>,
    /// <c>Authors</c>, <c>IconUrl</c>, <c>ProjectUrl</c>, <c>LicenseUrl</c>, <c>Tags</c>,
    /// <c>Published</c>, <c>DownloadCount</c>).
    /// </para>
    /// </summary>
    public class NuGetSearchService
    {
        // URL nuget.org больше не хранится в коде: источник задаётся панелью из NuGet.Config.

        private readonly SourceRepository _repository;

        private LocalPackageIndexCache _localIndexCache;
        private readonly object _localMemoLock = new object();
        private readonly Dictionary<string, List<NuGetPackageInfo>> _localSearchMemo =
            new Dictionary<string, List<NuGetPackageInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _localVersionsMemo =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NuGetPackageInfo> _localPackageInfoMemo =
            new Dictionary<string, NuGetPackageInfo>(StringComparer.OrdinalIgnoreCase);

        public NuGetSearchService(string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
                throw new ArgumentException("Source URL is required.", nameof(sourceUrl));

            SourceUrl = sourceUrl;
            _repository = Repository.Factory.GetCoreV3(sourceUrl);
            NuGetLog.Info("Search service created for source: " + sourceUrl);
        }
        /// <summary>
        /// Сбрасывает in-memory мемоизацию локального источника (поиск/версии/карточки).
        /// Кнопка «Сбросить кэши оптимизаций» вызывает этот метод через
        /// <see cref="NuGetOptimizationCache"/> и <see cref="NuGetPanelViewModel.ResetCaches"/>.
        /// </summary>
        public void ClearMemoization()
        {
            lock (_localMemoLock)
            {
                _localSearchMemo.Clear();
                _localVersionsMemo.Clear();
                _localPackageInfoMemo.Clear();
            }

            NuGetLog.Info("Local source memoization cleared for source: " + SourceUrl);
        }



        /// <summary>Адрес источника, к которому подключён этот экземпляр.</summary>
        public string SourceUrl { get; }

        /// <summary>
        /// Источник — локальная папка/UNC, а не сетевой канал (http/https). Для локального
        /// источника пустой запрос поиска означает «показать все пакеты из папки».
        /// </summary>
        public bool IsLocal
        {
            get
            {
                if (Uri.TryCreate(SourceUrl, UriKind.Absolute, out Uri uri))
                    return uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps;

                // Относительный путь (например, "offline") не парсится как Absolute — это локальная папка.
                return true;
            }
        }

        /// <summary>
        /// Страница результатов поиска. <paramref name="skip"/> — сколько карточек уже показано
        /// (кнопка «Load more» листает источник без повторной выдачи первых страниц).
        /// </summary>
        public virtual async Task<List<NuGetPackageInfo>> SearchAsync(
            string query,
            int take = 20,
            bool includePrerelease = false,
            int skip = 0)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                // Удалённый источник не умеет «показать всё»: пустой запрос там не имеет смысла.
                // Локальная папка (LocalPackageSearchResource) при пустом запросе возвращает все пакеты.
                if (!IsLocal)
                    return new List<NuGetPackageInfo>();

                query = string.Empty;
            }

            if (IsLocal)
                return await SearchLocalAsync(query, take, includePrerelease, skip);

            NuGetLog.Info("Search: query=\"" + query + "\" skip=" + skip + " take=" + take
                          + " prerelease=" + includePrerelease + " source=" + SourceUrl);
            var searchResource = await _repository.GetResourceAsync<PackageSearchResource>();
            var filter = new SearchFilter(includePrerelease: includePrerelease);
            IEnumerable<IPackageSearchMetadata> results = await searchResource.SearchAsync(
                query,
                filter,
                skip: Math.Max(skip, 0),
                take: take,
                NuGetProtocolLogger.Instance,
                CancellationToken.None);

            List<NuGetPackageInfo> packages = results.Select(ToPackageInfo).ToList();
            NuGetLog.Info("Search result: " + packages.Count + " package(s) from source " + SourceUrl);
            return packages;
        }

        /// <summary>
        /// Карточка конкретного пакета: поиск по идентификатору и выбор точного совпадения
        /// (без этого «Newtonsoft.Json» мог бы вернуть похожий пакет). <c>null</c> — пакет не найден.
        /// </summary>
        public virtual async Task<NuGetPackageInfo> GetPackageInfoAsync(string packageId, bool includePrerelease = false)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            if (IsLocal)
                return GetLocalPackageInfo(packageId, includePrerelease);

            List<NuGetPackageInfo> results = await SearchAsync(packageId, 10, includePrerelease);
            return results.FirstOrDefault(r => string.Equals(r.Id, packageId, StringComparison.OrdinalIgnoreCase))
                   ?? results.FirstOrDefault();
        }

        public virtual async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, bool includePrerelease = false)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return Array.Empty<string>();

            if (IsLocal)
                return GetLocalVersions(packageId, includePrerelease);
            NuGetLog.Info("GetVersions: id=" + packageId + " prerelease=" + includePrerelease + " source=" + SourceUrl);
            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            var cache = new SourceCacheContext();
            IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(
                packageId,
                cache,
                NuGetProtocolLogger.Instance,
                CancellationToken.None);

            IEnumerable<string> normalized = versions.Select(v => v.ToNormalizedString());
            if (!includePrerelease)
                normalized = normalized.Where(v => NuGetVersion.TryParse(v, out var nv) && !nv.IsPrerelease);

            List<string> versionList = normalized.ToList();
            NuGetLog.Info("GetVersions result: " + versionList.Count + " version(s) for " + packageId + " from source " + SourceUrl);
            return versionList;
        }

        /// <summary>
        /// Все версии пакета по убыванию (с признаками «последняя»/«последняя стабильная»/«в проекте»).
        /// <paramref name="installedVersion"/> подсвечивает установленную версию в списке.
        /// </summary>
        public virtual async Task<List<NuGetPackageVersionInfo>> GetVersionInfosAsync(
            string packageId,
            string installedVersion,
            bool includePrerelease = false)
        {
            IReadOnlyList<string> versions = await GetVersionsAsync(packageId, includePrerelease);

            List<NuGetVersion> parsed = versions
                .Select(v => NuGetVersion.TryParse(v, out NuGetVersion pv) ? pv : null)
                .Where(v => v != null)
                .OrderByDescending(v => v)
                .ToList();

            NuGetVersion latest = parsed.FirstOrDefault();
            NuGetVersion latestStable = parsed.FirstOrDefault(v => !v.IsPrerelease);

            var result = new List<NuGetPackageVersionInfo>();
            foreach (NuGetVersion version in parsed)
            {
                string text = version.ToNormalizedString();
                bool isInstalled = !string.IsNullOrWhiteSpace(installedVersion)
                                   && string.Equals(text, installedVersion, StringComparison.OrdinalIgnoreCase);
                bool isLatest = latest != null && version == latest;
                bool isLatestStable = latestStable != null && version == latestStable;

                result.Add(new NuGetPackageVersionInfo
                {
                    Version = text,
                    IsPrerelease = version.IsPrerelease,
                    IsInstalled = isInstalled,
                    IsLatest = isLatest,
                    IsLatestStable = isLatestStable,
                    Label = NuGetPackageStateResolver.VersionLabel(
                        text,
                        isLatestStable,
                        isLatest && latestStable == null,
                        isInstalled)
                });
            }

            return result;
        }

        /// <summary>
        /// Скачивает <c>.nupkg</c> из того же источника, что и поиск: прокси и авторизация берутся из
        /// NuGet.Config, TLS и повторы — из NuGet.Protocol. <c>null</c> — пакета или версии в источнике нет.
        /// <para>
        /// Пакет всегда скачивается из выбранного источника без фолбэка на другие источники.
        /// 
        /// </para>
        /// </summary>
        public virtual async Task<byte[]> DownloadNupkgAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(version))
                return null;

            if (!NuGetVersion.TryParse(version, out NuGetVersion parsed))
                return null;

            NuGetLog.Info("DownloadNupkg: id=" + packageId + " version=" + version + " source=" + SourceUrl);
            FindPackageByIdResource resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            using (var cache = new SourceCacheContext())
            using (var stream = new MemoryStream())
            {
                bool copied = await resource.CopyNupkgToStreamAsync(
                    packageId,
                    parsed,
                    stream,
                    cache,
                    NuGetProtocolLogger.Instance,
                    cancellationToken);

                byte[] data = copied && stream.Length > 0 ? stream.ToArray() : null;
                NuGetLog.Info("DownloadNupkg result: " + (data == null ? "null" : data.Length + " bytes")
                              + " for " + packageId + " " + version + " from source " + SourceUrl);
                return data;
            }
        }

        // --- Кэш локального источника (offline-папка) ---

        private LocalPackageIndexCache LocalIndexCache
        {
            get
            {
                if (_localIndexCache == null)
                    _localIndexCache = new LocalPackageIndexCache(SourceUrl);
                return _localIndexCache;
            }
        }

        private Task<List<NuGetPackageInfo>> SearchLocalAsync(
            string query,
            int take,
            bool includePrerelease,
            int skip)
        {
            string memoKey = BuildSearchMemoKey(query, take, includePrerelease, skip);
            lock (_localMemoLock)
            {
                if (_localSearchMemo.TryGetValue(memoKey, out List<NuGetPackageInfo> cached))
                    return Task.FromResult(cached);
            }

            IReadOnlyList<LocalPackageIndexEntry> entries = LocalIndexCache.Entries;
            List<LocalPackageIndexEntry> filtered = FilterLocalEntries(entries, query, includePrerelease);

            // Browse-карточка — одна на пакет: показываем самую свежую подходящую версию.
            var distinct = new List<LocalPackageIndexEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocalPackageIndexEntry entry in filtered)
            {
                if (seen.Add(entry.Id))
                    distinct.Add(entry);
            }

            int start = Math.Max(skip, 0);
            List<NuGetPackageInfo> page = distinct
                .Skip(start)
                .Take(Math.Max(take, 1))
                .Select(ToPackageInfo)
                .ToList();

            lock (_localMemoLock)
                _localSearchMemo[memoKey] = page;

            NuGetLog.Info("Search (local cache): query=\"" + query + "\" skip=" + skip + " take=" + take
                          + " prerelease=" + includePrerelease + " source=" + SourceUrl
                          + " -> " + page.Count + " package(s)");
            return Task.FromResult(page);
        }

        private NuGetPackageInfo GetLocalPackageInfo(string packageId, bool includePrerelease)
        {
            string memoKey = BuildPackageInfoMemoKey(packageId, includePrerelease);
            lock (_localMemoLock)
            {
                if (_localPackageInfoMemo.TryGetValue(memoKey, out NuGetPackageInfo cached))
                    return cached;
            }

            // Записи отсортированы по Id и убыванию версии, поэтому первое совпадение — самая свежая.
            IReadOnlyList<LocalPackageIndexEntry> entries = LocalIndexCache.Entries;
            LocalPackageIndexEntry match = entries.FirstOrDefault(entry =>
                string.Equals(entry.Id, packageId, StringComparison.OrdinalIgnoreCase)
                && (includePrerelease || !entry.IsPrerelease));

            NuGetPackageInfo info = match == null ? null : ToPackageInfo(match);
            lock (_localMemoLock)
                _localPackageInfoMemo[memoKey] = info;

            return info;
        }

        private IReadOnlyList<string> GetLocalVersions(string packageId, bool includePrerelease)
        {
            string memoKey = BuildVersionsMemoKey(packageId, includePrerelease);
            lock (_localMemoLock)
            {
                if (_localVersionsMemo.TryGetValue(memoKey, out IReadOnlyList<string> cached))
                    return cached;
            }

            IReadOnlyList<LocalPackageIndexEntry> entries = LocalIndexCache.Entries;
            List<string> versions = entries
                .Where(entry =>
                    string.Equals(entry.Id, packageId, StringComparison.OrdinalIgnoreCase)
                    && (includePrerelease || !entry.IsPrerelease))
                .Select(entry => entry.Version)
                .ToList();

            lock (_localMemoLock)
                _localVersionsMemo[memoKey] = versions;

            NuGetLog.Info("GetVersions (local cache): id=" + packageId + " prerelease=" + includePrerelease
                          + " source=" + SourceUrl + " -> " + versions.Count + " version(s)");
            return versions;
        }

        private static List<LocalPackageIndexEntry> FilterLocalEntries(
            IReadOnlyList<LocalPackageIndexEntry> entries,
            string query,
            bool includePrerelease)
        {
            var result = new List<LocalPackageIndexEntry>();
            foreach (LocalPackageIndexEntry entry in entries)
            {
                if (!includePrerelease && entry.IsPrerelease)
                    continue;

                if (query.Length == 0 || MatchesLocal(entry, query))
                    result.Add(entry);
            }

            return result;
        }

        private static bool MatchesLocal(LocalPackageIndexEntry entry, string query)
        {
            return IndexOf(entry.Id, query) >= 0
                   || IndexOf(entry.Description, query) >= 0
                   || IndexOf(entry.Tags, query) >= 0;
        }

        private static int IndexOf(string value, string query)
        {
            return string.IsNullOrEmpty(value)
                ? -1
                : value.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSearchMemoKey(string query, int take, bool includePrerelease, int skip)
        {
            return query + "|" + take + "|" + includePrerelease + "|" + skip;
        }

        private static string BuildVersionsMemoKey(string packageId, bool includePrerelease)
        {
            return packageId + "|" + includePrerelease;
        }

        private static string BuildPackageInfoMemoKey(string packageId, bool includePrerelease)
        {
            return packageId + "|" + includePrerelease;
        }

        private static NuGetPackageInfo ToPackageInfo(LocalPackageIndexEntry entry)
        {
            return new NuGetPackageInfo
            {
                Id = entry.Id,
                Version = entry.Version,
                IsPrerelease = entry.IsPrerelease,
                Description = entry.Description,
                Authors = entry.Authors,
                TotalDownloads = 0,
                IconUrl = entry.IconUrl,
                ProjectUrl = entry.ProjectUrl,
                LicenseUrl = entry.LicenseUrl,
                Tags = entry.Tags,
                Published = entry.Published
            };
        }

        private static NuGetPackageInfo ToPackageInfo(IPackageSearchMetadata metadata)
        {
            NuGetVersion version = metadata.Identity?.Version;

            return new NuGetPackageInfo
            {
                Id = metadata.Identity?.Id,
                Version = version?.ToNormalizedString(),
                IsPrerelease = version?.IsPrerelease ?? false,
                Description = metadata.Description,
                Authors = metadata.Authors,
                TotalDownloads = metadata.DownloadCount ?? 0,
                IconUrl = metadata.IconUrl?.ToString(),
                ProjectUrl = metadata.ProjectUrl?.ToString(),
                LicenseUrl = metadata.LicenseUrl?.ToString(),
                Tags = metadata.Tags,
                Published = metadata.Published
            };
        }
    }
}
