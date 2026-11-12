namespace RPA.ProjectModel
{
    /// <summary>Where a package version comes from.</summary>
    public enum VsPackageSource
    {
        /// <summary>SDK-style <c>&lt;PackageReference&gt;</c>.</summary>
        PackageReference,

        /// <summary>Legacy <c>packages.config</c> next to the project file.</summary>
        PackagesConfig
    }

    /// <summary>A NuGet package the project depends on (either project format).</summary>
    public class VsProjectPackage
    {
        public string Id { get; set; }

        /// <summary>Version as declared in the project or <c>packages.config</c> (null when it is not pinned).</summary>
        public string Version { get; set; }

        /// <summary>Target framework attribute of a <c>packages.config</c> entry, when present.</summary>
        public string TargetFramework { get; set; }

        public VsPackageSource Source { get; set; }

        /// <summary>True when the version came from <c>Directory.Packages.props</c> (central package management).</summary>
        public bool IsCentralVersion { get; set; }
    }

    /// <summary>A single item (Compile, XamlAppDef, Reference, ...) in a .csproj.</summary>
    public class VsProjectItem
    {
        public string ItemType { get; set; }
        public string Include { get; set; }
        public string HintPath { get; set; }
    }
}
