using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Contracts;
using RPA.Shared.Contracts;
using Agent.Core.Models;
using RPA.Shared.Models;
using Agent.Core.Services;
using RPA.Shared.Services;

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

        /// <summary>Maximum wall-clock time for one build before the MSBuild process tree is killed.</summary>
        public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>How often a long-running build logs a "still running" heartbeat line.</summary>
        public TimeSpan BuildWatchdogInterval { get; set; } = TimeSpan.FromSeconds(30);

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
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            // MSB4236 guard: SDK-style projects need a resolvable .NET SDK in the child process.
            foreach (var pair in MsBuildLocator.GetEnvironmentOverrides(location))
                psi.EnvironmentVariables[pair.Key] = pair.Value;

            // Keep /m worker nodes from outliving this build: reused nodes hold file locks and
            // can hang a subsequent build long after the current one finished or was cancelled.
            psi.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";

            ChatLog.Info("[build] start: config=" + config + ", toolchain=" + MsBuildToolchain + ", msbuild=" + location.Path + ", args=" + psi.Arguments);

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    var output = new StringBuilder();
                    var outputLock = new object();

                    process.OutputDataReceived += (s, e) => { if (e.Data == null) return; lock (outputLock) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data == null) return; lock (outputLock) output.AppendLine(e.Data); };

                    if (!process.Start())
                        throw new InvalidOperationException("Failed to start MSBuild.");

                    var pid = process.Id;
                    ChatLog.Info("[build] started: pid=" + pid);

                    var stopwatch = Stopwatch.StartNew();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    // The parameterless WaitForExit also drains the async stdout/stderr handlers,
                    // so by the time exitTask completes the StringBuilder holds the whole output.
                    var exitTask = Task.Run(() => process.WaitForExit());
                    var timeoutTask = Task.Delay(BuildTimeout, cancellationToken);

                    using (StartBuildWatchdog(process, stopwatch))
                    {
                        var completed = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
                        if (completed != exitTask)
                        {
                            var timedOut = !cancellationToken.IsCancellationRequested;
                            var reason = timedOut
                                ? "timed out after " + (int)BuildTimeout.TotalSeconds + "s"
                                : "cancelled";
                            ChatLog.Warn("[build] " + reason + ": pid=" + pid + ", elapsed=" + stopwatch.Elapsed + ", alive=" + IsAlive(process));
                            KillProcessTree(process);

                            // Let the readers flush whatever they already captured, then keep it
                            // for read_output/read_errors so the caller can inspect partial output.
                            await Task.WhenAny(exitTask, Task.Delay(3000)).ConfigureAwait(false);
                            stopwatch.Stop();
                            _lastOutput = location.Describe() + Environment.NewLine + output.ToString();

                            if (timedOut)
                                throw new InvalidOperationException("Build timed out after " + (int)BuildTimeout.TotalSeconds + " seconds. See the build output for partial progress.");
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    stopwatch.Stop();
                    var text = output.ToString();
                    _lastOutput = location.Describe() + Environment.NewLine + text;

                    ChatLog.Info("[build] finished: pid=" + pid + ", exitCode=" + process.ExitCode + ", elapsed=" + stopwatch.Elapsed + ", outputChars=" + text.Length);

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

        public Task BuildProjectAsync(string configuration, string projectName, CancellationToken cancellationToken = default)
        {
            // The studio build command builds the whole solution; a project-scoped build is
            // not supported by the MSBuild feedback service, so delegate to the solution build.
            return BuildAsync(configuration, cancellationToken);
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

        private Timer StartBuildWatchdog(Process process, Stopwatch stopwatch)
        {
            var interval = BuildWatchdogInterval;
            if (interval <= TimeSpan.Zero)
                interval = TimeSpan.FromSeconds(30);

            return new Timer(
                _ =>
                {
                    int pid;
                    bool alive;
                    try
                    {
                        pid = process.Id;
                        alive = !process.HasExited;
                    }
                    catch
                    {
                        return;
                    }
                    if (!alive) return;
                    ChatLog.Warn("[build] still running: pid=" + pid + ", elapsed=" + stopwatch.Elapsed);
                },
                null,
                interval,
                interval);
        }

        private static bool IsAlive(Process process)
        {
            try { return !process.HasExited; }
            catch { return false; }
        }

        private static void KillProcessTree(Process process)
        {
            int pid;
            try
            {
                if (process.HasExited) return;
                pid = process.Id;
            }
            catch
            {
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + pid + " /T /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var killer = new Process { StartInfo = psi })
                {
                    if (killer.Start())
                    {
                        try { killer.WaitForExit(10000); } catch { }
                    }
                }
            }
            catch
            {
                // Fall through to killing just the root process below.
            }

            try { process.Kill(); } catch { }
        }

        private static string BuildArguments(string path, string configuration)
        {
            return "\"" + path + "\" /nologo /m /nodeReuse:false /v:minimal /p:Configuration=" + configuration;
        }
    }
}
