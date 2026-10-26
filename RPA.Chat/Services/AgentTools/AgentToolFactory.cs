using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using VibeCoder.Core.AgentTools;
using VibeCoder.Core.Contracts;
using VibeCoder.Core.Models;
using VibeCoder.Core.Services;

namespace RPA.Chat.Services.AgentTools
{
    /// <summary>
    /// Builds the agent tool registry for the studio by wrapping the studio services
    /// (document tabs, MSBuild feedback, shell, git). This is the studio-adapted
    /// replacement for the VSIX <c>AgentToolFactory</c>: no EnvDTE/Visual Studio SDK
    /// dependency and no MCP category.
    /// </summary>
    public sealed class AgentToolFactory
    {
        private const int ReadFilesMaxLines = 2000;
        private const int ReadFilesMaxChars = 48000;
        private const int SearchMaxCharsPerQuery = 48000;
        private const int ListFilesLimit = 1000;

        private static readonly string[] SkippedDirectories =
        {
            ".git", ".svn", ".hg", ".vs", "bin", "obj", "node_modules", "packages", "TestResults",
            "__pycache__", "env", "venv", "dist", "out", "bundle", "vendor", "tmp", "temp",
            "deps", "pkg", "Pods"
        };

        private readonly IAiProvider _ai;
        private readonly Func<string> _getSolutionDir;
        private readonly IStudioDocumentService _documents;
        private readonly IBuildFeedbackService _buildFeedback;
        private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<string>> _askQuestionAsync;
        private readonly Func<string, string, CancellationToken, Task<string>> _spawnSubAgentAsync;
        private readonly bool _enableSubagents;
        private readonly int _subagentMaxDepth;
        private readonly ShellCommandService _shell = new ShellCommandService();
        private readonly GitService _git = new GitService();
        private readonly AsyncLocal<int> _subagentDepth = new AsyncLocal<int>();
        private readonly Func<string> _getDocsDirectory;

        public AgentToolFactory(
            IAiProvider ai,
            Func<string> getSolutionDir,
            IStudioDocumentService documents,
            IBuildFeedbackService buildFeedback,
            Func<string, IReadOnlyList<string>, CancellationToken, Task<string>> askQuestionAsync,
            Func<string, string, CancellationToken, Task<string>> spawnSubAgentAsync,
            bool enableSubagents,
            int subagentMaxDepth,
            Func<string> getDocsDirectory = null)
        {
            _ai = ai ?? throw new ArgumentNullException(nameof(ai));
            _getSolutionDir = getSolutionDir ?? throw new ArgumentNullException(nameof(getSolutionDir));
            _documents = documents ?? throw new ArgumentNullException(nameof(documents));
            _buildFeedback = buildFeedback ?? throw new ArgumentNullException(nameof(buildFeedback));
            _askQuestionAsync = askQuestionAsync ?? throw new ArgumentNullException(nameof(askQuestionAsync));
            _spawnSubAgentAsync = spawnSubAgentAsync ?? throw new ArgumentNullException(nameof(spawnSubAgentAsync));
            _enableSubagents = enableSubagents;
            _subagentMaxDepth = subagentMaxDepth > 0 ? subagentMaxDepth : 2;
            _getDocsDirectory = getDocsDirectory ?? HelpDocumentation.ResolveDocsDirectory;
        }

