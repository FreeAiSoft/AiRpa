using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using RPA.Helpers;

namespace RPA.VsProject
{
    /// <summary>Builds .sln/.csproj projects using MSBuild.</summary>
    public static class VsProjectBuilder
    {
        /// <summary>Locates MSBuild.exe via vswhere, well-known install folders and the .NET Framework.</summary>
        public static string FindMSBuild()
        {
            // 1) vswhere (Visual Studio 2017+).
            string vswhere = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe");

            if (File.Exists(vswhere))
            {
                string output = RunAndCapture(vswhere,
                    "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    string candidate = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // 2) Well-known install folders + .NET Framework MSBuild.
            string known = VsProjectOpener.FindFirstExisting(WellKnownMSBuildPaths());
            if (known != null)
            {
                return known;
            }

            // 3) Fallback: rely on PATH.
            return "MSBuild.exe";
        }

        /// <summary>Builds the command-line arguments passed to MSBuild.exe.</summary>
        public static string BuildArguments(string path, string configuration)
        {
            string config = string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration;
            return "\"" + path + "\" /nologo /m /v:minimal /p:Configuration=" + config;
        }

        /// <summary>Builds a project or solution and returns the result. <paramref name="log"/> receives each output line.</summary>
        public static VsBuildResult Build(string path, string configuration = "Debug", Action<string> log = null)
        {
            VsBuildResult result = new VsBuildResult { ProjectPath = path };

            if (string.IsNullOrWhiteSpace(path))
            {
                result.ExitCode = -1;
                result.Errors.Add("Path is required.");
                return result;
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                result.ExitCode = -1;
                result.Errors.Add("Project/solution file not found: " + path);
                return result;
            }

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = FindMSBuild(),
                Arguments = BuildArguments(path, configuration),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using Process process = new Process { StartInfo = psi };
                _ = process.Start();

                // Read stderr on a background task to avoid a pipe deadlock.
                Task<string> stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = stderrTask.Result;
                process.WaitForExit();

                result.ExitCode = process.ExitCode;
                result.Output = stdout + stderr;
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Errors.Add("Failed to start MSBuild: " + ErrorText.Of(ex));
                return result;
            }

            result.Success = result.ExitCode == 0;
            ExtractErrors(result);

            if (log != null && !string.IsNullOrWhiteSpace(result.Output))
            {
                foreach (string line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    log(line);
                }
            }

            return result;
        }

        private static void ExtractErrors(VsBuildResult result)
        {
            if (string.IsNullOrWhiteSpace(result.Output))
            {
                return;
            }

            foreach (string line in result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains(": error "))
                {
                    result.Errors.Add(line);
                }
            }
        }

        private static IEnumerable<string> WellKnownMSBuildPaths()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] editions = { "Enterprise", "Professional", "Community", "BuildTools" };
            string[] years = { "18", "2022", "2019", "2017" };

            foreach (string year in years)
            {
                foreach (string edition in editions)
                {
                    yield return Path.Combine(programFiles, "Microsoft Visual Studio", year, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                    yield return Path.Combine(programFilesX86, "Microsoft Visual Studio", year, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                }
            }

            // .NET Framework MSBuild (can build legacy net4x projects).
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319", "MSBuild.exe");
            yield return Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319", "MSBuild.exe");
        }

        private static string RunAndCapture(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using Process process = Process.Start(psi);
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
            catch
            {
                return null;
            }
        }
    }
}
