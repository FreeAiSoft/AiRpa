using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RPA.NuGet.Models;

namespace RPA.NuGet.Services
{
    /// <summary>
    /// Reads and writes the NuGet package references of a project.
    /// Supports legacy <c>packages.config</c> projects and SDK-style
    /// <c>PackageReference</c> projects.
    /// </summary>
    public static class NuGetProjectConfig
    {
        public static NuGetProjectPackages Load(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentException("Project path is required.", nameof(projectPath));

            string fullPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Project file not found.", fullPath);

            bool isSdk = IsSdkStyle(fullPath);

            var project = new NuGetProjectPackages
            {
                ProjectPath = fullPath,
                IsSdkStyle = isSdk
            };

            if (isSdk)
            {
                XDocument document = XDocument.Load(fullPath);
                foreach (XElement packageRef in document.Root.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference"))
                {
                    string id = (string)packageRef.Attribute("Include");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    project.Packages.Add(new NuGetPackageReference
                    {
                        Id = id,
                        Version = (string)packageRef.Attribute("Version")
                    });
                }
            }
            else
            {
                project.PackagesConfigPath = GetPackagesConfigPath(fullPath);
                if (File.Exists(project.PackagesConfigPath))
                {
                    XDocument document = XDocument.Load(project.PackagesConfigPath);
                    foreach (XElement package in document.Root.Elements().Where(e => e.Name.LocalName == "package"))
                    {
                        string id = (string)package.Attribute("id");
                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        project.Packages.Add(new NuGetPackageReference
                        {
                            Id = id,
                            Version = (string)package.Attribute("version"),
                            TargetFramework = (string)package.Attribute("targetFramework")
                        });
                    }
                }
            }

            return project;
        }

        public static void Save(NuGetProjectPackages project)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(project.ProjectPath))
                throw new InvalidOperationException("Project path is not set.");

            if (project.IsSdkStyle)
                SaveSdkStyle(project);
            else
                SavePackagesConfig(project);
        }

        public static void Add(NuGetProjectPackages project, string id, string version)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version is required.", nameof(version));

            var existing = Find(project, id);
            if (existing != null)
            {
                existing.Version = version;
                return;
            }

            project.Packages.Add(new NuGetPackageReference { Id = id, Version = version });
        }

        public static void Remove(NuGetProjectPackages project, string id)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Package id is required.", nameof(id));

            project.Packages.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void Update(NuGetProjectPackages project, string id, string version)
        {
            if (project == null)
                throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException("Package version is required.", nameof(version));

            var existing = Find(project, id);
            if (existing != null)
            {
                existing.Version = version;
                return;
            }

            project.Packages.Add(new NuGetPackageReference { Id = id, Version = version });
        }

        public static NuGetPackageReference Find(NuGetProjectPackages project, string id)
        {
            if (project == null)
                return null;

            return project.Packages.FirstOrDefault(
                p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static bool Contains(NuGetProjectPackages project, string id)
        {
            return Find(project, id) != null;
        }

        public static bool IsSdkStyle(string projectPath)
        {
            XDocument document = XDocument.Load(projectPath);
            XElement root = document.Root;
            return root != null
                   && string.Equals(root.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase)
                   && root.Attribute("Sdk") != null;
        }

        public static string GetPackagesConfigPath(string projectPath)
        {
            return Path.Combine(Path.GetDirectoryName(projectPath) ?? string.Empty, "packages.config");
        }

        private static void SaveSdkStyle(NuGetProjectPackages project)
        {
            XDocument document = XDocument.Load(project.ProjectPath, LoadOptions.PreserveWhitespace);
            XElement root = document.Root;
            if (root == null)
                throw new InvalidDataException("Project file is empty.");

            var wanted = new HashSet<string>(project.Packages.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

            List<XElement> packageRefs = root.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .ToList();

            foreach (XElement packageRef in packageRefs)
            {
                string id = (string)packageRef.Attribute("Include");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (wanted.Contains(id))
                {
                    var target = Find(project, id);
                    packageRef.SetAttributeValue("Version", target?.Version);
                    wanted.Remove(id);
                }
                else
                {
                    packageRef.Remove();
                }
            }

            var newRefs = project.Packages
                .Where(p => wanted.Contains(p.Id))
                .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (newRefs.Count > 0)
            {
                XElement itemGroup = root.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ItemGroup"
                                         && e.Elements().Any(x => x.Name.LocalName == "PackageReference"));

                if (itemGroup == null)
                {
                    itemGroup = new XElement("ItemGroup");
                    root.Add(itemGroup);
                }

                foreach (var package in newRefs)
                {
                    itemGroup.Add(new XElement("PackageReference",
                        new XAttribute("Include", package.Id),
                        new XAttribute("Version", package.Version ?? string.Empty)));
                }
            }

            document.Save(project.ProjectPath);
        }

        private static void SavePackagesConfig(NuGetProjectPackages project)
        {
            string configPath = project.PackagesConfigPath ?? GetPackagesConfigPath(project.ProjectPath);
            project.PackagesConfigPath = configPath;

            XDocument document;
            XElement packages;
            if (File.Exists(configPath))
            {
                document = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
                packages = document.Root;
                if (packages == null || packages.Name.LocalName != "packages")
                    throw new InvalidDataException("Invalid packages.config: root <packages> element missing.");

                packages.Elements().Where(e => e.Name.LocalName == "package").Remove();
            }
            else
            {
                document = new XDocument();
                packages = new XElement("packages");
                document.Add(packages);
            }

            string defaultTargetFramework = project.Packages
                .Where(p => !string.IsNullOrWhiteSpace(p.TargetFramework))
                .Select(p => p.TargetFramework)
                .FirstOrDefault() ?? "net48";

            foreach (var package in project.Packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                packages.Add(new XElement("package",
                    new XAttribute("id", package.Id),
                    new XAttribute("version", package.Version ?? string.Empty),
                    new XAttribute("targetFramework", string.IsNullOrWhiteSpace(package.TargetFramework)
                        ? defaultTargetFramework
                        : package.TargetFramework)));
            }

            document.Save(configPath);
        }
    }
}


