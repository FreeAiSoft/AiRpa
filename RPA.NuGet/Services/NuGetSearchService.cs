using System;
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
    /// </summary>
    public class NuGetSearchService
    {
        private readonly SourceRepository _repository;

        public NuGetSearchService(string sourceUrl = "https://api.nuget.org/v3/index.json")
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
                throw new ArgumentException("Source URL is required.", nameof(sourceUrl));

            _repository = Repository.Factory.GetCoreV3(sourceUrl);
        }

        public async Task<List<NuGetPackageInfo>> SearchAsync(string query, int take = 20, bool includePrerelease = false)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<NuGetPackageInfo>();

            var searchResource = await _repository.GetResourceAsync<PackageSearchResource>();
            var filter = new SearchFilter(includePrerelease: includePrerelease);
            IEnumerable<IPackageSearchMetadata> results = await searchResource.SearchAsync(
                query,
                filter,
                skip: 0,
                take: take,
                NullLogger.Instance,
                CancellationToken.None);

            return results.Select(m => new NuGetPackageInfo
            {
                Id = m.Identity?.Id,
                Version = m.Identity?.Version?.ToNormalizedString(),
                Description = m.Description,
                Authors = m.Authors,
                TotalDownloads = m.DownloadCount ?? 0
            }).ToList();
        }

        public async Task<IReadOnlyList<string>> GetVersionsAsync(string packageId, bool includePrerelease = false)
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
    }
}
