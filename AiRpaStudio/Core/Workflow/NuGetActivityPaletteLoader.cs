using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RPA.Core.Generation;
using RPA.NuGet.Models;
using RPA.NuGet.Services;

namespace RPA.Core.Workflow
{
    /// <summary>
    /// Loads activities from installed NuGet packages into the studio palette without a restart.
    /// <para>
    /// After install (and restore for PackageReference) the assemblies live either in
    /// packages\&lt;id&gt;.&lt;version&gt;\lib\&lt;tfm&gt; (packages.config) or in the global NuGet folder
    /// %NUGET_PACKAGES% / %USERPROFILE%\.nuget\packages\&lt;id&gt;\&lt;version&gt;\lib\&lt;tfm&gt;. Every DLL is
    /// inspected for Activity subclasses and registered in StudioActivityPalette.
    /// </para>
    /// <para>
    /// Package assemblies often depend on sibling assemblies shipped in the same lib folder
    /// (ActivityLibraryBase.dll, RpaCommon.dll) or on transitive packages. A temporary
    /// AssemblyResolve handler loads those dependencies while the palette is scanned.
    /// </para>
    /// </summary>
    internal sealed class NuGetActivityPaletteLoader
    {
        private sealed class PackageLib
        {
            public string LibDirectory;
        }

        /// <summary>
        /// Результат пересинхронизации пакетных активностей: сколько типов добавилось и сколько
        /// исчезло относительно предыдущего набора пакетных активностей палитры.
        /// </summary>
        internal sealed class NuGetPaletteScanResult
        {
            internal NuGetPaletteScanResult(int added, int removed)
            {
                Added = added;
                Removed = removed;
            }

            /// <summary>Число типов активностей, добавленных в палитру.</summary>
            internal int Added { get; }

            /// <summary>Число типов активностей, удалённых из палитры.</summary>
            internal int Removed { get; }

            /// <summary>Состав пакетных активностей изменился (добавление или удаление).</summary>
            internal bool Changed => Added != 0 || Removed != 0;
        }

