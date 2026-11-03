using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace VibeCoder.Core.Services
{
    /// <summary>Visual Studio toolchain used to run MSBuild.</summary>
    public enum MsBuildToolchain
    {
        /// <summary>Newest installed toolchain (vswhere -latest).</summary>
        Auto = 0,

        /// <summary>Visual Studio 2022 / Build Tools 2022 (MSBuild 17.x).</summary>
        Vs2022 = 1,

        /// <summary>Visual Studio 2026 / Build Tools 2026 (MSBuild 18.x).</summary>
        Vs2026 = 2
    }

    /// <summary>
    /// Result of an MSBuild lookup: the executable, which toolchain it belongs to and how it was found.
    /// Plain data only, so it can be logged, shown in the UI and asserted in tests.
    /// </summary>
    public sealed class MsBuildLocation
    {
        /// <summary>True when an existing MSBuild.exe was located.</summary>
        public bool Found { get; set; }

        /// <summary>Full path to MSBuild.exe (null when not found).</summary>
        public string Path { get; set; }

        /// <summary>Toolchain the caller asked for.</summary>
        public MsBuildToolchain Requested { get; set; }

        /// <summary>Toolchain the executable actually belongs to (Auto when it cannot be classified).</summary>
        public MsBuildToolchain Toolchain { get; set; }

        /// <summary>Human-readable toolchain name, e.g. "Visual Studio 2022".</summary>
        public string DisplayName { get; set; }

        /// <summary>MSBuild.exe file version, e.g. "17.11.35312.102".</summary>
        public string Version { get; set; }

        /// <summary>Where the path came from: vswhere, well-known, framework.</summary>
        public string Source { get; set; }

        /// <summary>Trace of the lookup (and, for failures, the reason).</summary>
        public string Diagnostic { get; set; }

        /// <summary>True when the requested toolchain was not installed and a different one is used instead.</summary>
        public bool IsFallback
        {
            get { return Found && Requested != MsBuildToolchain.Auto && Requested != Toolchain; }
        }

        /// <summary>Single-line, language-neutral description for logs and tool output.</summary>
        public string Describe()
        {
            if (!Found)
            {
                return "MSBuild not found" + (string.IsNullOrEmpty(Diagnostic) ? "." : ": " + Diagnostic);
            }

            StringBuilder sb = new StringBuilder("MSBuild");
            if (!string.IsNullOrEmpty(Version))
            {
                sb.Append(' ').Append(Version);
            }

            if (!string.IsNullOrEmpty(DisplayName))
            {
                sb.Append(" (").Append(DisplayName).Append(')');
            }

            sb.Append(": ").Append(Path);

            if (IsFallback)
            {
                sb.Append(" [requested ")
                  .Append(MsBuildLocator.DescribeToolchain(Requested))
                  .Append(", not installed - falling back to Auto]");
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            return Describe();
        }
    }

    /// <summary>
    /// Test seam for <see cref="MsBuildLocator"/>: everything the lookup touches from the machine.
    /// </summary>
    public interface IMsBuildEnvironment
    {
        string ProgramFiles { get; }
        string ProgramFilesX86 { get; }
        string GetEnvironmentVariable(string name);
        string RunVswhere(string arguments);
        bool FileExists(string path);
        string GetFileVersion(string path);
    }

    /// <summary>Real machine: vswhere on disk, the file system and process environment.</summary>
    public sealed class SystemMsBuildEnvironment : IMsBuildEnvironment
    {
        public string ProgramFiles
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles); }
        }

        public string ProgramFilesX86
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86); }
        }

        public string GetEnvironmentVariable(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name);
            }
            catch
            {
                return null;
            }
        }

        public bool FileExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public string GetFileVersion(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                return FileVersionInfo.GetVersionInfo(path).FileVersion;
            }
            catch
            {
                return null;
            }
        }

        public string RunVswhere(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = MsBuildLocator.FindVswhere(this),
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = psi })
                {
                    if (!process.Start())
                    {
                        return null;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);
                    return output;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Single source of truth for "which MSBuild.exe do we build with".
    /// Looks the toolchain up via vswhere for a requested Visual Studio generation, falls back to
    /// well-known install folders, then to the .NET Framework engine, and reports everything it saw
    /// so the choice is visible in the build log instead of being implicit.
    /// </summary>
    public static class MsBuildLocator
    {
        /// <summary>vswhere component that guarantees a usable MSBuild.</summary>
        public const string MsBuildComponent = "Microsoft.Component.MSBuild";

        private const string FindMsBuildArgument = "-requires " + MsBuildComponent + " -find MSBuild\\**\\Bin\\MSBuild.exe";

        private static readonly string[] Editions = { "Enterprise", "Professional", "Community", "BuildTools" };

        /// <summary>Toolchain name for logs (language-neutral).</summary>
        public static string DescribeToolchain(MsBuildToolchain toolchain)
        {
            switch (toolchain)
            {
                case MsBuildToolchain.Vs2022:
                    return "Visual Studio 2022";
                case MsBuildToolchain.Vs2026:
                    return "Visual Studio 2026";
                default:
                    return "Auto";
            }
        }

        /// <summary>vswhere <c>-version</c> range for a toolchain (null for Auto).</summary>
        public static string VersionRange(MsBuildToolchain toolchain)
        {
            switch (toolchain)
            {
                case MsBuildToolchain.Vs2022:
                    return "[17.0,18.0)";
                case MsBuildToolchain.Vs2026:
                    return "[18.0,19.0)";
                default:
                    return null;
            }
        }

        /// <summary>Parses a toolchain name from settings/tool arguments ("auto", "vs2022", "vs2026").</summary>
        public static MsBuildToolchain ParseToolchain(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return MsBuildToolchain.Auto;
            }

            string normalized = value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
            if (string.Equals(normalized, "2022", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "vs2022", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "vs17", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "17", StringComparison.OrdinalIgnoreCase))
            {
                return MsBuildToolchain.Vs2022;
            }

            if (string.Equals(normalized, "2026", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "vs2026", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "vs18", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "18", StringComparison.OrdinalIgnoreCase))
            {
                return MsBuildToolchain.Vs2026;
            }

            return MsBuildToolchain.Auto;
        }

        /// <summary>Resolves MSBuild.exe for the requested toolchain on this machine.</summary>
        /// <param name="preference">Explicit toolchain, or Auto for the newest installed one.</param>
        /// <param name="allowFallback">When false, an explicit preference that is not installed yields Found=false.</param>
        public static MsBuildLocation Resolve(MsBuildToolchain preference = MsBuildToolchain.Auto, bool allowFallback = true)
        {
            return Resolve(new SystemMsBuildEnvironment(), preference, allowFallback);
        }

        /// <summary>Resolves MSBuild.exe using an injectable environment (tests).</summary>
        public static MsBuildLocation Resolve(IMsBuildEnvironment environment, MsBuildToolchain preference, bool allowFallback)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            List<string> diagnostics = new List<string>();

            if (preference != MsBuildToolchain.Auto)
            {
                MsBuildLocation explicitLocation = FromVswhere(environment, preference, diagnostics)
                                                   ?? FromWellKnown(environment, preference, diagnostics);

                if (explicitLocation != null)
                {
                    explicitLocation.Diagnostic = Join(diagnostics);
                    return explicitLocation;
                }

                string message = DescribeToolchain(preference) + " not found (vswhere -version " + VersionRange(preference) + ")";
                if (!allowFallback)
                {
                    diagnostics.Add(message + ".");
                    return NotFound(preference, Join(diagnostics));
                }

                diagnostics.Add(message + " - falling back to Auto.");
            }

            MsBuildLocation location = FromVswhere(environment, MsBuildToolchain.Auto, diagnostics)
                                       ?? FromWellKnown(environment, MsBuildToolchain.Auto, diagnostics)
                                       ?? FromFramework(environment, diagnostics);

            if (location == null)
            {
                return NotFound(preference, Join(diagnostics));
            }

            location.Requested = preference;
            if (string.IsNullOrEmpty(location.Diagnostic))
            {
                location.Diagnostic = Join(diagnostics);
            }

            return location;
        }

        /// <summary>All MSBuild.exe paths vswhere reports for every installed instance (diagnostics).</summary>
        public static IReadOnlyList<MsBuildLocation> FindAll()
        {
            return FindAll(new SystemMsBuildEnvironment());
        }

        /// <summary>All MSBuild.exe paths vswhere reports for every installed instance (diagnostics).</summary>
        public static IReadOnlyList<MsBuildLocation> FindAll(IMsBuildEnvironment environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            List<MsBuildLocation> result = new List<MsBuildLocation>();
            List<string> diagnostics = new List<string>();

            foreach (MsBuildToolchain toolchain in new[] { MsBuildToolchain.Vs2022, MsBuildToolchain.Vs2026 })
            {
                MsBuildLocation location = FromVswhere(environment, toolchain, diagnostics);
                if (location != null)
                {
                    result.Add(location);
                }
            }

            return result;
        }

        /// <summary>Classifies an MSBuild.exe path/version into a toolchain (Auto when unknown).</summary>
        public static MsBuildToolchain Classify(string msbuildPath, string fileVersion)
        {
            if (!string.IsNullOrEmpty(msbuildPath))
            {
                string normalized = msbuildPath.Replace('/', '\\');
                if (normalized.IndexOf("\\18\\", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.IndexOf("\\2026\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return MsBuildToolchain.Vs2026;
                }

                if (normalized.IndexOf("\\2022\\", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.IndexOf("\\17\\", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return MsBuildToolchain.Vs2022;
                }
            }

            Version parsed;
            if (!string.IsNullOrEmpty(fileVersion) && Version.TryParse(fileVersion.Trim(), out parsed))
            {
                if (parsed.Major >= 18)
                {
                    return MsBuildToolchain.Vs2026;
                }

                if (parsed.Major == 17)
                {
                    return MsBuildToolchain.Vs2022;
                }
            }

            return MsBuildToolchain.Auto;
        }

        /// <summary>
        /// Locates vswhere.exe (installed by every Visual Studio since 2017).
        /// </summary>
        public static string FindVswhere(IMsBuildEnvironment environment)
        {
            if (environment == null)
            {
                return null;
            }

            string root = environment.ProgramFilesX86;
            if (!string.IsNullOrEmpty(root))
            {
                string candidate = Path.Combine(root, "Microsoft Visual Studio", "Installer", "vswhere.exe");
                if (environment.FileExists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Environment variables the MSBuild child process needs so SDK-style projects resolve
        /// Microsoft.NET.Sdk.* (otherwise the build fails with MSB4236 when the .NET SDK is not on PATH).
        /// </summary>
        public static IDictionary<string, string> GetEnvironmentOverrides(MsBuildLocation location)
        {
            return GetEnvironmentOverrides(location, new SystemMsBuildEnvironment());
        }

        /// <summary>
        /// Environment variables the MSBuild child process needs (injectable environment for tests).
        /// </summary>
        public static IDictionary<string, string> GetEnvironmentOverrides(MsBuildLocation location, IMsBuildEnvironment environment)
        {
            if (environment == null)
            {
                throw new ArgumentNullException(nameof(environment));
            }

            string dotNetRoot = environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrWhiteSpace(dotNetRoot))
            {
                string programFiles = environment.ProgramFiles;
                if (!string.IsNullOrEmpty(programFiles))
                {
                    string candidate = Path.Combine(programFiles, "dotnet");
                    if (environment.FileExists(Path.Combine(candidate, "dotnet.exe")))
                    {
                        dotNetRoot = candidate;
                    }
                }
            }

            return BuildDotNetSdkOverrides(
                dotNetRoot,
                environment.GetEnvironmentVariable("PATH"),
                environment.GetEnvironmentVariable("DOTNET_ROOT"));
        }

        /// <summary>
        /// Pure helper: which overrides make the .NET SDK resolvable for the MSBuild child process.
        /// Existing user settings win; PATH is only extended when the SDK folder is missing from it.
        /// </summary>
        public static IDictionary<string, string> BuildDotNetSdkOverrides(string dotNetRoot, string currentPath, string currentDotNetRoot)
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(dotNetRoot))
            {
                return overrides;
            }

            string root = dotNetRoot.Trim().TrimEnd('\\');
            if (root.Length == 0)
            {
                return overrides;
            }

            if (string.IsNullOrWhiteSpace(currentDotNetRoot))
            {
                overrides["DOTNET_ROOT"] = root;
            }

            bool onPath = false;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                foreach (string part in currentPath.Split(';'))
                {
                    string trimmed = part.Trim().TrimEnd('\\');
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    if (string.Equals(trimmed, root, StringComparison.OrdinalIgnoreCase))
                    {
                        onPath = true;
                        break;
                    }
                }
            }

            if (!onPath)
            {
                overrides["PATH"] = root + ";" + (currentPath ?? string.Empty);
            }

            return overrides;
        }

        private static MsBuildLocation FromVswhere(IMsBuildEnvironment environment, MsBuildToolchain toolchain, List<string> diagnostics)
        {
            string vswhere = FindVswhere(environment);
            if (vswhere == null)
            {
                diagnostics.Add("vswhere.exe not found.");
                return null;
            }

            string range = VersionRange(toolchain);
            string arguments = (range == null ? string.Empty : "-version \"" + range + "\" ")
                               + "-latest -prerelease -products * " + FindMsBuildArgument;

            string output = environment.RunVswhere(arguments);
            if (string.IsNullOrWhiteSpace(output))
            {
                diagnostics.Add("vswhere (" + DescribeToolchain(toolchain) + ") returned no path.");
                return null;
            }

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Trim();
                if (!environment.FileExists(candidate))
                {
                    continue;
                }

                return Create(candidate, toolchain, "vswhere", environment);
            }

            diagnostics.Add("vswhere (" + DescribeToolchain(toolchain) + ") returned a path that does not exist: " + output.Trim());
            return null;
        }

        private static MsBuildLocation FromWellKnown(IMsBuildEnvironment environment, MsBuildToolchain toolchain, List<string> diagnostics)
        {
            foreach (string candidate in WellKnownPaths(environment, toolchain))
            {
                if (environment.FileExists(candidate))
                {
                    return Create(candidate, toolchain, "well-known", environment);
                }
            }

            diagnostics.Add("Well-known " + DescribeToolchain(toolchain) + " folders checked, MSBuild.exe not found.");
            return null;
        }

        private static MsBuildLocation FromFramework(IMsBuildEnvironment environment, List<string> diagnostics)
        {
            string windows = environment.GetEnvironmentVariable("WINDIR");
            if (string.IsNullOrEmpty(windows))
            {
                windows = environment.GetEnvironmentVariable("SystemRoot");
            }

            if (string.IsNullOrEmpty(windows))
            {
                diagnostics.Add("Windows folder unknown, .NET Framework MSBuild skipped.");
                return null;
            }

            string[] candidates =
            {
                Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319", "MSBuild.exe"),
                Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319", "MSBuild.exe")
            };

            foreach (string candidate in candidates)
            {
                if (!environment.FileExists(candidate))
                {
                    continue;
                }

                MsBuildLocation location = Create(candidate, MsBuildToolchain.Auto, "framework", environment);
                location.DisplayName = "MSBuild .NET Framework 4.0 (SDK-style projects unsupported)";
                location.Diagnostic = "No Visual Studio instance found, using the .NET Framework engine.";
                return location;
            }

            diagnostics.Add(".NET Framework MSBuild not found.");
            return null;
        }

        private static MsBuildLocation Create(string path, MsBuildToolchain requested, string source, IMsBuildEnvironment environment)
        {
            string version = environment.GetFileVersion(path);
            MsBuildToolchain toolchain = Classify(path, version);

            return new MsBuildLocation
            {
                Found = true,
                Path = path,
                Requested = requested,
                Toolchain = toolchain,
                DisplayName = DescribeToolchain(toolchain),
                Version = version,
                Source = source
            };
        }

        private static IEnumerable<string> WellKnownPaths(IMsBuildEnvironment environment, MsBuildToolchain toolchain)
        {
            string[] years;
            switch (toolchain)
            {
                case MsBuildToolchain.Vs2022:
                    years = new[] { "2022", "17" };
                    break;
                case MsBuildToolchain.Vs2026:
                    years = new[] { "18", "2026" };
                    break;
                default:
                    years = new[] { "18", "2026", "2022", "17", "2019", "2017" };
                    break;
            }

            string[] roots = { environment.ProgramFiles, environment.ProgramFilesX86 };

            foreach (string year in years)
            {
                foreach (string root in roots)
                {
                    if (string.IsNullOrEmpty(root))
                    {
                        continue;
                    }

                    foreach (string edition in Editions)
                    {
                        yield return Path.Combine(root, "Microsoft Visual Studio", year, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                    }
                }
            }
        }

        private static MsBuildLocation NotFound(MsBuildToolchain requested, string diagnostic)
        {
            return new MsBuildLocation
            {
                Found = false,
                Requested = requested,
                Toolchain = MsBuildToolchain.Auto,
                Diagnostic = diagnostic
            };
        }

        private static string Join(List<string> diagnostics)
        {
            return diagnostics == null || diagnostics.Count == 0 ? null : string.Join(" ", diagnostics);
        }
    }
}
