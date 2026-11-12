using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RPA.ProjectModel;

namespace RPA.VsProject
{
    /// <summary>
    /// Parses Visual Studio project (.csproj) files — both legacy (ToolsVersion + TargetFrameworkVersion)
    /// and SDK-style (Sdk attribute, &lt;Sdk Name="..."/&gt; element, &lt;Import Sdk="..."/&gt;), including
    /// multi-targeting and packages declared by <c>PackageReference</c> or <c>packages.config</c> (CP-05).
    /// </summary>
    public static class VsProjectParser
    {
        /// <summary>Project type GUID of a Windows Workflow Foundation (activity library) project.</summary>
        public const string WorkflowProjectTypeGuid = "{32f31d43-81cc-4c15-9de6-3fc5453562b6}";

        private const string PackagesConfigFileName = "packages.config";
        private const string CentralPackageVersionsFileName = "Directory.Packages.props";

        public static VsProjectInfo Parse(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("Project path is required.", nameof(projectPath));
            }

            string fullPath = Path.GetFullPath(projectPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Project file not found.", fullPath);
            }

            XDocument document = XDocument.Load(fullPath);
            XElement root = document.Root ?? throw new InvalidDataException("Project file is empty.");

            VsProjectInfo info = new VsProjectInfo
            {
                ProjectPath = fullPath,
                IsSdkStyle = DetectSdkStyle(root),
                RootNamespace = FirstElementValue(root, "RootNamespace"),
                AssemblyName = FirstElementValue(root, "AssemblyName"),
                TargetFrameworkVersion = FirstElementValue(root, "TargetFrameworkVersion"),
                TargetFramework = FirstElementValue(root, "TargetFramework"),
                ProjectGuid = FirstElementValue(root, "ProjectGuid"),
                OutputType = FirstElementValue(root, "OutputType")
            };

            info.TargetFrameworks = ReadTargetFrameworks(root);
            info.IsWorkflowProject = IsWorkflowProject(root);

            foreach (XElement itemGroup in root.Descendants().Where(e => e.Name.LocalName == "ItemGroup"))
            {
                foreach (XElement item in itemGroup.Elements())
                {
                    string itemType = item.Name.LocalName;
                    string include = (string)item.Attribute("Include");
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        include = (string)item.Attribute("Update");
                    }

                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }

                    XElement hintPath = item.Elements().FirstOrDefault(e => e.Name.LocalName == "HintPath");

