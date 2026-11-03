using System.Collections.Generic;

namespace RPA.VsProject
{
    /// <summary>A project entry in a .sln file.</summary>
    public class VsSolutionProject
    {
        public string TypeGuid { get; set; }
        public string Name { get; set; }
        public string RelativePath { get; set; }
        public string ProjectGuid { get; set; }
    }

    /// <summary>Parsed .sln file.</summary>
    public class VsSolution
    {
        public string SolutionPath { get; set; }
        public string Name { get; set; }
        public List<VsSolutionProject> Projects { get; set; } = new List<VsSolutionProject>();
    }

    /// <summary>A single item (Compile, XamlAppDef, Reference, ...) in a .csproj.</summary>
    public class VsProjectItem
    {
        public string ItemType { get; set; }
        public string Include { get; set; }
        public string HintPath { get; set; }
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
        public bool IsSdkStyle { get; set; }
        public List<VsProjectItem> Items { get; set; } = new List<VsProjectItem>();
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
    }
}
