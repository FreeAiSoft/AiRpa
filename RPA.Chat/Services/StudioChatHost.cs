using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Contracts;
using Agent.Core.Context;
using Agent.Core.Models;
using Agent.Core.Services;
using Agent.Models;
using Agent.Toolkit.Wpf.Controls;
using RPA.Shared.AgentTools;
using RPA.Shared.Models;
using RPA.Shared.Services;
using RPA.Chat.Services.AgentTools;

namespace RPA.Chat.Services
{
    /// <summary>
    /// Реализация <see cref="IChatHost"/> для студии. Владеет агентским циклом, инструментами,
    /// историей, документацией и настройками; не трогает WPF-контролы напрямую — сообщения идут в
    /// <see cref="ChatModel"/>, а представление (<see cref="Agent.Toolkit.Wpf.Controls.ChatControl"/>)
    /// отображает их само. UI-действия (ввод текста, рендер документации) хост передаёт через
    /// делегаты, которые подключает <see cref="Views.ChatHostView"/>.
    /// </summary>
    public sealed class StudioChatHost : IChatHost
    {
        private const string SkipAnswerText = "User skipped the question.";

        private const int ErrorContextLines = 15;
        private const int MaxErrorsWithContext = 10;

        private IAiProvider _ai;
        private IBuildFeedbackService _buildFeedback;
        private readonly GitService _git = new GitService();
        private AgentToolRegistry _toolRegistry;
        private AgentRunner _agentRunner;
        private readonly ContextUsageBinder _contextUsageBinder;
        private CancellationTokenSource _chatCts;
        private TaskCompletionSource<string> _questionTcs;
        private ChatMessage _activeQuestion;
        private readonly DocumentationViewer _docsViewer = new DocumentationViewer();
        private string _docsCurrentPath = DocumentationViewer.IndexFileName;

        public StudioChatHost(ChatModel model, SettingsData settings, ISettingsStore settingsPersistence = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            Settings = settings ?? new SettingsData();
            SettingsPersistence = settingsPersistence;

            RpaAgentOptions.Load();
            _contextUsageBinder = new ContextUsageBinder(Model, SynchronizationContext.Current);
            _buildFeedback = new MsBuildFeedbackService(GetSolutionDirectory);
            RecreateAiProvider();
            RebuildAgentTools();
        }

        public ChatModel Model { get; }

        public SettingsData Settings { get; }

        public ISettingsStore SettingsPersistence { get; }

        /// <summary>Пишет текст в поле ввода (подключается представлением).</summary>
        public Action<string> InputWriter { get; set; }

        /// <summary>Рендерит markdown-документацию (подключается представлением).</summary>
        public Action<string, string> DocsRenderer { get; set; }

        // ------------------------------------------------------------------
        // IChatHost: agent run
        // ------------------------------------------------------------------

        public async Task SendChatAsync(string text, IReadOnlyList<ChatAttachment> attachments)
        {
            if (_chatCts != null || _agentRunner == null) return;

            var images = new List<ChatImagePart>();
            var notes = new List<string>();
            var fileBlocks = new StringBuilder();

            if (attachments != null)
            {
                foreach (var a in attachments)
                {
                    if (a == null) continue;
                    if (a.IsImage)
                    {
                        images.Add(a.ToImagePart());
                        notes.Add("Image: " + a.FileName);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(a.TextContent))
                        {
                            fileBlocks.AppendLine().AppendLine("Attached file: " + a.FileName)
                                .AppendLine("```").AppendLine(a.TextContent).AppendLine("```");
                        }
                        notes.Add("File: " + a.FileName);
                    }
                }
            }

            var promptText = (text ?? "").Trim();
            if (fileBlocks.Length > 0)
                promptText = string.IsNullOrWhiteSpace(promptText)
                    ? fileBlocks.ToString().TrimEnd()
                    : promptText.TrimEnd() + Environment.NewLine + fileBlocks.ToString().TrimEnd();

