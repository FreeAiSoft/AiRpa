using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Читает метаданные и зависимости <c>.nupkg</c> и распаковывает пакет (legacy-раскладка
    /// <c>packages\{id}.{version}</c>) через NuGet.Packaging.
    /// <para>
    /// Сеть сюда больше не входит: скачивание выполняется только через выбранный источник в
    /// <see cref="NuGetSearchService"/>, без фолбэка на nuget.org.
    /// </para>
    /// </summary>
    public class NuGetPackageDownloader
    {
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

        /// <summary>
        /// Читает зависимости пакета из <c>.nuspec</c>, выбрав группу, подходящую целевому
        /// фреймворку проекта. Нужен менеджеру пакетов, чтобы ставить транзитивные зависимости
        /// для <c>packages.config</c>-проектов так, как это делает NuGet restore.
        /// </summary>
        public IReadOnlyList<NuGetPackageDependency> ReadDependencies(byte[] nupkg, string targetFramework)
        {
            if (nupkg == null || nupkg.Length == 0)
                return Array.Empty<NuGetPackageDependency>();

            using (var stream = new MemoryStream(nupkg))
            using (var reader = new PackageArchiveReader(stream))
            {
                NuspecReader nuspec = reader.NuspecReader;
                PackageDependencyGroup group = SelectDependencyGroup(nuspec.GetDependencyGroups(), targetFramework);
                if (group == null)
                    return Array.Empty<NuGetPackageDependency>();

                return group.Packages
                    .Where(package => package != null && !string.IsNullOrWhiteSpace(package.Id))
                    .Select(package => new NuGetPackageDependency
                    {
                        Id = package.Id,
                        VersionRange = package.VersionRange?.ToNormalizedString() ?? string.Empty
                    })
                    .ToList();
            }
        }

        private static PackageDependencyGroup SelectDependencyGroup(
            IEnumerable<PackageDependencyGroup> groups,
            string targetFramework)
        {
            List<PackageDependencyGroup> list = (groups ?? Enumerable.Empty<PackageDependencyGroup>()).ToList();
            if (list.Count == 0)
                return null;

            NuGetFramework project = ParseFramework(targetFramework);
            if (project != null)
            {
                PackageDependencyGroup exact = list.FirstOrDefault(group =>
                    group.TargetFramework != null &&
                    string.Equals(group.TargetFramework.GetShortFolderName(), project.GetShortFolderName(), StringComparison.OrdinalIgnoreCase));
                if (exact != null)
                    return exact;

                List<NuGetFramework> candidates = list
                    .Where(group => group.TargetFramework != null)
                    .Select(group => group.TargetFramework)
                    .ToList();
                if (candidates.Count > 0)
                {
                    try
                    {
                        NuGetFramework nearest = new FrameworkReducer().GetNearest(project, candidates);
                        if (nearest != null)
                        {
                            PackageDependencyGroup match = list.FirstOrDefault(group =>
                                group.TargetFramework != null && group.TargetFramework.Equals(nearest));
                            if (match != null)
                                return match;
                        }
                    }
                    catch (Exception)
                    {
                        // Подбор фреймворка — best-effort: нестандартный TFM не должен ломать установку.
                    }
                }
            }

            PackageDependencyGroup any = list.FirstOrDefault(group => group.TargetFramework == null || group.TargetFramework.IsAny);
            return any ?? list[0];
        }

        private static NuGetFramework ParseFramework(string targetFramework)
        {
            if (string.IsNullOrWhiteSpace(targetFramework))
                return null;

            try
            {
                return NuGetFramework.ParseFolder(targetFramework);
            }
            catch (Exception)
            {
                return null;
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
