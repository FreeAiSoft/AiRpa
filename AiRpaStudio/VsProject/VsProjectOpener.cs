using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace RPA.VsProject
{
    /// <summary>Opens .sln/.csproj files in an external Visual Studio instance.</summary>
    public static class VsProjectOpener
    {
        /// <summary>Builds the command-line arguments passed to devenv.exe (always a new instance).</summary>
        public static string BuildArguments(string path)
        {
            return "\"" + path + "\"";
        }

        /// <summary>
        /// Locates devenv.exe using vswhere, then the registry, then well-known install folders,
        /// falling back to "devenv.exe" (relying on PATH).
        /// </summary>
        public static string FindDevenv()
        {
            // 1) vswhere (Visual Studio 2017+).
            string vswhere = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe");

            if (File.Exists(vswhere))
            {
                string installPath = RunAndCapture(vswhere,
                    "-latest -products * -requires Microsoft.VisualStudio.Component.CoreEditor -property installationPath");
                if (!string.IsNullOrWhiteSpace(installPath))
                {
                    string candidate = Path.Combine(installPath.Trim(), "Common7", "IDE", "devenv.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            // 2) Registry.
            string fromRegistry = FindDevenvFromRegistry();
            if (fromRegistry != null)
                return fromRegistry;

            // 3) Well-known installation folders.
            string known = FindFirstExisting(WellKnownDevenvPaths());
            if (known != null)
                return known;

            // 4) Fallback: rely on PATH.
            return "devenv.exe";
        }

        public static ProcessStartInfo CreateProcessStartInfo(string path)
        {
            return new ProcessStartInfo
            {
                FileName = FindDevenv(),
                Arguments = BuildArguments(path),
                UseShellExecute = true
            };
        }

        public static void OpenInVisualStudio(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required.", nameof(path));

            Process.Start(CreateProcessStartInfo(path));
        }

        /// <summary>Returns the first path from <paramref name="candidates"/> that points to an existing file.</summary>
        public static string FindFirstExisting(IEnumerable<string> candidates)
        {
            if (candidates == null)
                return null;

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<string> WellKnownDevenvPaths()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] editions = { "Enterprise", "Professional", "Community", "BuildTools" };
            string[] years = { "18", "2022", "2019", "2017" };

            foreach (string year in years)
            {
                foreach (string edition in editions)
                {
                    yield return Path.Combine(programFiles, "Microsoft Visual Studio", year, edition, "Common7", "IDE", "devenv.exe");
                    yield return Path.Combine(programFilesX86, "Microsoft Visual Studio", year, edition, "Common7", "IDE", "devenv.exe");
                }
            }
        }

        private static string FindDevenvFromRegistry()
        {
            try
            {
                string[] hivePaths =
                {
                    @"SOFTWARE\Microsoft\VisualStudio\SxS\VS7",
                    @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\SxS\VS7"
                };

                foreach (string hivePath in hivePaths)
                {
                    using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(hivePath))
                    {
                        if (key == null)
                            continue;

                        foreach (string valueName in key.GetValueNames())
                        {
                            string installDir = key.GetValue(valueName) as string;
                            if (string.IsNullOrWhiteSpace(installDir))
                                continue;

                            string candidate = Path.Combine(installDir.Trim(), "Common7", "IDE", "devenv.exe");
                            if (File.Exists(candidate))
                                return candidate;
                        }
                    }
                }
            }
            catch
            {
                // Registry access may be restricted; ignore and fall through.
            }

            return null;
        }

        private static string RunAndCapture(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
