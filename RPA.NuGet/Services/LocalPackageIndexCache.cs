using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using NuGet.Versioning;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Одна версия пакета в индексе локального NuGet-источника. Это «сырые» данные, прочитанные
    /// из <c>.nuspec</c> внутри <c>.nupkg</c>: на их основе <see cref="NuGetSearchService"/> строит
    /// карточки <c>NuGetPackageInfo</c> без обращения к NuGet.Protocol.
    /// </summary>
    internal sealed class LocalPackageIndexEntry
    {
        public string Id { get; set; }

        public string Version { get; set; }

        public string Authors { get; set; }

        public string Description { get; set; }

        public string Tags { get; set; }

        public string IconUrl { get; set; }

        public string ProjectUrl { get; set; }

        public string LicenseUrl { get; set; }

        /// <summary>Дата изменения <c>.nupkg</c> — единственный доступный аналог даты публикации.</summary>
        public DateTimeOffset? Published { get; set; }

        public bool IsPrerelease { get; set; }
    }

    /// <summary>Сериализуемый снимок индекса локального источника.</summary>
    internal sealed class LocalPackageIndex
    {
        /// <summary>Версия формата/логики индекса: при её смене старый кэш пересобирается.</summary>
        public int CacheVersion { get; set; }

        /// <summary>Отпечаток набора <c>.nupkg</c>, по которому индекс считается актуальным.</summary>
        public string Fingerprint { get; set; }

        public List<LocalPackageIndexEntry> Entries { get; set; }
    }

    /// <summary>
    /// Индекс локального NuGet-источника (папки с <c>.nupkg</c>). Один раз перебирает каталог,
    /// разбирает <c>.nuspec</c> внутри каждого пакета и строит <c>packageId → версии + метаданные</c>.
    /// <para>
    /// Результат сохраняется на диск (<c>%LOCALAPPDATA%\AiRpaStudio\NuGetIndex\&lt;hash&gt;.json</c>)
    /// и переиспользуется между запусками. Инвалидация — по отпечатку: имена, размеры и
    /// <c>LastWriteTimeUtc</c> всех <c>.nupkg</c> в каталоге. Это убирает ~56-секундный обход
    /// <see cref="NuGet.Protocol.Core.Types.PackageSearchResource"/> для локальной папки.
    /// </para>
    /// </summary>
    internal sealed class LocalPackageIndexCache
    {
        private const int CacheVersion = 1;
        private readonly string _directory;
        private readonly object _sync = new object();
        private LocalPackageIndex _index;

        public LocalPackageIndexCache(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory is required.", nameof(directory));

            _directory = directory;
        }

        /// <summary>Все проиндексированные записи (отсортированы по Id, затем по убыванию версии).</summary>
        public IReadOnlyList<LocalPackageIndexEntry> Entries => GetIndex().Entries;

        private LocalPackageIndex GetIndex()
        {
            lock (_sync)
            {
                if (_index != null)
                    return _index;

                _index = LoadOrBuild();
                return _index;
            }
        }

        private LocalPackageIndex LoadOrBuild()
        {
            string cachePath = GetCachePath(_directory);
            string fingerprint = ComputeFingerprint(_directory);

            if (!string.IsNullOrEmpty(fingerprint))
            {
                try
                {
                    if (File.Exists(cachePath))
                    {
                        LocalPackageIndex cached =
                            JsonConvert.DeserializeObject<LocalPackageIndex>(File.ReadAllText(cachePath));
                        if (cached != null && cached.CacheVersion == CacheVersion
                            && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal)
                            && cached.Entries != null)
                        {
                            NuGetLog.Info("Local package index loaded from cache: " + cachePath);
                            return cached;
                        }
                    }
                }
                catch (Exception ex)
                {
                    NuGetLog.Warn("Failed to load local package index cache; rebuilding. " + ex.Message);
                }
            }

            List<LocalPackageIndexEntry> entries = BuildEntries(_directory);
            var index = new LocalPackageIndex
            {
                CacheVersion = CacheVersion,
                Fingerprint = fingerprint,
                Entries = entries
            };

            if (!string.IsNullOrEmpty(fingerprint))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                    File.WriteAllText(cachePath, JsonConvert.SerializeObject(index, Formatting.None));
                    NuGetLog.Info("Local package index cached: " + cachePath + " (" + entries.Count + " package(s))");
                }
                catch (Exception ex)
                {
                    NuGetLog.Warn("Failed to write local package index cache; continuing in-memory. " + ex.Message);
                }
            }

            return index;
        }

        private static List<LocalPackageIndexEntry> BuildEntries(string directory)
        {
            var entries = new List<LocalPackageIndexEntry>();
            if (!Directory.Exists(directory))
                return entries;

            foreach (string file in Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    LocalPackageIndexEntry entry = ReadNuspec(file);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch (Exception ex)
                {
                    NuGetLog.Warn("Failed to index package " + file + ": " + ex.Message);
                }
            }

            entries.Sort((left, right) =>
            {
                int byId = string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
                if (byId != 0)
                    return byId;

                return CompareVersionsDesc(left.Version, right.Version);
            });

            return entries;
        }

        private static int CompareVersionsDesc(string left, string right)
        {
            bool leftOk = NuGetVersion.TryParse(left, out NuGetVersion leftVersion);
            bool rightOk = NuGetVersion.TryParse(right, out NuGetVersion rightVersion);

            if (leftOk && rightOk)
                return rightVersion.CompareTo(leftVersion);
            if (leftOk)
                return -1;
            if (rightOk)
                return 1;

            return string.Compare(right, left, StringComparison.OrdinalIgnoreCase);
        }

        private static LocalPackageIndexEntry ReadNuspec(string file)
        {
            using (ZipArchive zip = ZipFile.OpenRead(file))
            {
                ZipArchiveEntry nuspec = zip.Entries.FirstOrDefault(entry =>
                    !string.IsNullOrEmpty(entry.Name)
                    && string.Equals(Path.GetExtension(entry.Name), ".nuspec", StringComparison.OrdinalIgnoreCase));
                if (nuspec == null)
                    return null;

                XDocument document;
                using (Stream stream = nuspec.Open())
                    document = XDocument.Load(stream);

                XElement metadata = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "metadata");
                if (metadata == null)
                    return null;

                string id = ElementValue(metadata, "id");
                string version = ElementValue(metadata, "version");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
                    return null;

                bool isPrerelease =
                    NuGetVersion.TryParse(version, out NuGetVersion parsedVersion)
                    && parsedVersion.IsPrerelease;

                var info = new FileInfo(file);
                return new LocalPackageIndexEntry
                {
                    Id = id,
                    Version = version,
                    Authors = ElementValue(metadata, "authors"),
                    Description = ElementValue(metadata, "description"),
                    Tags = ElementValue(metadata, "tags"),
                    IconUrl = ElementValue(metadata, "iconUrl"),
                    ProjectUrl = ElementValue(metadata, "projectUrl"),
                    LicenseUrl = ElementValue(metadata, "licenseUrl"),
                    Published = new DateTimeOffset(info.LastWriteTimeUtc),
                    IsPrerelease = isPrerelease
                };
            }
        }

        private static string ElementValue(XElement parent, string localName)
        {
            XElement element = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
            return element == null ? null : (element.Value ?? string.Empty).Trim();
        }

        private static string ComputeFingerprint(string directory)
        {
            if (!Directory.Exists(directory))
                return null;

            IEnumerable<string> fileSignatures = Directory
                .EnumerateFiles(directory, "*.nupkg", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return info.Name + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                });

            return Hash(string.Join("\n", fileSignatures));
        }

        private static string GetCachePath(string directory)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AiRpaStudio",
                "NuGetIndex");

            string normalized = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return Path.Combine(root, Hash(normalized) + ".json");
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return Convert.ToBase64String(bytes)
                    .Replace('/', '-')
                    .Replace('+', '_')
                    .Replace('=', '-');
            }
        }
    }
}