        /// <summary>Сканирует пакеты проекта и пересинхронизирует пакетные активности палитры.</summary>
        internal NuGetPaletteScanResult LoadFromProject(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
                return Empty;

            NuGetProjectPackages project;
            try
            {
                project = NuGetProjectConfig.Load(projectPath);
            }
            catch (Exception)
            {
                return Empty;
            }

            if (project == null)
                return Empty;

            string projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;

            var targets = new List<PackageLib>();
            foreach (NuGetPackageReference package in project.Packages)
            {
                string libDirectory = FindLibDirectory(project, projectDir, package.Id, package.Version);
                if (libDirectory == null)
                    continue;

                targets.Add(new PackageLib { LibDirectory = libDirectory });
            }

            if (targets.Count == 0)
                return Synchronize(Array.Empty<ActivityPaletteCategory>());

            List<string> searchDirectories = targets.Select(t => t.LibDirectory).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string fallbackRoot = project.IsSdkStyle
                ? GetGlobalPackageRoot()
                : Path.Combine(projectDir, "packages");

            ResolveEventHandler resolver = (sender, args) => ResolveAssembly(args.Name, searchDirectories, fallbackRoot);
            AppDomain.CurrentDomain.AssemblyResolve += resolver;

            var newCategories = new List<ActivityPaletteCategory>();
            try
            {
                // Pre-load every co-located assembly so GetTypes() can resolve base classes and
                // argument types that live in the same lib folder (ActivityLibraryBase, RpaCommon, ...).
                foreach (PackageLib target in targets)
                {
                    foreach (string dllPath in Directory.GetFiles(target.LibDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            _ = Assembly.LoadFrom(dllPath);
                        }
                        catch (Exception)
                        {
                            // A broken/unmanaged assembly in the package must not break the palette.
                        }
                    }
                }

                foreach (PackageLib target in targets)
                {
                    foreach (string dllPath in Directory.GetFiles(target.LibDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                    {
                        ActivityPaletteCategory category = BuildCategory(dllPath);
                        if (category != null)
                        {
                            newCategories.Add(category);
                        }
                    }
                }
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolver;
            }

            return Synchronize(newCategories);
        }

        internal static Assembly ResolveAssembly(string fullName, List<string> searchDirectories, string fallbackRoot)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            string simpleName = fullName.Split(',')[0];

            // Fast path: dependency ships next to the package assemblies.
            foreach (string directory in searchDirectories)
            {
                string candidate = Path.Combine(directory, simpleName + ".dll");
                if (File.Exists(candidate))
                {
                    try
                    {
                        return Assembly.LoadFrom(candidate);
                    }
                    catch (Exception)
                    {
                        // Try the next candidate.
                    }
                }
            }

            // Fallback: transitive dependency lives in another package (packages\ or global cache).
            if (!string.IsNullOrWhiteSpace(fallbackRoot) && Directory.Exists(fallbackRoot))
            {
                try
                {
                    string found = Directory.GetFiles(fallbackRoot, simpleName + ".dll", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (found != null)
                        return Assembly.LoadFrom(found);
                }
                catch (Exception)
                {
                    // Search can fail on permissions or locked files; keep the palette alive.
                }
            }

            return null;
        }

        private static string FindLibDirectory(NuGetProjectPackages project, string projectDir, string id, string version)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
                return null;

            string packageRoot = project.IsSdkStyle
                ? GetGlobalPackageRoot(id, version)
                : Path.Combine(projectDir, "packages", id + "." + version);

            string libRoot = Path.Combine(packageRoot, "lib");
            if (!Directory.Exists(libRoot))
                return null;

            string[] candidates = new[]
            {
                project.TargetFramework,
                "net472",
                "net48",
                "net462",
                "netstandard2.0",
                "netstandard2.1"
            };

            foreach (string candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
            {
                string directory = Path.Combine(libRoot, candidate);
                if (Directory.Exists(directory))
                    return directory;
            }

            // Exotic TFM: take the first folder that actually contains DLLs.
            return Directory.GetDirectories(libRoot)
                .FirstOrDefault(directory =>
                    Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly).Length > 0);
        }

        private static string GetGlobalPackageRoot()
        {
            string root = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget",
                    "packages");
            }

            return root;
        }

        private static string GetGlobalPackageRoot(string id, string version)
        {
            return Path.Combine(GetGlobalPackageRoot(), id.ToLowerInvariant(), version.ToLowerInvariant());
        }

        private static readonly NuGetPaletteScanResult Empty = new NuGetPaletteScanResult(0, 0);

        private static NuGetPaletteScanResult Synchronize(IReadOnlyList<ActivityPaletteCategory> newCategories)
        {
            Type[] before = StudioActivityPalette.PackageCategories()
                .SelectMany(category => category.Types)
                .Distinct()
                .ToArray();

            Type[] after = (newCategories ?? Array.Empty<ActivityPaletteCategory>())
                .SelectMany(category => category.Types)
                .Distinct()
                .ToArray();

            int added = after.Count(type => !before.Contains(type));
            int removed = before.Count(type => !after.Contains(type));

            StudioActivityPalette.ReplacePackageLibraries(newCategories);
            ActivityPaletteRegistry.InvalidateDefault();
            StudioActivityCatalog.InvalidateDefault();

            return new NuGetPaletteScanResult(added, removed);
        }

        private static ActivityPaletteCategory BuildCategory(string dllPath)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(dllPath);
            }
            catch (Exception)
            {
                return null;
            }

            IReadOnlyList<Type> activityTypes = StudioActivityPalette.ActivityTypesOf(assembly);
            if (activityTypes.Count == 0)
                return null;

            // Category = assembly file name without extension (Activities.Browser.dll -> Activities.Browser).
            string category = Path.GetFileNameWithoutExtension(dllPath);
            return new ActivityPaletteCategory(category, activityTypes);
        }
    }
}