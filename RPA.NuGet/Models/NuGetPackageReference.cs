namespace RPA.NuGet.Models
{
    /// <summary>
    /// A single NuGet package reference of the current project. Maps both to a
    /// <c>&lt;package&gt;</c> entry in <c>packages.config</c> (legacy) and to a
    /// <c>&lt;PackageReference&gt;</c> element in an SDK-style <c>.csproj</c>.
    /// </summary>
    public class NuGetPackageReference
    {
        public string Id { get; set; }

        public string Version { get; set; }

        /// <summary>Only used by legacy <c>packages.config</c> (targetFramework attribute).</summary>
        public string TargetFramework { get; set; }
    }
}