            var uiText = (text ?? "").Trim();
            if (notes.Count > 0)
                uiText = string.IsNullOrWhiteSpace(uiText)
                    ? string.Join("  ", notes)
                    : uiText + Environment.NewLine + string.Join("  ", notes);

            if (string.IsNullOrWhiteSpace(promptText) && images.Count == 0)
                return;

            var modeName = Model.Mode.ToString();
            var history = BuildTranscript();
            Model.EnsureActiveConversation();
            AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.User, Text = uiText });

            var thinking = new ChatMessage { Kind = ChatMessageKind.AgentText, Mode = modeName, IsThinking = true, IsPartial = true, Text = "Thinking…" };
            AppendChatMessage(thinking);

            var cts = new CancellationTokenSource();
            _chatCts = cts;
            Model.StartStatus();
            try
            {
                var progress = new Progress<ChatStreamDelta>(delta =>
                {
                    if (!string.IsNullOrEmpty(delta.ReasoningDelta))
                        thinking.Reasoning += delta.ReasoningDelta;
                });
                var result = await _agentRunner.RunAsync(history, promptText, images, Model.Mode, BuildContextBudget(), cts.Token, progress);

                thinking.IsThinking = false;
                thinking.IsPartial = false;
                if (string.IsNullOrWhiteSpace(thinking.Reasoning) && !string.IsNullOrWhiteSpace(result.Reasoning))
                    thinking.Reasoning = result.Reasoning;

                if (thinking.HasReasoning) thinking.Text = string.Empty;
                else Model.Messages.Remove(thinking);

                if (!string.IsNullOrWhiteSpace(result.FinalText))
                {
                    AppendChatMessage(new ChatMessage
                    {
                        Kind = Model.Mode == AgentMode.Act ? ChatMessageKind.ResultAnswerAct : ChatMessageKind.ResultAnswerPlan,
                        Mode = modeName,
                        Text = result.FinalText
                    });
                }
            }
            catch (OperationCanceledException)
            {
                thinking.IsThinking = false;
                if (Model.Messages.Contains(thinking)) AppendChat("Request cancelled.");
            }
            catch (Exception ex)
            {
                thinking.IsThinking = false;
                AppendExtensionError(ex);
                ChatLog.Error(ex, "Agent chat request failed.");
            }
            finally
            {
                _chatCts = null;
                cts.Dispose();
                Model.StopStatus();
                SaveHistoryNow();
                Model.PersistUiState(MainTab.Chat);
            }
        }

        public void CancelAgentRun()
        {
            _chatCts?.Cancel();
        }

        public Task ResumeAgentRunAsync()
        {
            // Резюм после отмены в текущем агентском цикле не поддержан (см. ChatViewModel); флаг
            // Model.HasResumableRun хост не выставляет, поэтому кнопка Resume в ChatControl не видна.
            return Task.CompletedTask;
        }

        public bool IsAgentRunning => _chatCts != null;

        // ------------------------------------------------------------------
        // IChatHost: host actions
        // ------------------------------------------------------------------

        public void OpenCommitControl()
        {
            var solutionDir = GetSolutionDirectory();
            if (string.IsNullOrWhiteSpace(solutionDir))
            {
                AppendChat("Commit is unavailable: no open solution.");
                return;
            }

            _ = CheckCommitBranchAndNotifyAsync(solutionDir);
        }

        public void OpenCreateRobot()
        {
            AppendChat("Create robot: use the create_robot tool or the studio project commands.");
        }

        public async void SendBuildErrors()
        {
            await SendErrorsAsync();
        }

        public void RebuildAgentTools()
        {
            UnhookAgentRunner();
            _toolRegistry = BuildToolRegistry();
            var toolCallingMode = string.Equals(Settings.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowed = BuildAllowedPermissions();
            var allowedWays = BuildAllowedWays();

            _agentRunner = new AgentRunner(
                _ai,
                _toolRegistry,
                m => AgentPromptBuilder.Build(m, _toolRegistry, toolCallingMode, allowed, allowedWays, Settings.UserName, Settings.AddressByName, ParseMemoryBankMode()),
                BuildContextBudget(),
                toolCallingMode: toolCallingMode,
                allowedPermissions: allowed,
                allowedWays: allowedWays);
            _agentRunner.ToolExecuted += OnAgentToolExecuted;
            _agentRunner.ContextCompacted += OnAgentContextCompacted;
            _agentRunner.RetryOccurred += OnAgentRetryOccurred;
        }

        public void StartNewChat()
        {
            CancelAgentRun();
            Model.StartNewChat();
            Model.ShowConversationView();
            Model.PersistUiState(MainTab.Chat);
        }

        public void OpenConversation(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            if (id == Model.ActiveConversationId) return;

            CancelAgentRun();
            if (!Model.OpenConversation(id))
                return;
            _activeQuestion = null;
            _questionTcs = null;
            Model.ShowConversationView();
            Model.PersistUiState(MainTab.Chat);
        }

        public void OnConversationRemoved()
        {
            CancelAgentRun();
            _activeQuestion = null;
            _questionTcs = null;
        }

        public void ShowDocsIndex()
        {
            _docsCurrentPath = DocumentationViewer.IndexFileName;
            DocsRenderer?.Invoke("Documentation", _docsViewer.ReadIndex());
        }

        /// <summary>Обрабатывает переход по относительной ссылке в документации.</summary>
        public bool NavigateDocs(string target)
        {
            var path = _docsViewer.ResolveLink(_docsCurrentPath, target);
            if (string.IsNullOrEmpty(path))
                return false;

            _docsCurrentPath = path;
            var title = string.IsNullOrWhiteSpace(path) ? "Documentation" : Path.GetFileNameWithoutExtension(path);
            DocsRenderer?.Invoke(title, _docsViewer.ReadDocument(path));
            return true;
        }

        public async Task CompactNowAsync()
        {
            if (_agentRunner == null)
            {
                AppendChat("Compaction is unavailable.");
                return;
            }

            var transcript = BuildTranscript();
            if (transcript.Count == 0)
            {
                AppendChat("Nothing to compact yet.");
                return;
            }

            try
            {
                var condensed = await _agentRunner.CompactAsync(transcript, BuildContextBudget(), CancellationToken.None);
                var compacted = condensed.Select(m => new ChatMessage
                {
                    Kind = m.Role == "assistant" ? ChatMessageKind.AgentText
                        : m.Role == "user" ? ChatMessageKind.User
                        : ChatMessageKind.SystemNotice,
                    Text = m.Content
                }).ToList();
                Model.ApplyCompactedTranscript(compacted);
                AppendCompaction("Context compacted (summarized).");
                SaveHistoryNow();
            }
            catch (Exception ex)
            {
                AppendExtensionError("Compact failed: " + ex.Message);
            }
        }

        public void RequestMemoryUpdate()
        {
            var prompt = ParseMemoryBankMode() == MemoryBankMode.Convention
                ? "update memory bank: read memory-bank/*.md files with read_files, then update the relevant files with editor."
                : "update memory bank: read the current memory bank with read_memory, then update the relevant files with update_memory.";
            InputWriter?.Invoke(prompt);
        }

        // ------------------------------------------------------------------
        // Questions / agent loop internals
        // ------------------------------------------------------------------

        private async Task<string> AskQuestionAsync(string question, IReadOnlyList<string> options, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _questionTcs = tcs;

            var message = new ChatMessage
            {
                Kind = ChatMessageKind.Question,
                Payload = new QuestionPayload { Question = question, Options = options ?? new List<string>() },
                Mode = Model.Mode.ToString()
            };
            _activeQuestion = message;
            AppendChatMessage(message);

            ct.Register(() => { if (!tcs.Task.IsCompleted) tcs.TrySetResult(SkipAnswerText); });
            return await tcs.Task;
        }

        public void AnswerQuestion(ChatMessage message)
        {
            if (message?.Payload is QuestionPayload q && !string.IsNullOrEmpty(q.Answer))
                _questionTcs?.TrySetResult(q.Answer);
        }

        public void SkipQuestion(ChatMessage message)
        {
            if (message?.Payload is QuestionPayload q)
            {
                q.IsSkipped = true;
                if (string.IsNullOrEmpty(q.Answer))
                    q.Answer = SkipAnswerText;
            }
            _questionTcs?.TrySetResult(SkipAnswerText);
        }

        private async Task<string> RunSubAgentAsync(string systemPrompt, string task, CancellationToken ct)
        {
            var registry = _toolRegistry;
            if (registry == null)
                return "spawn_agent unavailable: the tool registry is not initialized.";

            var toolCallingMode = string.Equals(Settings.ToolCallingMode, "Native", StringComparison.OrdinalIgnoreCase)
                ? ToolCallingMode.Native
                : ToolCallingMode.JsonChat;
            var allowed = BuildAllowedPermissions();
            var allowedWays = BuildAllowedWays();
            var budget = BuildSubAgentBudget();

            var item = new SubagentStatusItem { Prompt = task };
            AppendChatMessage(new ChatMessage
            {
                Kind = ChatMessageKind.Subagent,
                Payload = new SubagentPayload { Item = item },
                Mode = Model.Mode.ToString()
            });
            item.Status = "running";

            var runner = new AgentRunner(
                _ai,
                registry,
                m => AgentPromptBuilder.Build(m, registry, toolCallingMode, allowed, allowedWays),
                budget,
                toolCallingMode: toolCallingMode,
                allowedPermissions: allowed,
                allowedWays: allowedWays);

            runner.ToolExecuted += (s, e) =>
            {
                var latest = string.IsNullOrWhiteSpace(e.DisplayResult) ? (e.ToolName ?? "") : (e.ToolName ?? "") + ": " + e.DisplayResult;
                item.ToolCalls += 1;
                item.LatestToolCall = latest;
            };

            try
            {
                var result = await runner.RunAsync(Array.Empty<ChatMessageData>(), task, Model.Mode, budget, ct);
                var finalText = result?.FinalText ?? "";
                item.Status = "completed";
                item.Result = finalText;
                return finalText;
            }
            catch (OperationCanceledException)
            {
                item.Status = "cancelled";
                return "Sub-agent cancelled.";
            }
            catch (Exception ex)
            {
                item.Status = "failed";
                item.Error = ex.Message;
                return "Sub-agent failed: " + ex.Message;
            }
        }

        private void OnAgentToolExecuted(object sender, AgentToolCall e)
        {
            var detail = string.IsNullOrWhiteSpace(e.DisplayResult) ? e.Result : e.DisplayResult;
            ChatLog.Info("Agent tool executed: " + (e.ToolName ?? "") + (e.Blocked ? " (blocked)" : ""));
            if (string.Equals(e.ToolName, "ask_question", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(e.ToolName, "spawn_agent", StringComparison.OrdinalIgnoreCase)) return;
            AppendToolCall(e.ToolName, detail, e.Blocked);
        }

        private void OnAgentContextCompacted(object sender, EventArgs e)
        {
            AppendCompaction("Context compacted (summarized) to fit the context window.");
        }

        private void OnAgentRetryOccurred(object sender, AgentRetryArgs e)
        {
            ChatLog.Warn("Agent retry: " + (e.Kind ?? "") + " attempt " + e.Attempt + "/" + e.AttemptLimit + " - " + (e.Detail ?? ""));
        }

        // ------------------------------------------------------------------
        // Settings / provider / tools
        // ------------------------------------------------------------------

        private string GetSolutionDirectory()
        {
            return ChatEnvironment.SolutionPathProvider?.Invoke() ?? string.Empty;
        }

        private void RecreateAiProvider()
        {
            _contextUsageBinder.Detach();
            UnhookAiNotifiers();
            _ai = AiProviderFactory.Create(Settings);
            HookAiNotifiers();
            _contextUsageBinder.Attach(_ai as ITokenUsageProvider, BuildContextBudget());
        }

        public void ApplySettings()
        {
            SettingsPersistence?.Save(Settings);
            RecreateAiProvider();
            RebuildAgentTools();
        }

        private void HookAiNotifiers()
        {
            if (_ai is IMessageNotifier m) m.MessageSent += OnAiMessageSent;
            if (_ai is IRetryNotifier r) r.RetryAttempt += OnRetryAttempt;
        }

        private void UnhookAiNotifiers()
        {
            if (_ai is IMessageNotifier m) m.MessageSent -= OnAiMessageSent;
            if (_ai is IRetryNotifier r) r.RetryAttempt -= OnRetryAttempt;
        }

        private void UnhookAgentRunner()
        {
            if (_agentRunner == null) return;
            _agentRunner.ToolExecuted -= OnAgentToolExecuted;
            _agentRunner.ContextCompacted -= OnAgentContextCompacted;
            _agentRunner.RetryOccurred -= OnAgentRetryOccurred;
            _agentRunner = null;
        }

        private AgentToolRegistry BuildToolRegistry()
        {
            var factory = new AgentToolFactory(
                _ai,
                GetSolutionDirectory,
                ChatEnvironment.DocumentService,
                _buildFeedback,
                AskQuestionAsync,
                RunSubAgentAsync,
                Settings.AllowSubagents,
                Settings.SubagentMaxDepth);
            return factory.CreateRegistry();
        }

        private AgentPermissions BuildAllowedPermissions()
        {
            return AgentPermissionsPolicy.Resolve(Settings);
        }

        private WorkflowWriteWay BuildAllowedWays()
        {
            return WorkflowWritePolicy.Resolve(Settings);
        }

        private MemoryBankMode ParseMemoryBankMode()
        {
            var value = Settings?.MemoryBankMode;
            if (string.Equals(value, "Tool", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Tool;
            if (string.Equals(value, "Convention", StringComparison.OrdinalIgnoreCase)) return MemoryBankMode.Convention;
            return MemoryBankMode.Disabled;
        }

        private List<ChatMessageData> BuildTranscript()
        {
            var transcript = new List<ChatMessageData>();
            foreach (var message in Model.Messages)
            {
                if (string.IsNullOrWhiteSpace(message.Text)) continue;
                switch (message.Kind)
                {
                    case ChatMessageKind.User:
                        transcript.Add(new ChatMessageData { Role = "user", Content = message.Text });
                        break;
                    case ChatMessageKind.AgentText:
                    case ChatMessageKind.ResultAnswerPlan:
                    case ChatMessageKind.ResultAnswerAct:
                        transcript.Add(new ChatMessageData { Role = "assistant", Content = message.Text });
                        break;
                }
            }
            return transcript;
        }

        private ContextBudget BuildContextBudget()
        {
            return new ContextBudget
            {
                ContextWindowSize = Settings.ContextWindowSize > 0 ? Settings.ContextWindowSize : 409600,
                CompactThresholdFraction = Settings.CompactThreshold,
                ReserveFraction = Settings.ReserveFraction,
                KeepRecentMessages = Settings.KeepRecentMessages,
                ToolResultMaxChars = Settings.ToolResultMaxChars,
                UseToolRole = Settings.UseToolRole,
                MaxIterations = Settings.MaxIterations,
                AutoCompact = Settings.AutoCompact
            };
        }

        private ContextBudget BuildSubAgentBudget()
        {
            var budget = BuildContextBudget();
            budget.MaxIterations = Settings.SubagentMaxIterations > 0 ? Settings.SubagentMaxIterations : 4;
            budget.AutoCompact = false;
            return budget;
        }

        // ------------------------------------------------------------------
        // Message appenders
        // ------------------------------------------------------------------

        private void AppendChatMessage(ChatMessage message)
        {
            Model.AppendMessage(message);
            SaveHistoryNow();
        }

        private void AppendChat(string text) => AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.SystemNotice, Text = text });

        private void AppendExtensionError(string text) => AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.Error, Text = text });

        private void AppendExtensionError(Exception ex) => AppendExtensionError(ex?.Message ?? "Unknown error");

        private void AppendToolCall(string toolName, string result, bool blocked) =>
            AppendChatMessage(new ChatMessage
            {
                Kind = blocked ? ChatMessageKind.ToolBlocked : ChatMessageKind.ToolCall,
                Payload = new ToolCallPayload { ToolName = toolName ?? "", Result = result ?? "", Blocked = blocked }
            });

        private void AppendCompaction(string text) => AppendChatMessage(new ChatMessage { Kind = ChatMessageKind.Compaction, Text = text });

        // ------------------------------------------------------------------
        // Notifications / build errors / commit helper
        // ------------------------------------------------------------------

        private void OnAiMessageSent(object sender, AiMessageEventArgs e)
        {
            ChatLog.Info("AI message sent (" + (e?.Role ?? "") + "): " + (e?.Content ?? "").Length + " chars.");
        }

        private void OnRetryAttempt(object sender, RetryAttemptEventArgs e)
        {
            ChatLog.Warn("Provider retry attempt " + (e?.Attempt ?? 0) + " (status " + (e?.StatusCode?.ToString() ?? "n/a") + ").");
        }

        private void SaveHistoryNow()
        {
            Model.SaveHistoryNow();
        }

        private async Task SendErrorsAsync()
        {
            try
            {
                var errors = await _buildFeedback.GetCompilationErrorsAsync();
                var text = await BuildCompilationErrorsContextAsync(errors);
                if (string.IsNullOrWhiteSpace(text))
                {
                    AppendChat(await FormatNoCompilationErrorsAsync());
                    return;
                }

                await SendChatAsync(text, null);
            }
            catch (Exception ex)
            {
                AppendExtensionError(ex);
            }
        }

        private async Task<string> BuildCompilationErrorsContextAsync(IReadOnlyList<BuildError> errors)
        {
            return await CompilationErrorContextBuilder.BuildAsync(
                errors,
                (file, line, ctx) => Task.FromResult(ReadDocumentWindow(file, line, ctx)),
                MaxErrorsWithContext,
                ErrorContextLines);
        }

        private static string ReadDocumentWindow(string file, int line, int contextLines)
        {
            try
            {
                var window = ChatEnvironment.DocumentService?.ReadOpenDocumentWindow(file, line, contextLines);
                if (!string.IsNullOrWhiteSpace(window))
                    return window;

                if (!File.Exists(file))
                    return null;

                var lines = File.ReadAllLines(file);
                if (line < 1 || line > lines.Length)
                    return null;

                var from = Math.Max(1, line - contextLines);
                var to = Math.Min(lines.Length, line + contextLines);
                var sb = new StringBuilder();
                for (var i = from; i <= to; i++)
                    sb.AppendLine(lines[i - 1]);
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> FormatNoCompilationErrorsAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine("The build Error List is empty.");

            string output;
            try
            {
                output = await _buildFeedback.GetOutputWindowTextAsync();
            }
            catch (Exception ex)
            {
                output = "Build output unavailable: " + ex.Message;
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                sb.AppendLine();
                sb.AppendLine("Build output:");
                sb.AppendLine(output);
            }

            return sb.ToString().TrimEnd();
        }

        private async Task CheckCommitBranchAndNotifyAsync(string solutionDir)
        {
            try
            {
                var repoRoot = await _git.GetRepositoryRootAsync(solutionDir);
                var branch = await _git.GetCurrentBranchAsync(repoRoot);
                if (string.Equals(branch, "dev", StringComparison.Ordinal))
                {
                    AppendChat("Commit is available on the 'dev' branch. Use git_status/git_diff and shell_command to commit changes.");
                }
                else
                {
                    AppendChat("Commit is only available on the 'dev' branch. Current branch: " + (string.IsNullOrWhiteSpace(branch) ? "(unknown)" : branch));
                }
            }
            catch (Exception ex)
            {
                AppendExtensionError("Could not open the commit control: " + ex.Message);
            }
        }
    }
}
