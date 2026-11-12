using System.Collections.Generic;
using RPA.ProjectModel;

namespace RPA.VsProject
{
    /// <summary>A project entry in a .sln file.</summary>
    public class VsSolutionProject
    {
        public string TypeGuid { get; set; }
        public string Name { get; set; }
        public string RelativePath { get; set; }
        public string ProjectGuid { get; set; }

        /// <summary>True for solution folders (GUID {2150E333-...}): they have no project file.</summary>
        public bool IsSolutionFolder { get; set; }

        /// <summary>GUID of the parent solution folder (from <c>NestedProjects</c>), null for root entries.</summary>
        public string ParentGuid { get; set; }

        /// <summary>Full path resolved against the solution directory (null for solution folders).</summary>
        public string FullPath { get; set; }

        /// <summary>False when the entry is not a C# project or the file is missing on disk.</summary>
        public bool FileExists { get; set; }

        /// <summary>Configuration mappings of this project, e.g. "Debug|Any CPU" -> "Debug|Any CPU".</summary>
        public Dictionary<string, string> Configurations { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>A build configuration declared by the solution (e.g. Debug|x64).</summary>
    public class VsSolutionConfiguration
    {
        public string Name { get; set; }
        public string Platform { get; set; }

        public override string ToString()
        {
            return Name + "|" + Platform;
        }
    }

    /// <summary>Parsed .sln file.</summary>
    public class VsSolution
    {
        public string SolutionPath { get; set; }
        public string Name { get; set; }

        /// <summary>"Microsoft Visual Studio Solution File, Format Version 12.00" as declared in the header.</summary>
        public string FormatVersion { get; set; }

        /// <summary>Solution file generation, e.g. "17" (VS 2022) or "18" (VS 2026).</summary>
        public string VisualStudioVersion { get; set; }

        /// <summary>Toolchain the solution was saved by: drives the MSBuild recommendation (CP-06).</summary>
        public string RequiredToolchain { get; set; }

        public List<VsSolutionProject> Projects { get; set; } = new List<VsSolutionProject>();

        /// <summary>Solution configurations (from <c>SolutionConfigurationPlatforms</c>).</summary>
        public List<VsSolutionConfiguration> Configurations { get; set; } = new List<VsSolutionConfiguration>();

        /// <summary>Non-fatal parse problems: a missing project file, an unreadable section, ...</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Parsed .csproj file.</summary>
    public class VsProjectInfo
    {
        public string ProjectPath { get; set; }
        public string Name { get; set; }
        public string RootNamespace { get; set; }
        public string AssemblyName { get; set; }
        public string TargetFrameworkVersion { get; set; }
        public string TargetFramework { get; set; }
        public string ProjectGuid { get; set; }
        public string OutputType { get; set; }

        /// <summary>
        /// True for SDK-style projects — detected through any of the three supported forms:
        /// <c>Sdk</c> attribute, <c>&lt;Sdk Name="..."/&gt;</c> element or <c>&lt;Import Sdk="..."/&gt;</c>.
        /// </summary>
        public bool IsSdkStyle { get; set; }

        /// <summary>Normalized target frameworks ("net472", "net48", "netstandard2.0"), multi-targeting supported.</summary>
        public List<string> TargetFrameworks { get; set; } = new List<string>();

        /// <summary>Packages declared by <c>PackageReference</c> or <c>packages.config</c>.</summary>
        public List<VsProjectPackage> Packages { get; set; } = new List<VsProjectPackage>();

        /// <summary>True when the project file contains the Windows Workflow Foundation project type GUID.</summary>
        public bool IsWorkflowProject { get; set; }

        public List<VsProjectItem> Items { get; set; } = new List<VsProjectItem>();

        /// <summary>True when a restore step is required before the build.</summary>
        public bool NeedsRestore
        {
            get { return Packages.Count > 0; }
        }

        /// <summary>The effective TFM for a single-targeting project (null when multi-targeting or unknown).</summary>
        public string PrimaryTargetFramework
        {
            get { return TargetFrameworks.Count == 1 ? TargetFrameworks[0] : null; }
        }
    }

    /// <summary>Result of a build.</summary>
    public class VsBuildResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string ProjectPath { get; set; }
        public string Output { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Full path to the MSBuild.exe that was used (null when the lookup failed).</summary>
        public string MsBuildPath { get; set; }

        /// <summary>Toolchain name, e.g. "Visual Studio 2026".</summary>
        public string ToolchainName { get; set; }

        /// <summary>MSBuild.exe file version, e.g. "18.10.1.42706".</summary>
        public string ToolchainVersion { get; set; }

        /// <summary>True when the requested toolchain is not installed and another one was used instead.</summary>
        public bool ToolchainFallback { get; set; }

        /// <summary>True when a restore pass was executed before the build (CP-05).</summary>
        public bool RestoreAttempted { get; set; }

        /// <summary>True when restore succeeded (false when it was not attempted or failed).</summary>
        public bool RestoreSucceeded { get; set; }

        /// <summary>Output of the restore pass.</summary>
        public string RestoreOutput { get; set; }
    }
}
