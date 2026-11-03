using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VibeCoder.Core.Contracts;
using VibeCoder.Core.Models;
using VibeCoder.Core.Services;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Studio-adapted build feedback: one-click build via MSBuild plus compiler diagnostics parsed from the
    /// MSBuild output. Replaces the Visual Studio DTE/Error List implementation.
    /// MSBuild.exe is resolved by <see cref="MsBuildLocator"/> (the same lookup the studio build command
    /// uses), so the toolchain can be pinned and is always reported in the build output.
    /// </summary>
    public sealed class MsBuildFeedbackService : IBuildFeedbackService, IBuildToolchainAware
    {
        private static readonly Regex ErrorLineRegex = new Regex(
            @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]{2,}\d+)\s*:\s*(?<message>.*)$",
            RegexOptions.Compiled);

        private readonly Func<string> _getSolutionPath;
        private string _lastOutput = "";
        private MsBuildLocation _lastLocation;

        public MsBuildFeedbackService(Func<string> getSolutionPath)
        {
            _getSolutionPath = getSolutionPath ?? throw new ArgumentNullException(nameof(getSolutionPath));
        }

        /// <summary>Requested toolchain: "auto" (newest installed), "vs2022" or "vs2026".</summary>
        public string MsBuildToolchain { get; set; } = "auto";

        /// <summary>Which MSBuild.exe the last build used, or which one a build would use now.</summary>
        public string DescribeToolchain()
        {
            if (_lastLocation != null)
                return _lastLocation.Describe();

            return MsBuildLocator.Resolve(MsBuildLocator.ParseToolchain(MsBuildToolchain)).Describe();
        }

        public async Task BuildAsync(string configuration, CancellationToken cancellationToken = default)
        {
            var path = ResolveSolutionPath();
            var config = string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration.Trim();

            var location = MsBuildLocator.Resolve(MsBuildLocator.ParseToolchain(MsBuildToolchain));
            _lastLocation = location;
            if (!location.Found)
                throw new InvalidOperationException(location.Describe());

            var psi = new ProcessStartInfo
            {
                FileName = location.Path,
                Arguments = BuildArguments(path, config),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // MSB4236 guard: SDK-style projects need a resolvable .NET SDK in the child process.
            foreach (var pair in MsBuildLocator.GetEnvironmentOverrides(location))
                psi.EnvironmentVariables[pair.Key] = pair.Value;

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

                    // The toolchain line is part of the build output, so it is visible in the studio log
                    // window and in read_build_output instead of being an invisible implementation detail.
                    _lastOutput = location.Describe() + Environment.NewLine + stdout + stderr;

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

        private static string BuildArguments(string path, string configuration)
        {
            return "\"" + path + "\" /nologo /m /v:minimal /p:Configuration=" + configuration;
        }
    }
}
