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
using RPA.Shared.AgentTools;
using Agent.Core.Contracts;
using Agent.Core.Models;
using RPA.Shared.Models;
using Agent.Core.Services;
using RPA.Shared.Services;
using RPA.Shared.Contracts;
using RPA.Shared.Services.Documents;
using RPA.Chat.Services.Workflow;


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
        private const int ActivitySearchDefaultLimit = 20;
        private const int ActivitySearchMaxLimit = 200;


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
        private readonly IActivityCatalog _activityCatalog;
        private readonly IWorkflowOutlineService _workflowOutline;
        private readonly IWorkflowEditingService _workflowEditing;
        private readonly IWorkflowXamlGate _workflowXamlGate;
        private readonly IWorkflowMcpBridge _workflowMcpBridge;

        /// <summary>Инструменты ТЗ (волна 8): выделены в отдельный класс, чтобы реестр оставался читаемым.</summary>
        private readonly SpecToolHandlers _spec;


        public AgentToolFactory(
            IAiProvider ai,
            Func<string> getSolutionDir,
            IStudioDocumentService documents,
            IBuildFeedbackService buildFeedback,
            Func<string, IReadOnlyList<string>, CancellationToken, Task<string>> askQuestionAsync,
            Func<string, string, CancellationToken, Task<string>> spawnSubAgentAsync,
            bool enableSubagents,
            int subagentMaxDepth,
            Func<string> getDocsDirectory = null,
            IActivityCatalog activityCatalog = null,
            IWorkflowOutlineService workflowOutline = null,
            IWorkflowEditingService workflowEditing = null,
            IWorkflowXamlGate workflowXamlGate = null,
            IWorkflowMcpBridge workflowMcpBridge = null)
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
            _activityCatalog = activityCatalog;
            _workflowOutline = workflowOutline;
            _workflowEditing = workflowEditing;
            _workflowXamlGate = workflowXamlGate;
            _workflowMcpBridge = workflowMcpBridge;
            _spec = new SpecToolHandlers(() => _getSolutionDir());

        }

        public AgentToolRegistry CreateRegistry()
        {
            var registry = new AgentToolRegistry();

            // ---- Read-only tools ----
            registry.Register(new AgentTool(
                "read_files",
                "Read the content of text or image files at the provided absolute paths, or return only an inclusive one-based line range. Prefers the open editor tab content (unsaved changes) when the file is open. For .xaml files a metadata header is added: root element, size, UTF-8 BOM, line endings, indentation, declared xmlns and whether the file is open in the studio designer — match that form when writing the file back with the editor tool.",
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
                "Search the built-in studio user documentation (workflow designer, activities, project management, settings, MCP server, NuGet manager, AI activities) and return matching sections with their relative paths.",
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

            registry.Register(new AgentTool(
                "activity_search",
                "Search the studio activity catalog (real Windows Workflow Foundation activity types available in the designer) by name, full type name, description or category. Returns machine-readable metadata per activity: type name, display name, category, arguments (name, direction, type, required) and child containers. Use it before writing or editing workflow XAML instead of guessing activity names.",
                "{\"query\":\"string\",\"category\":\"string\",\"limit\":\"integer\"}",
                false,
                ActivitySearchAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "activity_describe",
                "Describe one workflow activity from the studio activity catalog: full type name, category, arguments (name, direction, type, required, default value) and child containers (where nested activities can be placed). Call activity_search first to get the exact name.",
                "{\"typeName\":\"string\"}",
                false,
                ActivityDescribeAsync,
                permission: AgentPermissions.ReadFiles));
            registry.Register(new AgentTool(
                "workflow_outline",
                "Read the structure of a workflow process (XAML file) as a tree instead of raw XAML: stable element ids (root, root/Activities[0], root/Then), activity types, display names, arguments with their expressions, variables and Windows Workflow Foundation validation issues bound to element ids. Read the outline before editing a workflow instead of guessing XAML; the same ids are the addresses the editing tools accept.",
                "{\"path\":\"string\",\"depth\":\"integer\",\"include_arguments\":\"boolean\"}",
                false,
                WorkflowOutlineAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "workflow_validate",
                "Validate a workflow process and get a structured report instead of raw XAML: how many errors and warnings, every issue with an element id (the same ids as workflow_outline and the editing tools), the activity property and the check code, and where the structure came from (the designer live model with unsaved edits, or the file on disk). Three states are reported explicitly: no errors, issues with addresses, and \"validation not performed\" (the file was not read or WF validation failed) — the last one is never reported as \"no errors\". Call it after every edit and before building, and repeat it until the answer says there are no errors.",
                "{\"path\":\"string\",\"elementId\":\"string\",\"max_issues\":\"integer\"}",
                false,
                WorkflowValidateAsync,
                permission: AgentPermissions.ReadFiles));



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
                "Edit a file: write full content (path + content), or replace an old string with a new string (path + old_text + new_text). Workflow .xaml files are normalized to the studio form (CRLF, no XML declaration, xmlns:mc/sap/sap2010/mca, mc:Ignorable) and validated after writing; file indentation and expression text are preserved as they are. Writing is refused when the .xaml file is open in the studio designer (use workflow_outline + xaml.add_activity/xaml.set_argument there) and when the XAML is not well-formed.",
                "{\"path\":\"string\",\"content\":\"string\",\"old_text\":\"string\",\"new_text\":\"string\",\"validate\":\"string\"}",
                true,
                EditorAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "build",
                "Build the open project/solution with the given configuration using MSBuild. Optional toolchain: auto (newest installed, default), vs2022 or vs2026.",
                "{\"configuration\":\"string\",\"toolchain\":\"string\"}",
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


            // ---- Создание активности в библиотеке активностей (AG-08) ----
            // Файлы проекта правит служба (RPA.Chat), загрузку сборки в домен и обновление палитры делает
            // хост студии через IActivityPaletteNotifier: без этого тип не виден ни каталогу, ни панели.
            registry.Register(new AgentTool(
                "activity_create",
                "Create a new activity in an EXISTING activity library project (.csproj): writes the class file, an optional designer (Designer.xaml + code-behind) and registers both in the project file; with build=true the project is built first. Atomic: a wrong target framework (net472 is required), a duplicate name, a write error or failing build leaves nothing behind (created files are deleted, the .csproj is restored byte for byte). After a successful build the studio loads the assembly and refreshes the agent catalog and the Activities palette, so activity_search finds the new type without restarting. Creating a project from scratch is out of scope. Read the class file back with read_files to continue the implementation.",
                "{\"projectPath\":\"string\",\"className\":\"string\",\"namespace\":\"string\",\"description\":\"string\",\"category\":\"string\",\"baseType\":\"string\",\"withDesigner\":\"boolean\",\"build\":\"boolean\",\"configuration\":\"string\",\"arguments\":[{\"name\":\"string\",\"typeName\":\"string\",\"direction\":\"string\",\"required\":\"boolean\",\"description\":\"string\",\"category\":\"string\"}]}",
                true,
                ActivityCreateAsync,
                permission: AgentPermissions.EditFiles));

            // ---- ТЗ (волна 8, TZ-01…TZ-09): приём, модель, декомпозиция, трассировка, генерация, тесты ----
            // ТЗ живёт в проекте (<проект>\docs\spec\<slug>\), а не в %AppData%: версии и диффы достаются
            // бесплатно, работа переживает переустановку студии. Каждый инструмент отвечает текстом с адресами.
            registry.Register(new AgentTool(
                "spec_import",
                "Import a technical specification (ТЗ) into the open project: pass \"path\" to a md/txt/docx/pdf/xlsx file or \"text\" with the specification itself. The source is stored byte for byte under <project>\\docs\\spec\\<slug>\\source, every change creates a numbered version in versions\\vNNNN-<sha8>.md, the same sha256 does not create a version, and the answer contains the version, the sha, the converted length and a line diff against the previous version. Formats docx/xlsx/pdf are read into markdown; a scan without a text layer is reported honestly instead of being imported as garbage.",
                "{\"path\":\"string\",\"text\":\"string\",\"name\":\"string\",\"slug\":\"string\",\"note\":\"string\"}",
                true,
                _spec.ImportAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_show",
                "Show the imported specification: either the structured model (goal, preconditions, steps, data, exceptions, acceptance criteria, ambiguities, tasks, test cases, traceability) or the current text of the specification. Pass \"section\" to read one part only (all, goal, steps, data, exceptions, criteria, ambiguities, tasks, testcases, links) — use it for a long specification instead of loading everything into the context. The answer always ends with the model check state: ready, issues with addresses, or \"model not built\".",
                "{\"slug\":\"string\",\"section\":\"string\",\"max_chars\":\"integer\"}",
                false,
                _spec.ShowAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "spec_extract",
                "Save the structured specification model that YOU extracted from the specification text: pass \"model\" (object) or \"json\" (string) with goal, steps (id, text, expected, dataRefs, branches), data (id, name, type, source, example), exceptions, acceptanceCriteria (id, text, check), ambiguities. The tool does not parse the text itself: it validates the model (empty goal, step without expected result, criterion without a check, duplicate ids, dangling data references) and writes specification.json (source of truth) plus specification.md (derived). Blocking issues mean nothing is saved and the answer lists them with addresses like steps[2].expected; force=true saves a draft.",
                "{\"slug\":\"string\",\"title\":\"string\",\"model\":{},\"json\":\"string\",\"force\":\"boolean\"}",
                true,
                _spec.ExtractAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_update",
                "Patch the saved specification model without rewriting it: pass \"model\" (object) or \"json\" (string) with only the fields to change (arrays are replaced, objects are merged). Use it for corrections after spec_show, for filling acceptance criteria or for updating the source reference. Blocking validation issues keep the change out of the file and are reported with addresses; force=true saves a draft.",
                "{\"slug\":\"string\",\"model\":{},\"json\":\"string\",\"force\":\"boolean\"}",
                true,
                _spec.UpdateAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_decompose",
                "Save the task decomposition of the specification: pass \"tasks\" (array) or \"json\" with tasks (id, title, specRefs to step/criterion ids, status, activityHints, openQuestions, steps[{activityType, parent, container, argumentName, argumentValue}]). Validation is the point of the tool: every task must reference an existing item, every step and every acceptance criterion must be covered by at least one task, otherwise the answer names the uncovered addresses and (without force=true) nothing is saved.",
                "{\"slug\":\"string\",\"tasks\":[],\"json\":\"string\",\"force\":\"boolean\"}",
                true,
                _spec.DecomposeAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_link",
                "Trace one specification item to one workflow element: \"itemId\" (step or criterion id) and \"elementId\" (the same address workflow_outline returns, e.g. root/Activities[0]), plus \"path\" to the process. The item and the element are both checked against the model and the real process structure — a label to a deleted item or a deleted element is rejected instead of being stored. The answer contains the updated coverage report.",
                "{\"slug\":\"string\",\"itemId\":\"string\",\"elementId\":\"string\",\"path\":\"string\",\"note\":\"string\"}",
                true,
                _spec.LinkAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_coverage",
                "Report how much of the specification is covered by the workflow: which steps and acceptance criteria have traceability labels and which do not (with addresses), plus broken labels (item or element deleted). Three states are named explicitly: everything covered, uncovered items with addresses, and \"traceability not performed\" with the reason (no process, no model). Read the report before generating a robot so nothing is silently skipped.",
                "{\"slug\":\"string\",\"path\":\"string\"}",
                false,
                _spec.CoverageAsync,
                permission: AgentPermissions.ReadFiles));

            registry.Register(new AgentTool(
                "spec_generate",
                "Generate the robot skeleton from the specification: without \"apply\" it only builds the plan (operations per task, what is skipped as already generated, what needs a decision: uncovered steps, tasks without steps, open questions, activity types missing in the studio catalog). With apply=true the plan is applied in atomic batches through WorkflowEditRunner (one task = one undo step) with workflow_validate after each batch; a failing batch stops the run and is reverted as a whole. Requires the process to be open in the designer: edits go to the live model, not to the file text.",
                "{\"slug\":\"string\",\"path\":\"string\",\"apply\":\"boolean\"}",
                true,
                _spec.GenerateAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_ask",
                "Record an ambiguity of the specification in its journal: \"question\", optional \"itemId\" it belongs to and \"options\". The agent still asks the user with ask_question (2-5 options) — this tool keeps the journal so the answer is not lost between turns and reaches the generation report.",
                "{\"slug\":\"string\",\"id\":\"string\",\"itemId\":\"string\",\"question\":\"string\",\"options\":[\"string\"]}",
                true,
                _spec.AskAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_answer",
                "Close an ambiguity from the journal: \"id\" of the question and \"answer\". The answer is stored with the author and timestamp and the item becomes resolved. Pass \"text\" with the revised specification text to create a new specification version — the version number, the sha and the diff come back in the answer.",
                "{\"slug\":\"string\",\"id\":\"string\",\"answer\":\"string\",\"answered_by\":\"string\",\"text\":\"string\"}",
                true,
                _spec.AnswerAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_testcases",
                "Save acceptance test cases built from the acceptance criteria: \"testCases\" (array) or \"json\" with cases (id, criterionId, inputs, expectedOutputs, negative, note). Every criterion must have at least one case and every case must reference an existing criterion, otherwise the answer names the uncovered criteria and nothing is saved (force=true saves a draft).",
                "{\"slug\":\"string\",\"testCases\":[],\"json\":\"string\",\"force\":\"boolean\"}",
                true,
                _spec.TestCasesAsync,
                permission: AgentPermissions.EditFiles));

            registry.Register(new AgentTool(
                "spec_run_tests",
                "Run the specification test cases and produce the acceptance checklist: each case gets passed, failed or \"not run\" (never reported as passed), the details name the mismatching output arguments. The run goes through the workflow engine behind IWorkflowRunService; the report is written to <project>\\docs\\spec\\<slug>\\runs\\<stamp>.json and .md. Needs \"path\" to the workflow file that is executed.",
                "{\"slug\":\"string\",\"path\":\"string\"}",
                true,
                _spec.RunTestsAsync,
                permission: AgentPermissions.ExecuteCommand));

            // ---- MCP-инструменты способов написания WF (план «3 галочки», Фаза 5) ----
            // Чат видит только IWorkflowMcpBridge; каждый MCP-инструмент регистрируется обычным AgentTool
            // с Way-меткой, чтобы фильтр способа (Фаза 7) мог спрятать его при выключенной галочке.
            foreach (WorkflowMcpToolInfo tool in McpBridge.ListTools())
            {
                if (string.IsNullOrWhiteSpace(tool.Name))
                {
                    continue;
                }

                string toolName = tool.Name;
                registry.Register(new AgentTool(
                    toolName,
                    tool.Description ?? "",
                    tool.ParametersSchema ?? "{}",
                    tool.Mutates,
                    (args, ct) => McpBridge.CallAsync(toolName, args?.ToString() ?? "{}", ct),
                    permission: tool.Mutates ? AgentPermissions.EditFiles : AgentPermissions.ReadFiles,
                    way: tool.Way));
            }

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
            var shortTask = task.Length > 60 ? SafeText.Truncate(task, 60) + "…" : task;
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
                    AppendXamlHeader(sb, p, buffer, "буфер редактора");
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
                    // ТЗ-02: docx/pdf/xlsx извлекаются общим reader'ом (одна логика с spec_import и вложениями чата),
                    // а не читаются как текст: раньше zip-мусор docx попадал в контекст агента.
                    var document = DocumentTextReaders.IsBinaryDocument(p)
                        ? DocumentTextReaders.Read(p)
                        : null;
                    if (document != null)
                    {
                        sb.AppendLine(document.Describe());
                        if (document.Ok)
                        {
                            sb.AppendLine(Truncate(ReadRange(document.Text, startLine, endLine)));
                        }

                        continue;
                    }

                    var text = File.ReadAllText(p);
                    AppendXamlHeader(sb, p, text, "файл на диске");
                    sb.AppendLine(Truncate(ReadRange(text, startLine, endLine)));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("Failed to read file: " + ex.Message);
                }
            }

            return Task.FromResult(sb.ToString().TrimEnd());
        }

        /// <summary>
        /// Шапка метаданных для <c>.xaml</c> (карточка <c>AG-07</c>): кодировка/BOM, EOL, отступ, корень,
        /// объявленные <c>xmlns</c> и «открыт в конструкторе». Без неё агент не знает, что файл с BOM или
        /// с одиночными LF, и портит его первой же перезаписью.
        /// </summary>
        private void AppendXamlHeader(StringBuilder sb, string path, string text, string source)
        {
            if (!WorkflowXamlText.IsWorkflowXamlFile(path))
                return;

            sb.AppendLine(WorkflowXamlText.Describe(text, HasUtf8Bom(path), source, IsOpenInDesigner(path)));
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

        /// <summary>
        /// Activity catalog used by <c>activity_search</c>/<c>activity_describe</c> (card <c>AG-01</c>).
        /// The constructor argument wins over the ambient hub so tests can inject a fake catalog while the
        /// studio injects the real one built from its activity libraries.
        /// </summary>
        private IActivityCatalog Catalog => _activityCatalog ?? ChatEnvironment.ActivityCatalog;

        private const string CatalogUnavailableMessage =
            "Activity catalog is unavailable: the studio host did not provide one. " +
            "The catalog is built by the studio from its loaded activity libraries, so run the chat inside the studio.";

        private Task<string> ActivitySearchAsync(JObject args, CancellationToken ct)
        {
            IActivityCatalog catalog = Catalog;
            if (catalog == null || catalog.Count == 0)
                return Task.FromResult(CatalogUnavailableMessage);

            var query = (args["query"]?.Value<string>() ?? string.Empty).Trim();
            var category = (args["category"]?.Value<string>() ?? string.Empty).Trim();

            var limit = ActivitySearchDefaultLimit;
            JToken limitToken = args["limit"];
            if (limitToken != null && limitToken.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(limitToken.ToString()))
            {
                if (!int.TryParse(limitToken.ToString(), out limit) || limit <= 0)
                    return Task.FromResult("Invalid \"limit\" argument: expected a positive integer.");
                if (limit > ActivitySearchMaxLimit)
                    limit = ActivitySearchMaxLimit;
            }

            try
            {
                IReadOnlyList<ActivityInfo> found = catalog.Search(
                    query,
                    string.IsNullOrEmpty(category) ? null : category,
                    limit);
                return Task.FromResult(ActivityCatalogText.DescribeSearch(query, category, found, catalog.Count, limit));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"activity_search failed: {ex}");
            }
        }

        private Task<string> ActivityDescribeAsync(JObject args, CancellationToken ct)
        {
            IActivityCatalog catalog = Catalog;
            if (catalog == null || catalog.Count == 0)
                return Task.FromResult(CatalogUnavailableMessage);

            var typeName = (args["typeName"]?.Value<string>() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(typeName))
                return Task.FromResult("Missing or empty \"typeName\" argument. Use activity_search(query) to find the exact name.");

            try
            {
                ActivityInfo activity = catalog.Describe(typeName);
                if (activity == null)
                    return Task.FromResult(ActivityCatalogText.DescribeUnknown(typeName, catalog.Search(typeName, null, 5)));

                return Task.FromResult(ActivityCatalogText.DescribeActivity(activity));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"activity_describe failed: {ex}");
            }
        }


        /// <summary>
        /// Workflow outline service used by <c>workflow_outline</c> (card <c>AG-03</c>). The constructor
        /// argument wins over the ambient hub so tests can inject a fake service while the studio injects
        /// the real one built on top of WF loading and validation.
        /// </summary>
        private IWorkflowOutlineService OutlineService => _workflowOutline ?? ChatEnvironment.WorkflowOutline;

        private const string OutlineUnavailableMessage =
            "Workflow outline service is unavailable: the studio host did not provide one. " +
            "The outline is built by the studio (it owns WF loading and validation), so run the chat inside the studio; " +
            "as a fallback read the XAML file with read_files.";

        private Task<string> WorkflowOutlineAsync(JObject args, CancellationToken ct)
        {
            var rawPath = (args["path"]?.Value<string>() ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(rawPath))
                return Task.FromResult("Missing or empty \"path\" argument: pass the path of a workflow XAML file.");

            IWorkflowOutlineService service = OutlineService;
            if (service == null)
                return Task.FromResult(OutlineUnavailableMessage);

            var depth = 0;
            JToken depthToken = args["depth"];
            if (depthToken != null && depthToken.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(depthToken.ToString()))
            {
                if (!int.TryParse(depthToken.ToString(), out depth) || depth < 0)
                    return Task.FromResult("Invalid \"depth\" argument: expected a non-negative integer (0 = whole tree).");
            }

            var includeArguments = true;
            JToken includeToken = args["include_arguments"];
            if (includeToken != null && includeToken.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(includeToken.ToString()))
            {
                if (!bool.TryParse(includeToken.ToString(), out includeArguments))
                    return Task.FromResult("Invalid \"include_arguments\" argument: expected true or false.");
            }

            try
            {
                WorkflowOutline outline = service.GetOutline(ResolvePath(rawPath), depth, includeArguments);
                if (outline == null)
                    return Task.FromResult(OutlineUnavailableMessage);

                return Task.FromResult(WorkflowOutlineText.Describe(outline));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"workflow_outline failed: {ex}");
            }
        }

        /// <summary>
        /// Вердикт валидации процесса (карточка <c>AG-06</c>). Отдельной службы валидации нет намеренно:
        /// отчёт строится над тем же <see cref="IWorkflowOutlineService"/>, что и <c>workflow_outline</c>,
        /// поэтому адреса, тексты и свойства замечаний у чтения и у валидации всегда одни и те же.
        /// </summary>
        private Task<string> WorkflowValidateAsync(JObject args, CancellationToken ct)
        {
            var rawPath = ArgText(args, "path");
            if (string.IsNullOrEmpty(rawPath))
                return Task.FromResult("Missing or empty \"path\" argument: pass the path of a workflow XAML file.");

            IWorkflowOutlineService service = OutlineService;
            if (service == null)
                return Task.FromResult(OutlineUnavailableMessage);

            var maxIssues = WorkflowOutlineText.MaxIssues;
            JToken maxToken = args["max_issues"] ?? args["maxIssues"];
            if (maxToken != null && maxToken.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(maxToken.ToString()))
            {
                if (!int.TryParse(maxToken.ToString().Trim(), out maxIssues) || maxIssues < 0)
                    return Task.FromResult(
                        "Invalid \"max_issues\" argument: expected a non-negative integer (0 = counts only).");
            }

            var elementId = ArgText(args, "elementId") ?? ArgText(args, "element_id");

            try
            {
                // Аргументы не нужны: вердикт валидации от них не зависит, а ответ не должен занимать
                // контекст модели. Дерево и адреса при этом те же, что у workflow_outline.
                WorkflowOutline outline = service.GetOutline(ResolvePath(rawPath), 0, false);
                if (outline == null)
                    return Task.FromResult(OutlineUnavailableMessage);

                WorkflowValidationReport report = WorkflowValidationReport.FromOutline(outline, elementId, maxIssues);

                return Task.FromResult(WorkflowValidationText.Describe(report));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"workflow_validate failed: {ex}");
            }
        }

        // ---- Правка открытого процесса (AG-04, AG-09) ----

        /// <summary>
        /// Мост правок (AG-02). Аргумент конструктора важнее окружения: тесты подставляют фейковую сессию,
        /// студия — живой конструктор.
        /// </summary>
        private IWorkflowEditingService Editing => _workflowEditing ?? ChatEnvironment.WorkflowEditing;

        private const string EditingUnavailableMessage =
            "Workflow editing service is unavailable: the studio host did not provide one. " +
            "Edits go to the model of a workflow tab open in the designer (the studio owns it), so open the file " +
            "in the studio designer and retry; as a fallback edit the XAML file with the editor tool.";

        private static string ArgText(JObject args, string name)
        {
            JToken token = args?[name];
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                return null;

            var value = token.ToString().Trim();
            return value.Length == 0 ? null : value;
        }

        /// <summary>Неотрицательный индекс; отсутствие аргумента — «в конец».</summary>
        private static IActivityPaletteNotifier PaletteNotifier => ChatEnvironment.ActivityPaletteNotifier;

        /// <summary>
        /// Создание активности в существующем проекте библиотеки активностей (карточка <c>AG-08</c>).
        /// Служба делает файлы, <c>.csproj</c> и откат; инструмент добавляет к ответу состояние палитры:
        /// без загрузки сборки в домен тип не виден ни каталогу, ни панели «Активности».
        /// </summary>
        private async Task<string> ActivityCreateAsync(JObject args, CancellationToken ct)
        {
            var rawPath = ArgText(args, "projectPath") ?? ArgText(args, "project");
            if (rawPath == null)
                return "Missing \"projectPath\": pass the path of the activity library project (.csproj) where the activity is created.";

            var request = new ActivityCreateRequest
            {
                ProjectPath = ResolvePath(rawPath),
                ClassName = ArgText(args, "className") ?? ArgText(args, "name"),
                Namespace = ArgText(args, "namespace"),
                Description = ArgText(args, "description"),
                Category = ArgText(args, "category"),
                BaseType = ArgText(args, "baseType"),
                Configuration = ArgText(args, "configuration")
            };

            if (!TryArgBool(args, "withDesigner", out bool? withDesigner, out string flagError))
                return flagError;
            if (withDesigner.HasValue)
                request.WithDesigner = withDesigner.Value;

            if (!TryArgBool(args, "build", out bool? build, out flagError))
                return flagError;
            if (build.HasValue)
                request.Build = build.Value;

            if (args["arguments"] is JArray items)
            {
                foreach (JToken token in items)
                {
                    if (!(token is JObject item))
                        return "Invalid \"arguments\" item: every item must be an object with a \"name\".";

                    request.Arguments.Add(new ActivityCreateArgument
                    {
                        Name = ArgText(item, "name"),
                        TypeName = ArgText(item, "typeName") ?? ArgText(item, "type"),
                        Direction = ArgText(item, "direction"),
                        Required = item["required"] != null && item["required"].Type == JTokenType.Boolean && item["required"].Value<bool>(),
                        Description = ArgText(item, "description"),
                        Category = ArgText(item, "category")
                    });
                }
            }

            try
            {
                var service = new ActivityCreationService(_buildFeedback);
                ActivityCreateResult result = await service.CreateAsync(request, ct).ConfigureAwait(false);

                ActivityPaletteRefreshResult palette = null;
                if (result.Succeeded && !string.IsNullOrWhiteSpace(result.AssemblyPath))
                    palette = (PaletteNotifier ?? NullActivityPaletteNotifier.Instance).Notify(result.AssemblyPath);

                return ActivityCreateText.Describe(result, palette);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return "activity_create failed: " + ex;
            }
        }

        /// <summary>Логический аргумент инструмента: отсутствие — «не задан», мусор — ошибка запроса.</summary>
        private static bool TryArgBool(JObject args, string name, out bool? value, out string error)
        {
            value = null;
            error = null;

            JToken token = args?[name];
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                return true;

            if (token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }

            var text = token.ToString().Trim();
            if (text.Length == 0)
                return true;

            if (bool.TryParse(text, out bool parsed))
            {
                value = parsed;
                return true;
            }

            error = "Invalid \"" + name + "\" argument: expected true or false.";
            return false;
        }

        private async Task<string> BuildAsync(JObject args, CancellationToken ct)
        {
            var configuration = (args["configuration"]?.Value<string>() ?? "Debug").Trim();
            if (string.IsNullOrWhiteSpace(configuration))
                configuration = "Debug";
            // Optional explicit toolchain (CP-04): the caller may pin VS 2022 or VS 2026 instead of "newest installed".
            var requestedToolchain = args["toolchain"]?.Value<string>();
            var toolchainInfo = "";
            if (_buildFeedback is IBuildToolchainAware toolchainAware)
            {
                if (!string.IsNullOrWhiteSpace(requestedToolchain))
                    toolchainAware.MsBuildToolchain = requestedToolchain.Trim();
                toolchainInfo = " Toolchain: " + toolchainAware.DescribeToolchain();
            }


            try
            {
                await _buildFeedback.BuildAsync(configuration, ct).ConfigureAwait(false);
                var errors = await _buildFeedback.GetCompilationErrorsAsync(ct).ConfigureAwait(false);
                var errorCount = (errors ?? new List<BuildError>()).Count(e => e != null && string.Equals(e.Severity, "Error", StringComparison.OrdinalIgnoreCase));
                return errorCount == 0
                    ? "Build succeeded (" + configuration + ")." + toolchainInfo
                    : "Build finished with " + errorCount + " error(s) (" + configuration + "). Use read_errors to inspect them." + toolchainInfo;
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

                string text;
                var replaced = false;

                if (content != null && content.Type != JTokenType.Null)
                {
                    text = content.Value<string>() ?? string.Empty;
                }
                else if (oldText != null && newText != null)
                {
                    if (!File.Exists(p))
                        return Task.FromResult("File not found: " + p);

                    var current = File.ReadAllText(p, Encoding.UTF8);
                    var index = current.IndexOf(oldText, StringComparison.Ordinal);
                    if (index < 0)
                        return Task.FromResult("The specified old_text was not found in the file.");
                    if (current.IndexOf(oldText, index + 1, StringComparison.Ordinal) >= 0)
                        return Task.FromResult("The specified old_text was found more than once; make it unique before editing.");

                    text = current.Remove(index, oldText.Length).Insert(index, newText);
                    replaced = true;
                }
                else
                {
                    return Task.FromResult("Provide either \"content\" (full write) or \"old_text\"+\"new_text\" (replace).");
                }

                // XAML рабочего процесса пишется по тем же правилам, что и у сериализатора студии (AG-07):
                // отдельного xaml_write нет, поэтому нормализация встроена в единственный путь текстовой правки.
                if (WorkflowXamlText.IsWorkflowXamlFile(p))
                    return Task.FromResult(WriteWorkflowXaml(p, text, args, replaced));

                var directory = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(p, text, Encoding.UTF8);

                return Task.FromResult((replaced ? "File edited: " : "File written: ") + MakeRelative(p));
            }
            catch (Exception ex)
            {
                return Task.FromResult("Editor failed: " + ex.Message);
            }
        }

        /// <summary>Сколько замечаний валидации вернуть текстом; остальные — числом.</summary>
        private const int XamlIssuesInResponse = 10;

        /// <summary>
        /// Запись <c>.xaml</c> (карточка <c>AG-07</c>): нормализация канонического вида, отказ при файле,
        /// открытом конструктором, и валидация тем же гейтом, что у генерации процессов. Режимы
        /// <c>validate</c>: <c>off</c> — не проверять, <c>report</c> (по умолчанию) — записать и вернуть
        /// замечания, <c>strict</c> — откатить файл, если проверка не прошла или не выполнялась.
        /// </summary>
        private string WriteWorkflowXaml(string path, string text, JObject args, bool replaced)
        {
            var mode = (ArgText(args, "validate") ?? "report").Trim().ToLowerInvariant();
            if (mode != "off" && mode != "report" && mode != "strict")
                return "Invalid \"validate\" argument: expected \"off\", \"report\" (default) or \"strict\".";

            if (IsOpenInDesigner(path))
            {
                return "File not written: " + MakeRelative(path) + " is open in the studio designer. " +
                       "Edits live in the designer model and the file is saved from it, so a text write would be lost. " +
                       "Use workflow_outline for addresses and xaml.add_activity / xaml.delete_activity / xaml.move_activity / " +
                       "xaml.wrap_activity / xaml.set_argument / xaml.add_variable for changes, then save the designer tab.";
            }

            WorkflowXamlNormalizeResult normalized = WorkflowXamlText.Normalize(text);
            if (normalized.Kind == WorkflowXamlKind.Malformed)
            {
                return "File not written: the XAML is not well-formed XML (" + normalized.Error + "). " +
                       "Nothing on disk was changed: fix the markup and write it again.";
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var existed = File.Exists(path);
            var previous = existed ? File.ReadAllBytes(path) : null;
            var bom = existed && HasUtf8Bom(path);

            File.WriteAllText(path, normalized.Text, new UTF8Encoding(bom));

            var sb = new StringBuilder();
            sb.Append(replaced ? "File edited: " : "File written: ").Append(MakeRelative(path));
            sb.AppendLine();
            sb.Append("xaml: ").Append(DescribeNormalization(normalized))
              .Append(bom ? "; BOM preserved" : "; UTF-8 without BOM");

            if (mode == "off")
            {
                sb.AppendLine();
                sb.Append("validation: skipped (validate=off)");
                return sb.ToString();
            }

            WorkflowXamlGateResult gate;
            try
            {
                IWorkflowXamlGate xamlGate = XamlGate ?? NullWorkflowXamlGate.Instance;
                gate = xamlGate.ValidateText(normalized.Text) ?? WorkflowXamlGateResult.NotPerformed("гейт не вернул результат");
            }
            catch (Exception ex)
            {
                gate = WorkflowXamlGateResult.NotPerformed("валидация упала: " + ex.Message);
            }

            if (!gate.Validated)
            {
                if (mode == "strict")
                {
                    RestoreFile(path, previous);
                    sb.AppendLine();
                    sb.Append("Write reverted: validation was not performed (").Append(gate.Reason)
                      .Append("), so strict mode cannot confirm the XAML. ")
                      .Append(previous == null ? "The created file was deleted." : "The file was restored to its previous content.");
                    return sb.ToString();
                }

                sb.AppendLine();
                sb.Append("validation: not performed — ").Append(gate.Reason).Append(" (this is not \"no errors\")");
                return sb.ToString();
            }

            if (gate.Ok)
            {
                sb.AppendLine();
                sb.Append("validation: ok (0 errors")
                  .Append(gate.Warnings.Count > 0 ? ", " + gate.Warnings.Count + " warning(s)" : string.Empty)
                  .Append(")");
                return sb.ToString();
            }

            if (mode == "strict")
            {
                RestoreFile(path, previous);
                sb.AppendLine();
                sb.Append("Write reverted: XAML validation failed (").Append(gate.Errors.Count).Append(" error(s)); ")
                  .Append(previous == null ? "the created file was deleted." : "the file was restored to its previous content.");
                AppendGateIssues(sb, gate);
                return sb.ToString();
            }

            sb.AppendLine();
            sb.Append("validation: ").Append(gate.Errors.Count).Append(" error(s)");
            AppendGateIssues(sb, gate);
            sb.AppendLine();
            sb.Append("hint: fix the issues and write again, or write with validate=\"strict\" to have rejected XAML reverted.");
            return sb.ToString();
        }

        private static string DescribeNormalization(WorkflowXamlNormalizeResult normalized)
        {
            if (normalized.Kind == WorkflowXamlKind.OtherXml)
                return "left as is (" + string.Join("; ", normalized.Notes) + ")";

            if (!normalized.Changed)
                return "already canonical";

            return normalized.Notes.Count == 0
                ? "normalized"
                : "normalized (" + string.Join("; ", normalized.Notes) + ")";
        }

        private static void AppendGateIssues(StringBuilder sb, WorkflowXamlGateResult gate)
        {
            IReadOnlyList<string> errors = gate.Errors ?? new string[0];
            foreach (var error in errors.Take(XamlIssuesInResponse))
            {
                sb.AppendLine();
                sb.Append("  - ").Append(error);
            }

            if (errors.Count > XamlIssuesInResponse)
            {
                sb.AppendLine();
                sb.Append("  … ").Append(errors.Count - XamlIssuesInResponse).Append(" more");
            }
        }

        private static void RestoreFile(string path, byte[] previous)
        {
            if (previous != null)
            {
                File.WriteAllBytes(path, previous);
                return;
            }

            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Гейт валидации XAML (AG-07). Аргумент конструктора важнее окружения: тесты подставляют фейк,
        /// студия — тот же гейт, которым валидируются генерируемые процессы.
        /// </summary>
        private IWorkflowXamlGate XamlGate => _workflowXamlGate ?? ChatEnvironment.WorkflowXamlGate;

        /// <summary>
        /// Мост MCP (план «3 галочки»). Аргумент конструктора важнее окружения: тесты подставляют фейк,
        /// студия — встроенный <c>StudioWorkflowMcpBridge</c> поверх singleton <c>RpaMcpServer</c>.
        /// </summary>
        private IWorkflowMcpBridge McpBridge => _workflowMcpBridge ?? ChatEnvironment.WorkflowMcpBridge;

        /// <summary>Файл открыт вкладкой конструктора студии: правки живут в модели и перекроют текст файла.</summary>
        private bool IsOpenInDesigner(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                IWorkflowEditingService editing = Editing;
                return editing != null && editing.IsOpenInDesigner(path);
            }
            catch (Exception)
            {
                // Сбой моста не должен блокировать запись: считаем файл не открытым.
                return false;
            }
        }

        /// <summary>Файл начинается с BOM UTF-8 (в файлах студии BOM нет, но у пользовательских он бывает).</summary>
        private static bool HasUtf8Bom(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return false;

                using (var stream = File.OpenRead(path))
                {
                    return stream.ReadByte() == 0xEF && stream.ReadByte() == 0xBB && stream.ReadByte() == 0xBF;
                }
            }
            catch (Exception)
            {
                return false;
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
            return SafeText.Truncate(text, ReadFilesMaxChars) + "\n…(truncated)";
        }

        private static string MiddleTruncateLines(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxChars) return text;
            var half = maxChars / 2;
            return SafeText.Truncate(text, half) + "\n…(truncated)…\n" + SafeText.Tail(text, half);
        }
    }
}

