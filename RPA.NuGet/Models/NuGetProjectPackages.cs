using System.Collections.Generic;

namespace RPA.NuGet.Models
{
    /// <summary>
    /// NuGet package state of a single project: either the SDK-style
    /// <c>PackageReference</c> items inside the <c>.csproj</c>, or the legacy
    /// <c>packages.config</c> located next to it.
    /// </summary>
    public class NuGetProjectPackages
    {
        /// <summary>Full path to the <c>.csproj</c> file.</summary>
        public string ProjectPath { get; set; }

        /// <summary>Full path to <c>packages.config</c>; <c>null</c> for SDK-style projects.</summary>
        public string PackagesConfigPath { get; set; }

        public bool IsSdkStyle { get; set; }

        public List<NuGetPackageReference> Packages { get; set; } = new List<NuGetPackageReference>();
    }
}
