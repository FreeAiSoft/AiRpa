using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Thin wrapper around the local git executable for the agent's git tools and the
    /// commit workflow. All commands run against a specific repository directory and are
    /// safe to call from a background thread.
    /// </summary>
    public sealed class GitService
    {
        private const string GitExecutable = "git";
        private const int MaxLoggedOutputChars = 20000;

        /// <summary>Resolves the repository root for the given directory.</summary>
        public async Task<string> GetRepositoryRootAsync(string startDirectory, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
                throw new ArgumentException("startDirectory is required.", nameof(startDirectory));

            var output = await RunAsync(startDirectory, "rev-parse --show-toplevel", cancellationToken).ConfigureAwait(false);
            var root = output.Trim();
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Could not determine the git repository root.");

            return root;
        }

        /// <summary>Returns the name of the currently checked-out branch.</summary>
        public async Task<string> GetCurrentBranchAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            var output = await RunAsync(repositoryRoot, "branch --show-current", cancellationToken).ConfigureAwait(false);
            return output.Trim();
        }

        /// <summary>
        /// Builds the text used to auto-generate a commit message: the diff against HEAD plus the
        /// list of changed files from <c>git status --porcelain</c>.
        /// </summary>
        public async Task<string> GetDiffForMessageAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            var status = await RunAsync(repositoryRoot, "status --porcelain", cancellationToken).ConfigureAwait(false);

            var diff = string.Empty;
            try
            {
                diff = await RunAsync(repositoryRoot, "diff HEAD --", cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ChatLog.Warn("GetDiffForMessage: git diff HEAD failed, falling back to the changed-file list: " + ex.Message);
            }

            var changed = new List<string>();
            foreach (var line in SplitLines(status))
            {
                if (line.Length > 3)
                    changed.Add(line.Substring(3));
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(diff))
                parts.Add(diff.TrimEnd());
            if (changed.Count > 0)
                parts.Add("Changed files:" + Environment.NewLine + string.Join(Environment.NewLine, changed));

            return string.Join(Environment.NewLine + Environment.NewLine, parts);
        }

        /// <summary>Returns the changed-file list (porcelain format).</summary>
        public async Task<List<string>> GetChangedFilesAsync(string repositoryRoot, CancellationToken cancellationToken = default)
        {
            var status = await RunAsync(repositoryRoot, "status --porcelain --untracked-files=all", cancellationToken).ConfigureAwait(false);
            var changed = new List<string>();
            foreach (var line in SplitLines(status))
            {
                if (line.Length > 3)
                    changed.Add(line.Substring(3));
            }
            return changed;
        }

        /// <summary>Returns the diff of a single path against HEAD.</summary>
        public async Task<string> GetDiffForPathAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default)
        {
            return await RunAsync(repositoryRoot, "diff HEAD -- " + relativePath, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Stages all changes and creates a commit with the given message.</summary>
        public async Task<string> CommitAllAsync(string repositoryRoot, string message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Commit message is required.", nameof(message));

            await RunAsync(repositoryRoot, "add -A", cancellationToken).ConfigureAwait(false);
            await RunAsync(repositoryRoot, "commit -m \"" + message.Replace("\"", "\\\"") + "\"", cancellationToken).ConfigureAwait(false);
            return await GetCurrentBranchAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> RunAsync(string workingDirectory, string arguments, CancellationToken cancellationToken, Action<StreamWriter> feedStandardInput = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = GitExecutable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = feedStandardInput != null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                using (var process = new Process { StartInfo = psi })
                {
                    if (!process.Start())
                        throw new InvalidOperationException("Failed to start git. Ensure git is installed and available on PATH.");

                    if (feedStandardInput != null)
                    {
                        feedStandardInput(process.StandardInput);
                        process.StandardInput.Close();
                    }

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

                    ChatLog.Info("git " + arguments + " -> exit=" + process.ExitCode + ", stdout=" + stdout.Length + " chars, stderr=" + stderr.Length + " chars");

                    if (process.ExitCode != 0)
                    {
                        var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                        throw new InvalidOperationException("git " + arguments + " failed (exit code " + process.ExitCode + "): " + detail.Trim());
                    }

                    return stdout;
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
                throw new InvalidOperationException("Failed to start git. Ensure git is installed and available on PATH.", ex);
            }
        }

        private static string FormatLoggedOutput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var trimmed = text.TrimEnd();
            if (trimmed.Length <= MaxLoggedOutputChars)
                return trimmed;

            return trimmed.Substring(0, MaxLoggedOutputChars) + Environment.NewLine + "... (output truncated, " + trimmed.Length + " chars total)";
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}

