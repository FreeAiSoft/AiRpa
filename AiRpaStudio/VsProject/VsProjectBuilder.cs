using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using RPA.Helpers;
using Agent.Core.Services;

namespace RPA.VsProject
{
    /// <summary>Builds .sln/.csproj projects using MSBuild.</summary>
    public static class VsProjectBuilder
    {
        /// <summary>
        /// Locates MSBuild.exe through <see cref="MsBuildLocator"/> (newest installed toolchain).
        /// The lookup is shared with the chat/agent build path, so both always agree.
        /// </summary>
        public static string FindMSBuild()
        {
            return FindMSBuild(MsBuildToolchain.Auto).Path;
        }

        /// <summary>Locates MSBuild.exe for the requested toolchain; the result says how it was found.</summary>
        public static MsBuildLocation FindMSBuild(MsBuildToolchain toolchain)
        {
            MsBuildLocation location = MsBuildLocator.Resolve(toolchain);
            if (!location.Found)
            {
                // Nothing found: keep the old behaviour and let the OS look MSBuild.exe up in PATH.
                location.Path = "MSBuild.exe";
            }

            return location;
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
            return BuildWithToolchain(path, configuration, MsBuildToolchain.Auto, log);
        }

        /// <summary>
        /// Builds a project or solution with an explicitly chosen toolchain: the MSBuild.exe path, its
        /// version and any fallback to another toolchain are written to the log, so the build is never
        /// anonymous and "which Visual Studio built this" is answerable.
        /// </summary>
        public static VsBuildResult BuildWithToolchain(string path, string configuration, MsBuildToolchain toolchain, Action<string> log = null)
        {
            MsBuildLocation location = FindMSBuild(toolchain);

            VsBuildResult result = new VsBuildResult
            {
                ProjectPath = path,
                MsBuildPath = location.Found ? location.Path : null,
                ToolchainName = location.DisplayName,
                ToolchainVersion = location.Version,
                ToolchainFallback = location.IsFallback
            };

            if (log != null)
            {
                log(location.Describe());
            }

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
                FileName = location.Path,
                Arguments = BuildArguments(path, configuration),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            ApplyEnvironmentOverrides(psi, location);

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

        /// <summary>
        /// Gives the child MSBuild process a resolvable .NET SDK, otherwise SDK-style projects fail with
        /// MSB4236 ("SDK Microsoft.NET.Sdk.WindowsDesktop could not be found") when the SDK is not on PATH.
        /// </summary>
        private static void ApplyEnvironmentOverrides(ProcessStartInfo psi, MsBuildLocation location)
        {
            foreach (KeyValuePair<string, string> pair in MsBuildLocator.GetEnvironmentOverrides(location))
            {
                psi.EnvironmentVariables[pair.Key] = pair.Value;
            }
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
    }
}
