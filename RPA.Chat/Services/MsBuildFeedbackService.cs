using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VibeCoder.Core.Contracts;
using VibeCoder.Core.Models;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Studio-adapted build feedback: one-click build via MSBuild (mirroring
    /// RPA.VsProject.VsProjectBuilder) plus compiler diagnostics parsed from the MSBuild
    /// output. Replaces the Visual Studio DTE/Error List implementation.
    /// </summary>
    public sealed class MsBuildFeedbackService : IBuildFeedbackService
    {
        private static readonly Regex ErrorLineRegex = new Regex(
            @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]{2,}\d+)\s*:\s*(?<message>.*)$",
            RegexOptions.Compiled);

        private readonly Func<string> _getSolutionPath;
        private string _lastOutput = "";

        public MsBuildFeedbackService(Func<string> getSolutionPath)
        {
            _getSolutionPath = getSolutionPath ?? throw new ArgumentNullException(nameof(getSolutionPath));
        }

        public async Task BuildAsync(string configuration, CancellationToken cancellationToken = default)
        {
            var path = ResolveSolutionPath();
            var config = string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration.Trim();

            var psi = new ProcessStartInfo
            {
                FileName = FindMSBuild(),
                Arguments = BuildArguments(path, config),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    if (!process.Start())
                        throw new InvalidOperationException("Failed to start MSBuild.");

                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();
                    var exitTask = Task.Run(() => process.WaitForExit());

                    var completed = await Task.WhenAny(exitTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
                    if (completed != exitTask)
                    {
                        try { process.Kill(); } catch { }
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var stdout = await stdoutTask.ConfigureAwait(false);
                    var stderr = await stderrTask.ConfigureAwait(false);
                    _lastOutput = stdout + stderr;

                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("Build failed (exit code " + process.ExitCode + "). See the build output for details.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to run MSBuild: " + ex.Message, ex);
            }
        }

        public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
        {
            // MSBuild can build any configuration; a non-empty name is sufficient.
            return Task.FromResult(!string.IsNullOrWhiteSpace(configuration));
        }

        public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<BuildError>)ParseBuildErrors(_lastOutput));
        }

        public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_lastOutput ?? string.Empty);
        }

        private string ResolveSolutionPath()
        {
            var path = _getSolutionPath();
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("No open project or solution.");

            if (File.Exists(path) || Directory.Exists(path))
                return path;

            throw new InvalidOperationException("Project/solution file not found: " + path);
        }

        /// <summary>Parses MSBuild output lines into structured diagnostics (public for tests).</summary>
        public static List<BuildError> ParseBuildErrors(string output)
        {
            var result = new List<BuildError>();
            if (string.IsNullOrWhiteSpace(output))
                return result;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = ErrorLineRegex.Match(line);
                if (!match.Success)
                    continue;

                var severity = match.Groups["severity"].Value;
                var isWarning = string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase);
                result.Add(new BuildError
                {
                    Code = match.Groups["code"].Value,
                    Message = match.Groups["message"].Value.Trim(),
                    File = match.Groups["file"].Value.Trim(),
                    Line = int.TryParse(match.Groups["line"].Value, out var lineNo) ? lineNo : 0,
                    Column = int.TryParse(match.Groups["col"].Value, out var col) ? col : 0,
                    Severity = isWarning ? "Warning" : "Error",
                    ErrorLevel = isWarning ? 1 : 2,
                    ErrorLevelName = isWarning ? "Warning" : "Error"
                });
            }

            return result;
        }

        /// <summary>Locates MSBuild.exe via vswhere, well-known install folders and PATH.</summary>
        private static string FindMSBuild()
        {
            string vswhere = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe");

            if (File.Exists(vswhere))
            {
                string output = RunAndCapture(vswhere, "-latest -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    string candidate = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            foreach (var known in WellKnownMSBuildPaths())
            {
                if (File.Exists(known))
                    return known;
            }

            return "MSBuild.exe";
        }

        private static string BuildArguments(string path, string configuration)
        {
            return "\"" + path + "\" /nologo /m /v:minimal /p:Configuration=" + configuration;
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

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319", "MSBuild.exe");
            yield return Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319", "MSBuild.exe");
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
