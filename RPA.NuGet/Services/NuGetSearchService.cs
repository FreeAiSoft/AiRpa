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
    /// Searches a NuGet package source (default: nuget.org) using NuGet.Protocol
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
        public const string DefaultSourceUrl = "https://api.nuget.org/v3/index.json";

        private readonly SourceRepository _repository;

        public NuGetSearchService(string sourceUrl = DefaultSourceUrl)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
                throw new ArgumentException("Source URL is required.", nameof(sourceUrl));

            SourceUrl = sourceUrl;
            _repository = Repository.Factory.GetCoreV3(sourceUrl);
        }

        /// <summary>Адрес источника, к которому подключён этот экземпляр.</summary>
        public string SourceUrl { get; }

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
                return new List<NuGetPackageInfo>();

            var searchResource = await _repository.GetResourceAsync<PackageSearchResource>();
            var filter = new SearchFilter(includePrerelease: includePrerelease);
            IEnumerable<IPackageSearchMetadata> results = await searchResource.SearchAsync(
                query,
                filter,
                skip: Math.Max(skip, 0),
                take: take,
                NullLogger.Instance,
                CancellationToken.None);

            return results.Select(ToPackageInfo).ToList();
        }

        /// <summary>
        /// Карточка конкретного пакета: поиск по идентификатору и выбор точного совпадения
        /// (без этого «Newtonsoft.Json» мог бы вернуть похожий пакет). <c>null</c> — пакет не найден.
        /// </summary>
        public virtual async Task<NuGetPackageInfo> GetPackageInfoAsync(string packageId, bool includePrerelease = false)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return null;

            List<NuGetPackageInfo> results = await SearchAsync(packageId, 10, includePrerelease);
            return results.FirstOrDefault(r => string.Equals(r.Id, packageId, StringComparison.OrdinalIgnoreCase))
                   ?? results.FirstOrDefault();
        }

        public virtual async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, bool includePrerelease = false)
        {
            if (string.IsNullOrWhiteSpace(packageId))
                return Array.Empty<string>();

            var resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            var cache = new SourceCacheContext();
            IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(
                packageId,
                cache,
                NullLogger.Instance,
                CancellationToken.None);

            IEnumerable<string> normalized = versions.Select(v => v.ToNormalizedString());
            if (!includePrerelease)
                normalized = normalized.Where(v => NuGetVersion.TryParse(v, out var nv) && !nv.IsPrerelease);

            return normalized.ToList();
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
        /// Нужен для README панели деталей: раньше путь был жёстко зашит на api.nuget.org, поэтому для
        /// приватного, корпоративного или offline-источника README не появлялся никогда.
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

            FindPackageByIdResource resource = await _repository.GetResourceAsync<FindPackageByIdResource>();
            using (var cache = new SourceCacheContext())
            using (var stream = new MemoryStream())
            {
                bool copied = await resource.CopyNupkgToStreamAsync(
                    packageId,
                    parsed,
                    stream,
                    cache,
                    NullLogger.Instance,
                    cancellationToken);

                return copied && stream.Length > 0 ? stream.ToArray() : null;
            }
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