        public AgentToolRegistry CreateRegistry()
        {
            var registry = new AgentToolRegistry();

            // ---- Read-only tools ----
            registry.Register(new AgentTool(
                "read_files",
                "Read the content of text or image files at the provided absolute paths, or return only an inclusive one-based line range. Prefers the open editor tab content (unsaved changes) when the file is open.",
                "{\"paths\":[\"string\"],\"start_line\":\"integer\",\"end_line\":\"integer\"}",
                false,
                ReadFilesAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "list_files",
                "List files and directories in a directory, optionally recursive. Skips build artifacts and VCS directories.",
                "{\"path\":\"string\",\"recursive\":\"boolean\"}",
                false,
                ListFilesAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "search_codebase",
                "Perform regex pattern searches across the codebase. Supports multiple parallel searches.",
                "{\"queries\":[\"string\"]}",
                false,
                SearchCodebaseAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "git_status",
                "Show the git working-tree status of the open project's repository.",
                "{}",
                false,
                GitStatusAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "git_diff",
                "Show the uncommitted git diff of the open project's repository (optionally for one path).",
                "{\"path\":\"string\"}",
                false,
                GitDiffAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "fetch_web_content",
                "Fetch content from URLs and analyze them using the provided prompts.",
                "{\"requests\":[{\"url\":\"string\",\"prompt\":\"string\"}]}",
                false,
                FetchWebContentAsync,
                permission: AgentPermissions.WebFetch));

            registry.Register(new AgentTool(
                "read_errors",
                "Read the compiler errors/warnings from the last build of the open project.",
                "{}",
                false,
                ReadErrorsAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "read_output",
                "Read the raw build output text of the last build.",
                "{}",
                false,
                ReadOutputAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "search_docs",
                "Search the built-in studio user documentation (workflow designer, activities, project management, settings, MCP server, NuGet manager) and return matching sections with their relative paths.",
                "{\"query\":\"string\",\"max_results\":\"integer\"}",
                false,
                SearchDocsAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "read_docs",
                "Read the full content of a built-in documentation file by its relative path (as listed by search_docs).",
                "{\"path\":\"string\"}",
                false,
                ReadDocsAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "ask_question",
                "Ask the user a single question with 2-5 selectable options.",
                "{\"question\":\"string\",\"options\":[\"string\"]}",
                false,
                AskQuestionAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "submit_and_exit",
                "Submit the final answer and exit the conversation.",
                "{\"summary\":\"string\",\"verified\":\"boolean\"}",
                false,
                SubmitAndExitAsync,
                completesRun: true));

            if (_enableSubagents)
            {
                registry.Register(new AgentTool(
                    "spawn_agent",
                    "Delegate a task to a sub-agent with its own system prompt.",
                    "{\"system_prompt\":\"string\",\"task\":\"string\"}",
                    false,
                    SpawnAgentAsync,
                    SpawnAgentDisplayAsync,
                    permission: AgentPermissions.Subagents));
            }


            // ---- Mutating tools (Act only, permission-gated) ----
            registry.Register(new AgentTool(
                "run_commands",
                "Run non-interactive shell commands from the root of the workspace in Windows environment.",
                "{\"commands\":[\"string\"]}",
                true,
                RunCommandsAsync,
                permission: AgentPermissions.ExecuteCommand));

            registry.Register(new AgentTool(
                "editor",
                "Edit a file: write full content (path + content), or replace an old string with a new string (path + old_text + new_text).",
                "{\"path\":\"string\",\"content\":\"string\",\"old_text\":\"string\",\"new_text\":\"string\"}",
                true,
                EditorAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "build",
                "Build the open project/solution with the given configuration using MSBuild.",
                "{\"configuration\":\"string\"}",
                true,
                BuildAsync,
                permission: AgentPermissions.ExecuteCommand));

            registry.Register(new AgentTool(
                "download_file",
                "Download a file from a URL and save it to disk.",
                "{\"url\":\"string\",\"path\":\"string\"}",
                true,
                DownloadFileAsync,
                permission: AgentPermissions.WebFetch));

            registry.Register(new AgentTool(
                "git_commit",
                "Stage all changes and create a commit with the given message in the open project's repository.",
                "{\"message\":\"string\"}",
                true,
                GitCommitAsync,
                permission: AgentPermissions.ExecuteCommand));

            return registry;
        }


