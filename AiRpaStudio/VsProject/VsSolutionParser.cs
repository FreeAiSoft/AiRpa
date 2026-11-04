using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace RPA.VsProject
{
    /// <summary>
    /// Parses Visual Studio solution (.sln) files: project entries, solution folders, nesting,
    /// configurations and the solution file generation (CP-06).
    /// </summary>
    public static class VsSolutionParser
    {
        /// <summary>Project type GUID of a solution folder.</summary>
        public const string SolutionFolderTypeGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}";

        private const string FormatVersionPrefix = "Microsoft Visual Studio Solution File, Format Version";
        private const string VisualStudioVersionPrefix = "# Visual Studio Version";

        private static readonly Regex ProjectLineRegex = new Regex(
            @"^\s*Project\(""(\{[^}]+\})""\)\s*=\s*""([^""]+)"",\s*""([^""]+)"",\s*""(\{[^}]+\})""",
            RegexOptions.Compiled);

        private static readonly Regex ConfigurationLineRegex = new Regex(
            @"^\s*(\{[^}]+\})\.([^.]+)\.(ActiveCfg|Build\.0)\s*=\s*(.+?)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex NestedProjectLineRegex = new Regex(
            @"^\s*(\{[^}]+\})\s*=\s*(\{[^}]+\})\s*$",
            RegexOptions.Compiled);

        public static VsSolution Parse(string solutionPath)
        {
            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                throw new ArgumentException("Solution path is required.", nameof(solutionPath));
            }

            string fullPath = Path.GetFullPath(solutionPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Solution file not found.", fullPath);
            }

            VsSolution solution = new VsSolution
            {
                SolutionPath = fullPath,
                Name = Path.GetFileNameWithoutExtension(fullPath)
            };

            var nestedProjects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string extensibilityVersion = null;
            string section = null;

            foreach (string rawLine in File.ReadAllLines(fullPath))
            {
                string line = rawLine.Trim();

                if (line.StartsWith(FormatVersionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    solution.FormatVersion = line.Substring(FormatVersionPrefix.Length).TrimStart(' ', ',');
                    continue;
                }

                if (line.StartsWith(VisualStudioVersionPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    solution.VisualStudioVersion = MajorVersion(line.Substring(VisualStudioVersionPrefix.Length));
                    continue;
                }

                Match projectMatch = ProjectLineRegex.Match(line);
                if (projectMatch.Success)
                {
                    string typeGuid = projectMatch.Groups[1].Value;
                    string relativePath = projectMatch.Groups[3].Value;

                    solution.Projects.Add(new VsSolutionProject
                    {
                        TypeGuid = typeGuid,
                        Name = projectMatch.Groups[2].Value,
                        RelativePath = relativePath,
                        ProjectGuid = projectMatch.Groups[4].Value,
                        IsSolutionFolder = string.Equals(typeGuid, SolutionFolderTypeGuid, StringComparison.OrdinalIgnoreCase)
                    });

                    continue;
                }

                if (line.StartsWith("GlobalSection(", StringComparison.OrdinalIgnoreCase))
                {
                    int end = line.IndexOf(')');
                    section = end > 0 ? line.Substring("GlobalSection(".Length, end - "GlobalSection(".Length) : null;
                    continue;
                }

                if (line.StartsWith("EndGlobalSection", StringComparison.OrdinalIgnoreCase))
                {
                    section = null;
                    continue;
                }

                if (section == null || line.Length == 0)
                {
                    continue;
                }

                if (string.Equals(section, "SolutionConfigurationPlatforms", StringComparison.OrdinalIgnoreCase))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0)
                    {
                        AddConfiguration(solution, line.Substring(0, equals).Trim());
                    }

                    continue;
                }

                if (string.Equals(section, "ProjectConfigurationPlatforms", StringComparison.OrdinalIgnoreCase))
                {
                    Match configurationMatch = ConfigurationLineRegex.Match(line);

                    if (configurationMatch.Success
                        && string.Equals(configurationMatch.Groups[3].Value, "ActiveCfg", StringComparison.OrdinalIgnoreCase))
                    {
                        VsSolutionProject project = FindProject(solution, configurationMatch.Groups[1].Value);

                        if (project != null)
                        {
                            project.Configurations[configurationMatch.Groups[2].Value.Trim()] = configurationMatch.Groups[4].Value;
                        }
                    }

                    continue;
                }

                if (string.Equals(section, "NestedProjects", StringComparison.OrdinalIgnoreCase))
                {
                    Match nestedMatch = NestedProjectLineRegex.Match(line);

                    if (nestedMatch.Success)
                    {
                        nestedProjects[nestedMatch.Groups[1].Value] = nestedMatch.Groups[2].Value;
                    }

                    continue;
                }

                if (string.Equals(section, "ExtensibilityGlobals", StringComparison.OrdinalIgnoreCase))
                {
                    int equals = line.IndexOf('=');

                    if (equals > 0 && line.Substring(0, equals).Trim().Equals("VisualStudioVersion", StringComparison.OrdinalIgnoreCase))
                    {
                        extensibilityVersion = MajorVersion(line.Substring(equals + 1));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(solution.VisualStudioVersion))
            {
                solution.VisualStudioVersion = extensibilityVersion;
            }

            solution.RequiredToolchain = DescribeToolchain(solution.VisualStudioVersion);

            string solutionDirectory = Path.GetDirectoryName(fullPath) ?? ".";

            foreach (VsSolutionProject project in solution.Projects)
            {
                if (nestedProjects.TryGetValue(project.ProjectGuid ?? string.Empty, out string parentGuid))
                {
                    project.ParentGuid = parentGuid;
                }

                if (project.IsSolutionFolder)
                {
                    continue;
                }

                if (!IsCsproj(project))
                {
                    continue;
                }

                try
                {
                    project.FullPath = Path.GetFullPath(Path.Combine(solutionDirectory, project.RelativePath));
                    project.FileExists = File.Exists(project.FullPath);
                }
                catch (Exception ex)
                {
                    project.FileExists = false;
                    solution.Warnings.Add("не удалось разрешить путь проекта «" + project.Name + "»: " + ex.Message);
                    continue;
                }

                if (!project.FileExists)
                {
                    // Раньше такой проект молча исчезал из дерева (так «спрятались» ..\Activities.WinUI, TD-12).
                    solution.Warnings.Add("файл проекта не найден: " + project.RelativePath);
                }
            }

            return solution;
        }

        /// <summary>True if the project entry points to a C# project file.</summary>
        public static bool IsCsproj(VsSolutionProject project)
        {
            return project != null && !string.IsNullOrWhiteSpace(project.RelativePath) && project.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Human-readable toolchain for a solution generation ("17" → Visual Studio 2022, "18" → 2026).</summary>
        public static string DescribeToolchain(string visualStudioVersion)
        {
            switch (visualStudioVersion)
            {
                case "17":
                    return "Visual Studio 2022";
                case "18":
                    return "Visual Studio 2026";
                default:
                    return null;
            }
        }

        /// <summary>Maps the solution generation to a toolchain; null when the version is unknown.</summary>
        public static Agent.Core.Services.MsBuildToolchain? ToolchainFor(string visualStudioVersion)
        {
            switch (visualStudioVersion)
            {
                case "17":
                    return Agent.Core.Services.MsBuildToolchain.Vs2022;
                case "18":
                    return Agent.Core.Services.MsBuildToolchain.Vs2026;
                default:
                    return null;
            }
        }

        private static void AddConfiguration(VsSolution solution, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string[] parts = value.Split('|');

            if (parts.Length != 2)
            {
                return;
            }

            solution.Configurations.Add(new VsSolutionConfiguration
            {
                Name = parts[0].Trim(),
                Platform = parts[1].Trim()
            });
        }

        private static VsSolutionProject FindProject(VsSolution solution, string projectGuid)
        {
            foreach (VsSolutionProject project in solution.Projects)
            {
                if (string.Equals(project.ProjectGuid, projectGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }
            }

            return null;
        }

        private static string MajorVersion(string raw)
        {
            string value = (raw ?? string.Empty).Trim();

            if (value.Length == 0)
            {
                return null;
            }

            int dot = value.IndexOf('.');
            string major = dot > 0 ? value.Substring(0, dot) : value;

            return major.Trim();
        }
    }
}
