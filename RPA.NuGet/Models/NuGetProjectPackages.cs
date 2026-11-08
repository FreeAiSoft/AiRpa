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

        /// <summary>
        /// Нормализованный целевой фреймворк проекта (<c>net48</c>, <c>net472</c>), прочитанный из
        /// <c>TargetFramework</c>/<c>TargetFrameworks</c>/<c>TargetFrameworkVersion</c>. <c>null</c>,
        /// если определить не удалось. Используется для атрибута <c>targetFramework</c> в
        /// <c>packages.config</c> (карточка NuGet: вместо хардкода <c>net48</c>).
        /// </summary>
        public string TargetFramework { get; set; }

        public List<NuGetPackageReference> Packages { get; set; } = new List<NuGetPackageReference>();
    }
}