        private async Task<string> AskQuestionAsync(JObject args, CancellationToken ct)
        {
            var question = (args["question"]?.Value<string>() ?? "").Trim();
            var options = new List<string>();
            if (args["options"] is JArray array)
            {
                foreach (var item in array)
                {
                    var text = item?.Value<string>() ?? "";
                    if (!string.IsNullOrWhiteSpace(text))
                        options.Add(text.Trim());
                }
            }

            if (string.IsNullOrEmpty(question))
                return "Missing or empty \"question\" argument.";
            if (options.Count < 2 || options.Count > 5)
                return "The \"options\" argument must contain between 2 and 5 non-empty options.";

            try
            {
                return await _askQuestionAsync(question, options, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "ask_question failed: " + ex.Message;
            }
        }

        private async Task<string> SpawnAgentAsync(JObject args, CancellationToken ct)
        {
            var systemPrompt = (args["system_prompt"]?.Value<string>() ?? string.Empty).Trim();
            var task = (args["task"]?.Value<string>() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(systemPrompt))
                return "Missing or empty \"system_prompt\" argument.";
            if (string.IsNullOrEmpty(task))
                return "Missing or empty \"task\" argument.";

            var depth = _subagentDepth.Value;
            if (depth >= _subagentMaxDepth)
                return "spawn_agent refused: maximum sub-agent nesting depth reached (" + _subagentMaxDepth + ").";

            _subagentDepth.Value = depth + 1;
            try
            {
                return await _spawnSubAgentAsync(systemPrompt, task, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "spawn_agent failed: " + ex.Message;
            }
            finally
            {
                _subagentDepth.Value = depth;
            }
        }

        private Task<string> SpawnAgentDisplayAsync(JObject args, CancellationToken ct)
        {
            var task = (args["task"]?.Value<string>() ?? string.Empty).Trim();
            var shortTask = task.Length > 60 ? task.Substring(0, 60) + "…" : task;
            return Task.FromResult(string.IsNullOrEmpty(shortTask) ? "sub-agent" : shortTask);
        }

        private Task<string> SubmitAndExitAsync(JObject args, CancellationToken ct)
        {
            var summary = (args["summary"]?.Value<string>() ?? "").Trim();
            return Task.FromResult(string.IsNullOrEmpty(summary) ? "(submitted)" : summary);
        }

        private Task<string> ReadFilesAsync(JObject args, CancellationToken ct)
        {
            var paths = new List<string>();
            if (args["paths"] is JArray array)
            {
                foreach (var item in array)
                {
                    var p = item?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(p))
                        paths.Add(p);
                }
            }
            else
            {
                var single = args["path"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(single))
                    paths.Add(single);
            }

            if (paths.Count == 0)
                return Task.FromResult("Missing \"paths\" argument.");

            var startLine = args["start_line"]?.Value<int>() ?? 0;
            var endLine = args["end_line"]?.Value<int>() ?? 0;

            var sb = new StringBuilder();
            foreach (var raw in paths)
            {
                var p = ResolvePath(raw);
                if (string.IsNullOrWhiteSpace(p))
                {
                    sb.AppendLine(raw + ": (invalid path)");
                    continue;
                }

                // Prefer the open editor buffer (unsaved changes), like the VSIX read_files.
                var buffer = _documents.GetOpenDocumentContent(p);
                if (buffer != null)
                {
                    sb.AppendLine("=== " + MakeRelative(p) + " (open editor buffer) ===");
                    sb.AppendLine(Truncate(ReadRange(buffer, startLine, endLine)));
                    continue;
                }

                if (!File.Exists(p))
                {
                    sb.AppendLine(raw + ": File not found.");
                    continue;
                }

                sb.AppendLine("=== " + MakeRelative(p) + " ===");
                try
                {
                    sb.AppendLine(Truncate(ReadRange(File.ReadAllText(p), startLine, endLine)));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("Failed to read file: " + ex.Message);
                }
            }

            return Task.FromResult(sb.ToString().TrimEnd());
        }


        private async Task<string> ReadErrorsAsync(JObject args, CancellationToken ct)
        {
            var errors = await _buildFeedback.GetCompilationErrorsAsync(ct).ConfigureAwait(false);
            var all = (errors ?? new List<BuildError>()).Where(e => e != null).ToList();
            if (all.Count == 0)
                return "The compiler error list is empty.";

            var sb = new StringBuilder();
            sb.AppendLine("Compiler diagnostics (" + all.Count + " entries):");
            foreach (var e in all)
            {
                var severity = string.IsNullOrWhiteSpace(e.Severity) ? "Error" : e.Severity;
                sb.Append("[").Append(severity).Append("] ");
                if (!string.IsNullOrWhiteSpace(e.Code)) sb.Append(e.Code).Append(": ");
                sb.Append(e.Message ?? string.Empty);

                var location = FormatToolLocation(e);
                if (!string.IsNullOrEmpty(location)) sb.Append(" (").Append(location).Append(")");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private async Task<string> ReadOutputAsync(JObject args, CancellationToken ct)
        {
            var output = await _buildFeedback.GetOutputWindowTextAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                return "The build output is empty.";
            return Truncate(output);
        }

        private Task<string> SearchDocsAsync(JObject args, CancellationToken ct)
        {
            var query = (args["query"]?.Value<string>() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(query))
                return Task.FromResult("Missing or empty \"query\" argument.");

            var maxResults = args["max_results"]?.Value<int>() ?? 8;
            if (maxResults <= 0) maxResults = 8;
            if (maxResults > 25) maxResults = 25;

            var index = DocumentationIndex.Build(_getDocsDirectory());
            var results = index.Search(query, maxResults);
            if (results.Count == 0)
                return Task.FromResult("No documentation matches found.");

            var sb = new StringBuilder();
            foreach (var r in results)
            {
                sb.AppendLine("## " + (r.Title ?? r.RelativePath));
                sb.AppendLine("Path: " + r.RelativePath);
                sb.AppendLine(r.Snippet);
                sb.AppendLine();
            }

            return Task.FromResult(sb.ToString().TrimEnd());
        }

        private Task<string> ReadDocsAsync(JObject args, CancellationToken ct)
        {
            var path = (args["path"]?.Value<string>() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(path))
                return Task.FromResult("Missing or empty \"path\" argument.");

            var index = DocumentationIndex.Build(_getDocsDirectory());
            var content = index.Read(path);
            if (content == null)
                return Task.FromResult("Documentation file not found: " + path);

            return Task.FromResult(content);
        }

        private async Task<string> BuildAsync(JObject args, CancellationToken ct)
        {
            var configuration = (args["configuration"]?.Value<string>() ?? "Debug").Trim();
            if (string.IsNullOrWhiteSpace(configuration))
                configuration = "Debug";

            try
            {
                await _buildFeedback.BuildAsync(configuration, ct).ConfigureAwait(false);
                var errors = await _buildFeedback.GetCompilationErrorsAsync(ct).ConfigureAwait(false);
                var errorCount = (errors ?? new List<BuildError>()).Count(e => e != null && string.Equals(e.Severity, "Error", StringComparison.OrdinalIgnoreCase));
                return errorCount == 0
                    ? "Build succeeded (" + configuration + ")."
                    : "Build finished with " + errorCount + " error(s) (" + configuration + "). Use read_errors to inspect them.";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "Build failed: " + ex.Message;
            }
        }

        private static string FormatToolLocation(BuildError e)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(e.File)) parts.Add(e.File);
            if (e.Line > 0) parts.Add("line " + e.Line);
            if (e.Column > 0) parts.Add("col " + e.Column);
            return string.Join(", ", parts);
        }

        private Task<string> ListFilesAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p) || !Directory.Exists(p))
                return Task.FromResult("Directory not found: " + p);