                    info.Items.Add(new VsProjectItem
                    {
                        ItemType = itemType,
                        Include = include,
                        HintPath = hintPath?.Value
                    });
                }
            }

            ReadPackages(root, info);

            info.Name = string.IsNullOrWhiteSpace(info.AssemblyName)
                ? Path.GetFileNameWithoutExtension(fullPath)
                : info.AssemblyName;

            return info;
        }

        /// <summary>Normalizes a declared target framework: "v4.7.2" → "net472", "net48" → "net48".</summary>
        public static string NormalizeTargetFramework(string raw)
        {
            string value = (raw ?? string.Empty).Trim();

            if (value.Length == 0)
            {
                return null;
            }

            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(1);
            }

            if (value.All(c => char.IsDigit(c) || c == '.'))
            {
                return value.StartsWith("4.", StringComparison.Ordinal)
                    ? "net" + value.Replace(".", string.Empty)
                    : "net" + value;
            }

            return value.ToLowerInvariant();
        }

        /// <summary>
        /// Reads the SDK-style marker in all three supported shapes. Legacy projects (ToolsVersion +
        /// Microsoft.CSharp.targets) return false.
        /// </summary>
        private static bool DetectSdkStyle(XElement root)
        {
            if (!string.Equals(root.Name.LocalName, "Project", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace((string)root.Attribute("Sdk")))
            {
                return true;
            }

            // <Project><Sdk Name="Microsoft.NET.Sdk" /></Project>
            if (root.Elements().Any(e => string.Equals(e.Name.LocalName, "Sdk", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
            bool sdkImport = root.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase))
                .Any(e => !string.IsNullOrWhiteSpace((string)e.Attribute("Sdk"))
                          || EndsWithSdkProject((string)e.Attribute("Project")));

            if (sdkImport)
            {
                return true;
            }

            // Multi-targeting is SDK-only.
            return root.Descendants().Any(e => string.Equals(e.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase));
        }

        private static bool EndsWithSdkProject(string importProject)
        {
            if (string.IsNullOrWhiteSpace(importProject))
            {
                return false;
            }

            return importProject.EndsWith("Sdk.props", StringComparison.OrdinalIgnoreCase)
                   || importProject.EndsWith("Sdk.targets", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWorkflowProject(XElement root)
        {
            string guids = FirstElementValue(root, "ProjectTypeGuids");

            return !string.IsNullOrWhiteSpace(guids)
                   && guids.IndexOf(WorkflowProjectTypeGuid.Trim('{', '}'), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> ReadTargetFrameworks(XElement root)
        {
            var tfms = new List<string>();

            foreach (XElement element in root.Descendants())
            {
                if (element.Name.LocalName == "TargetFrameworks" || element.Name.LocalName == "TargetFramework")
                {
                    foreach (string value in element.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        AddTfm(tfms, value);
                    }
                }
                else if (element.Name.LocalName == "TargetFrameworkVersion")
                {
                    AddTfm(tfms, element.Value);
                }
            }

            return tfms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddTfm(List<string> tfms, string raw)
        {
            string normalized = NormalizeTargetFramework(raw);

            if (normalized != null && !tfms.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                tfms.Add(normalized);
            }
        }

        /// <summary>Reads <c>PackageReference</c> items and, for legacy projects, <c>packages.config</c>.</summary>
        private static void ReadPackages(XElement root, VsProjectInfo info)
        {
            Dictionary<string, string> centralVersions = null;

            foreach (XElement reference in root.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                string id = ((string)reference.Attribute("Include") ?? (string)reference.Attribute("Update"))?.Trim();

                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                string version = ((string)reference.Attribute("Version"))?.Trim();

                if (string.IsNullOrWhiteSpace(version))
                {
                    version = reference.Elements()
                        .FirstOrDefault(e => e.Name.LocalName == "Version")?.Value?.Trim();
                }

                if (string.IsNullOrWhiteSpace(version))
                {
                    centralVersions = centralVersions ?? ReadCentralPackageVersions(info.ProjectPath);

                    if (centralVersions.TryGetValue(id, out string central))
                    {
                        info.Packages.Add(new VsProjectPackage
                        {
                            Id = id,
                            Version = central,
                            Source = VsPackageSource.PackageReference,
                            IsCentralVersion = true
                        });

                        continue;
                    }
                }

                info.Packages.Add(new VsProjectPackage
                {
                    Id = id,
                    Version = string.IsNullOrWhiteSpace(version) ? null : version,
                    Source = VsPackageSource.PackageReference
                });
            }

            ReadPackagesConfig(info);
        }

        private static void ReadPackagesConfig(VsProjectInfo info)
        {
            string directory = Path.GetDirectoryName(info.ProjectPath);

            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string path = Path.Combine(directory, PackagesConfigFileName);

            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                XDocument document = XDocument.Load(path);

                foreach (XElement package in document.Descendants().Where(e => e.Name.LocalName == "package"))
                {
                    string id = ((string)package.Attribute("id"))?.Trim();

                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    info.Packages.Add(new VsProjectPackage
                    {
                        Id = id,
                        Version = ((string)package.Attribute("version"))?.Trim(),
                        TargetFramework = ((string)package.Attribute("targetFramework"))?.Trim(),
                        Source = VsPackageSource.PackagesConfig
                    });
                }
            }
            catch (Exception)
            {
                // A broken packages.config must not hide the project: the restore pass will report it.
            }
        }

        /// <summary>Reads central package versions from the nearest <c>Directory.Packages.props</c>.</summary>
        private static Dictionary<string, string> ReadCentralPackageVersions(string projectPath)
        {
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(projectPath) ?? ".");

            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, CentralPackageVersionsFileName);

                if (File.Exists(candidate))
                {
                    try
                    {
                        XDocument document = XDocument.Load(candidate);

                        foreach (XElement element in document.Descendants().Where(e => e.Name.LocalName == "PackageVersion"))
                        {
                            string id = ((string)element.Attribute("Include"))?.Trim();
                            string version = ((string)element.Attribute("Version"))?.Trim();

                            if (string.IsNullOrWhiteSpace(version))
                            {
                                version = element.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value?.Trim();
                            }

                            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                            {
                                versions[id] = version;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore a broken central props file: versions simply stay unresolved.
                    }

                    break;
                }

                directory = directory.Parent;
            }

            return versions;
        }

        private static string FirstElementValue(XElement root, string localName)
        {
            XElement element = root.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
            return element?.Value.Trim();
        }
    }
}
