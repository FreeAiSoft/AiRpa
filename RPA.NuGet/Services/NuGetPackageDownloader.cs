using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Packaging;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Downloads <c>.nupkg</c> files from the nuget.org flat-container endpoint and
    /// reads package metadata/dependencies via NuGet.Packaging.
    /// </summary>
    public class NuGetPackageDownloader
    {
        private readonly HttpClient _http;

        public NuGetPackageDownloader(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient();
        }

        public async Task<byte[]> DownloadNupkgAsync(string id, string version)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version is required.", nameof(version));

            string lowerId = id.ToLowerInvariant();
            string lowerVersion = version.ToLowerInvariant();
            string url = $"https://api.nuget.org/v3-flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg";

            return await _http.GetByteArrayAsync(url);
        }

        /// <summary>
        /// Extracts a <c>.nupkg</c> into <c>packagesFolder\{id}.{version}</c> (legacy layout),
        /// mirroring what NuGet restore does for packages.config projects.
        /// </summary>
        public void Extract(byte[] nupkg, string id, string version, string packagesFolder)
        {
            string destination = Path.Combine(packagesFolder, $"{id}.{version}");
            Directory.CreateDirectory(destination);

            using (var stream = new MemoryStream(nupkg))
            using (var reader = new PackageArchiveReader(stream))
            {
                foreach (string file in reader.GetFiles())
                {
                    string relative = SanitizeEntryPath(file);
                    if (relative == null)
                        continue;

                    string fullPath = Path.Combine(destination, relative);
                    string dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    using (Stream entry = reader.GetStream(file))
                    using (var output = File.Create(fullPath))
                    {
                        entry.CopyTo(output);
                    }
                }
            }
        }

        /// <summary>
        /// Reads description/authors/dependency groups from a nupkg without extracting it.
        /// </summary>
        public NuGetPackageMetadata ReadMetadata(byte[] nupkg)
        {
            using (var stream = new MemoryStream(nupkg))
            using (var reader = new PackageArchiveReader(stream))
            {
                NuspecReader nuspec = reader.NuspecReader;
                return new NuGetPackageMetadata
                {
                    Id = nuspec.GetId(),
                    Version = nuspec.GetVersion()?.ToNormalizedString(),
                    Description = nuspec.GetDescription(),
                    Authors = string.Join(", ", nuspec.GetAuthors())
                };
            }
        }

        private static string SanitizeEntryPath(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
                return null;

            string normalized = entry.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains(".."))
                return null;

            return normalized.Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public class NuGetPackageMetadata
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Authors { get; set; }
    }
}