            var recursive = args["recursive"]?.Value<bool>() ?? false;

            var dirs = new List<string>();
            var files = new List<string>();
            try
            {
                CollectListEntries(p, p, recursive, dirs, files);
            }
            catch (Exception ex)
            {
                return Task.FromResult("Failed to list directory: " + ex.Message);
            }

            var total = dirs.Count + files.Count;
            var entries = new List<string>(total);
            foreach (var d in dirs) entries.Add(d + "/");
            entries.AddRange(files);

            entries.Sort((a, b) =>
            {
                var aIsDir = a.EndsWith("/", StringComparison.Ordinal);
                var bIsDir = b.EndsWith("/", StringComparison.Ordinal);
                if (aIsDir != bIsDir) return aIsDir ? -1 : 1;
                return StringComparer.OrdinalIgnoreCase.Compare(a, b);
            });

            if (entries.Count == 0)
                return Task.FromResult("No files found.");

            var sb = new StringBuilder();
            foreach (var e in entries.Take(ListFilesLimit))
                sb.AppendLine(e);
            if (total > ListFilesLimit)
                sb.AppendLine().AppendLine("(File list truncated. Use list_files on specific subdirectories to explore further.)");
            return Task.FromResult(Truncate(sb.ToString()));
        }

        private void CollectListEntries(string root, string current, bool recursive, List<string> dirs, List<string> files)
        {
            string[] subdirs;
            string[] fs;
            try
            {
                subdirs = Directory.GetDirectories(current);
                fs = Directory.GetFiles(current);
            }
            catch
            {
                return; // unreadable directory
            }

            foreach (var d in subdirs)
            {
                if (SkippedDirectories.Contains(Path.GetFileName(d)))
                    continue;
                dirs.Add(MakeRelative(root, d).Replace('\\', '/'));
                if (recursive)
                    CollectListEntries(root, d, true, dirs, files);
            }

            foreach (var f in fs)
                files.Add(MakeRelative(root, f).Replace('\\', '/'));
        }


        private Task<string> SearchCodebaseAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return Task.FromResult("No open project directory to search.");

            var queries = ParseQueries(args["queries"]);
            if (queries.Count == 0)
                return Task.FromResult("Missing or invalid \"queries\" argument. Expected an array of regex patterns.");

            var sb = new StringBuilder();
            foreach (var pattern in queries)
            {
                Regex regex;
                try { regex = new Regex(pattern, RegexOptions.IgnoreCase); }
                catch (ArgumentException ex)
                {
                    sb.AppendLine("Invalid regex \"" + pattern + "\": " + ex.Message);
                    continue;
                }

                var matches = new StringBuilder();
                try
                {
                    foreach (var file in EnumerateTextFiles(root))
                    {
                        string[] lines;
                        try { lines = File.ReadAllLines(file); }
                        catch { continue; }

                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (regex.IsMatch(lines[i]))
                                matches.Append(MakeRelative(root, file)).Append(':').Append(i + 1).Append(": ").AppendLine(lines[i]);
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("Search for \"" + pattern + "\" failed: " + ex.Message);
                    continue;
                }

                var text = matches.ToString();
                sb.AppendLine("\"" + pattern + "\":");
                sb.AppendLine(text.Length == 0 ? "(no matches)" : MiddleTruncateLines(text, SearchMaxCharsPerQuery));
            }
            return Task.FromResult(sb.ToString());
        }

        private static List<string> ParseQueries(JToken token)
        {
            var result = new List<string>();
            if (token is JArray array)
            {
                foreach (var item in array)
                {
                    var query = item?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(query))
                        result.Add(query);
                }
            }
            else
            {
                var single = token?.Value<string>();
                if (!string.IsNullOrWhiteSpace(single))
                    result.Add(single);
            }
            return result;
        }

        private Task<string> EditorAsync(JObject args, CancellationToken ct)
        {
            var p = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(p))
                return Task.FromResult("Missing \"path\" argument.");

            try
            {
                var content = args["content"];
                var oldText = args["old_text"]?.Value<string>();
                var newText = args["new_text"]?.Value<string>();

                if (content != null && content.Type != JTokenType.Null)
                {
                    var directory = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    File.WriteAllText(p, content.Value<string>() ?? "", Encoding.UTF8);
                    return Task.FromResult("File written: " + MakeRelative(p));
                }

                if (oldText != null && newText != null)
                {
                    if (!File.Exists(p))
                        return Task.FromResult("File not found: " + p);

                    var text = File.ReadAllText(p, Encoding.UTF8);
                    var index = text.IndexOf(oldText, StringComparison.Ordinal);
                    if (index < 0)
                        return Task.FromResult("The specified old_text was not found in the file.");
                    if (text.IndexOf(oldText, index + 1, StringComparison.Ordinal) >= 0)
                        return Task.FromResult("The specified old_text was found more than once; make it unique before editing.");

                    text = text.Remove(index, oldText.Length).Insert(index, newText);
                    File.WriteAllText(p, text, Encoding.UTF8);
                    return Task.FromResult("File edited: " + MakeRelative(p));
                }

                return Task.FromResult("Provide either \"content\" (full write) or \"old_text\"+\"new_text\" (replace).");
            }
            catch (Exception ex)
            {
                return Task.FromResult("Editor failed: " + ex.Message);
            }
        }


        private async Task<string> GitStatusAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return "No open project directory.";

            try
            {
                var status = await _shell.RunRawAsync("git", "status --porcelain", root, ct).ConfigureAwait(false);
                if (!status.Success)
                    return "git status failed: " + status.Output;
                return string.IsNullOrWhiteSpace(status.Output) ? "(working tree clean)" : status.Output;
            }
            catch (Exception ex)
            {
                return "git status failed: " + ex.Message;
            }
        }

        private async Task<string> GitDiffAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return "No open project directory.";

            var path = args["path"]?.Value<string>();
            var arguments = string.IsNullOrWhiteSpace(path)
                ? "diff HEAD --"
                : "diff HEAD -- " + path;

            try
            {
                var result = await _shell.RunRawAsync("git", arguments, root, ct).ConfigureAwait(false);
                if (!result.Success)
                    return "git diff failed: " + result.Output;
                return string.IsNullOrWhiteSpace(result.Output) ? "(no changes)" : Truncate(result.Output);
            }
            catch (Exception ex)
            {
                return "git diff failed: " + ex.Message;
            }
        }

        private async Task<string> GitCommitAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return "No open project directory.";

            var message = (args["message"]?.Value<string>() ?? "").Trim();
            if (string.IsNullOrEmpty(message))
                return "Missing or empty \"message\" argument.";

            try
            {
                var branch = await _git.CommitAllAsync(root, message, ct).ConfigureAwait(false);
                return "Commit created on branch \"" + branch + "\".";
            }
            catch (Exception ex)
            {
                return "git commit failed: " + ex.Message;
            }
        }

        private async Task<string> RunCommandsAsync(JObject args, CancellationToken ct)
        {
            var root = _getSolutionDir();
            var commands = new List<string>();
            if (args["commands"] is JArray array)
            {
                foreach (var item in array)
                {
                    var c = item?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(c))
                        commands.Add(c);
                }
            }
            else
            {
                var single = args["command"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(single))
                    commands.Add(single);
            }

            if (commands.Count == 0)
                return "Missing \"commands\" argument.";

            var sb = new StringBuilder();
            foreach (var command in commands)
            {
                sb.AppendLine("$ " + command);
                var result = await _shell.RunAsync(command, root, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(result.Output))
                    sb.AppendLine(result.Output);
                if (!result.Success)
                    sb.AppendLine("(exit code " + result.ExitCode + ")");
            }

            return sb.ToString().TrimEnd();
        }


        private async Task<string> FetchWebContentAsync(JObject args, CancellationToken ct)
        {
            var requests = args["requests"] as JArray;
            if (requests == null || requests.Count == 0)
                return "Missing \"requests\" argument.";

            var sb = new StringBuilder();
            foreach (var request in requests)
            {
                var url = request["url"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(url))
                {
                    sb.AppendLine("(request with no url)");
                    continue;
                }

                try
                {
                    using (var client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        var content = await client.DownloadStringTaskAsync(url).ConfigureAwait(false);
                        sb.AppendLine("=== " + url + " ===");
                        sb.AppendLine(Truncate(content));
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine("=== " + url + " (failed: " + ex.Message + ") ===");
                }
            }

            return sb.ToString().TrimEnd();
        }

        private async Task<string> DownloadFileAsync(JObject args, CancellationToken ct)
        {
            var url = args["url"]?.Value<string>();
            var path = ResolvePath(args["path"]?.Value<string>() ?? "");
            if (string.IsNullOrWhiteSpace(url))
                return "Missing \"url\" argument.";
            if (string.IsNullOrWhiteSpace(path))
                return "Missing \"path\" argument.";

            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                using (var client = new WebClient())
                    await client.DownloadFileTaskAsync(url, path).ConfigureAwait(false);

                return "Downloaded to: " + MakeRelative(path);
            }
            catch (Exception ex)
            {
                return "Download failed: " + ex.Message;
            }
        }


        private string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var trimmed = path.Trim();
            if (Path.IsPathRooted(trimmed))
                return trimmed;

            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root))
                return trimmed;

            return Path.Combine(root, trimmed);
        }

        private string MakeRelative(string fullPath)
        {
            var root = _getSolutionDir();
            if (string.IsNullOrWhiteSpace(root))
                return fullPath;

            return MakeRelative(root, fullPath);
        }

        private static string MakeRelative(string root, string path)
        {
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathFull = Path.GetFullPath(path);
            if (string.Equals(rootFull, pathFull, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileName(pathFull);

            var prefix = rootFull + Path.DirectorySeparatorChar;
            if (pathFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return pathFull.Substring(prefix.Length);

            return pathFull;
        }

        private static IEnumerable<string> EnumerateTextFiles(string root)
        {
            var queue = new Queue<string>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                string[] subdirs;
                string[] files;
                try
                {
                    subdirs = Directory.GetDirectories(current);
                    files = Directory.GetFiles(current);
                }
                catch
                {
                    continue;
                }

                foreach (var d in subdirs)
                {
                    if (!SkippedDirectories.Contains(Path.GetFileName(d)))
                        queue.Enqueue(d);
                }

                foreach (var f in files)
                    yield return f;
            }
        }

        private static string ReadRange(string text, int startLine, int endLine)
        {
            if (text == null) return "";
            if (startLine <= 0 && endLine <= 0)
                return LimitLines(text, ReadFilesMaxLines);

            var lines = text.Replace("\r\n", "\n").Split('\n');
            var from = Math.Max(1, startLine <= 0 ? 1 : startLine);
            var to = Math.Min(lines.Length, endLine <= 0 ? lines.Length : endLine);
            if (from > to) return "";

            var sb = new StringBuilder();
            for (var i = from - 1; i < to; i++)
                sb.AppendLine(lines[i]);
            return sb.ToString();
        }

        private static string LimitLines(string text, int maxLines)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length <= maxLines) return text;
            return string.Join("\n", lines.Take(maxLines)) + "\n…(truncated)";
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= ReadFilesMaxChars) return text;
            return text.Substring(0, ReadFilesMaxChars) + "\n…(truncated)";
        }

        private static string MiddleTruncateLines(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            var half = maxChars / 2;
            return text.Substring(0, half) + "\n…(truncated)…\n" + text.Substring(text.Length - half);
        }
    }
}

