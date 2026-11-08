using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NuGet.Packaging;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Downloads <c>.nupkg</c> files from the nuget.org flat-container endpoint and
    /// reads package metadata/dependencies via NuGet.Packaging.
    /// <para>
    /// Набор страховок тот же, что у <see cref="NuGetIconService"/>: у .NET Framework 4.8 по умолчанию
    /// <c>SystemDefault</c>, и соединение с nuget.org обрывается на записи — поэтому TLS 1.2 включается
    /// явно, запрос повторяется (3 попытки) и ограничен таймаутом. Раньше здесь был «голый»
    /// <see cref="HttpClient"/> без повторов и без диагностики: чтение README пакета молча не работало,
    /// потому что исключение глушилось вызывающим кодом.
    /// </para>
    /// </summary>
    public class NuGetPackageDownloader
    {
        private const int Attempts = 3;
        private const int TimeoutSeconds = 60;

        private readonly HttpClient _http;

        static NuGetPackageDownloader()
        {
            try
            {
                // У .NET Framework 4.8 по умолчанию SystemDefault — TLS 1.2 включаем явно,
                // как это делает NuGet.Protocol.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // Старая ОС без TLS 1.2 — остаётся системное значение.
            }
        }

        public NuGetPackageDownloader(HttpClient httpClient = null)
        {
            _http = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
        }

        /// <summary>
        /// Текст последней ошибки скачивания (диагностика README и установки): панель показывает
        /// причину вместо тихого «ничего не пришло».
        /// </summary>
        public string LastError { get; private set; }

        public virtual async Task<byte[]> DownloadNupkgAsync(string id, string version)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version is required.", nameof(version));

            LastError = null;
            string lowerId = id.ToLowerInvariant();
            string lowerVersion = version.ToLowerInvariant();
            string url = $"https://api.nuget.org/v3-flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg";
            NuGetLog.Info("Download " + url);

            Exception last = null;

            for (int attempt = 1; attempt <= Attempts; attempt++)
            {
                try
                {
                    using (var response = await _http.GetAsync(url).ConfigureAwait(false))
                    {
                        NuGetLog.Debug("HTTP " + (int)response.StatusCode + " for " + url);
                        if (response.IsSuccessStatusCode)
                        {
                            byte[] data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                            if (data.Length > 0)
                                return data;

                            last = new InvalidOperationException("empty package body");
                        }
                        else
                        {
                            last = new InvalidOperationException("HTTP " + (int)response.StatusCode + " for " + url);
                            if (response.StatusCode == HttpStatusCode.NotFound)
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    NuGetLog.Error(ex, "download attempt " + attempt + " failed: " + url);
                    last = ex;
                }

                if (attempt < Attempts)
                    await Task.Delay(200 * attempt).ConfigureAwait(false);
            }

            LastError = last == null ? "unknown error" : last.GetBaseException().Message;
            throw new InvalidOperationException("Failed to download " + id + " " + version + ": " + LastError, last);
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
