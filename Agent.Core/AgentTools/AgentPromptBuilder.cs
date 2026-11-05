using System;
using System.Linq;
using System.Text;
using Agent.Core.Models;
using Agent.Core.Services;

namespace Agent.Core.AgentTools
{
    /// <summary>Builds the agent system prompt for a mode from the base prompt + tool list.</summary>
    public static class AgentPromptBuilder
    {
        /// <summary>Prefixes that mark a tool as a workflow tool (AG-10/AG-11 rule).</summary>
        private static readonly string[] WorkflowToolPrefixes = { "activity_", "workflow_", "argument_", "variable_" };

        /// <summary>Prefix that marks a specification tool (волна 8, TZ-01…TZ-09).</summary>
        private static readonly string[] SpecToolPrefixes = { "spec_" };

        /// <summary>Builds the JSON-over-chat system prompt (backward-compatible default).</summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry)
        {
            return Build(mode, registry, ToolCallingMode.JsonChat, AgentPermissions.All);
        }

        /// <summary>
        /// Builds the agent system prompt for a mode and tool-calling protocol.
        /// Native mode uses a prompt without the JSON tool protocol (tools are provided
        /// as native function definitions); JSON mode injects the tool list into {tools}.
        /// </summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode)
        {
            return Build(mode, registry, toolCallingMode, AgentPermissions.All);
        }

        /// <summary>Builds the prompt for a mode, protocol and allowed permission set.</summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed)
        {
            if (mode == AgentMode.Help)
                return BuildHelpSupportPrompt();

            if (toolCallingMode == ToolCallingMode.Native)
            {
                var nativePrompt = mode == AgentMode.Act
                    ? PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-act-system-native.txt", PromptDefaults.AgentActSystemPromptNative)
                    : PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-plan-system-native.txt", PromptDefaults.AgentPlanSystemPromptNative);

                return AppendSpecGuidance(AppendWorkflowGuidance(nativePrompt, mode, registry, toolCallingMode, allowed), mode, registry, allowed);
            }

            var basePrompt = PromptLibrary.Load(PromptPaths.ResolvePromptsDirectory(), PromptLibrary.Categories.System, "agent-json-base.txt", PromptDefaults.AgentJsonBaseSystemPrompt);
            var modeInstructions = mode == AgentMode.Act
                ? PromptDefaults.AgentActModeInstructions
                : PromptDefaults.AgentPlanModeInstructions;

            var sb = new StringBuilder();
            foreach (var t in registry.ForMode(mode, allowed))
            {
                sb.Append("- ").Append(t.Name).Append(": ").Append(t.Description);
                sb.Append(" Parameters: ").AppendLine(t.ParametersSchema);
            }

            var toolList = sb.ToString();
            if (string.IsNullOrWhiteSpace(toolList))
                toolList = "(none)";

            var prompt = basePrompt.Replace("{tools}", toolList).Replace("{mode_instructions}", modeInstructions);
            return AppendSpecGuidance(AppendWorkflowGuidance(prompt, mode, registry, toolCallingMode, allowed), mode, registry, allowed);
        }

        /// <summary>
        /// Builds the prompt for a mode, protocol, permission set and optional user addressing.
        /// When <paramref name="addressByName"/> is true and <paramref name="userName"/> is not
        /// blank, a short instruction to address the user by name is appended to the prompt.
        /// </summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed, string userName, bool addressByName)
        {
            return Build(mode, registry, toolCallingMode, allowed, userName, addressByName, MemoryBankMode.Tool);
        }

        /// <summary>
        /// Builds the prompt for a mode, protocol, permission set, optional user addressing
        /// and an optional memory-bank convention.
        /// </summary>
        public static string Build(AgentMode mode, AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed, string userName, bool addressByName, MemoryBankMode memoryBankMode)
        {
            var prompt = Build(mode, registry, toolCallingMode, allowed);
            var name = (userName ?? string.Empty).Trim();
            if (addressByName && name.Length > 0)
                prompt = prompt + "\n\nAddress the user by their name when replying: \"" + name + "\".";

            if (memoryBankMode == MemoryBankMode.Convention)
                prompt = prompt + "\n\n" + PromptDefaults.MemoryBankConventionInstructions;

            return prompt;
        }

        /// <summary>
        /// Appends the workflow guidance (AG-11) to a prompt, but only when the registry really exposes
        /// workflow tools for this mode: a prompt must never describe tools that cannot be called.
        /// PLAN gets the read-only part; ACT gets the editing part and the four worked examples.
        /// The examples follow the active protocol, so a native prompt never shows JSON action objects.
        /// </summary>
        public static string AppendWorkflowGuidance(
            string prompt,
            AgentMode mode,
            AgentToolRegistry registry,
            ToolCallingMode toolCallingMode,
            AgentPermissions allowed)
        {
            if (string.IsNullOrEmpty(prompt) || registry == null)
                return prompt;

            var hasWorkflowTools = registry.ForMode(mode, allowed).Any(tool => IsWorkflowTool(tool.Name));
            if (!hasWorkflowTools)
                return prompt;

            var sb = new StringBuilder(prompt);
            sb.Append("\n\n").Append(PromptDefaults.WorkflowReadInstructions);

            if (mode == AgentMode.Act)
            {
                sb.Append("\n\n").Append(PromptDefaults.WorkflowEditInstructions);
                sb.Append("\n\n").Append(toolCallingMode == ToolCallingMode.Native
                    ? PromptDefaults.WorkflowExamplesNative
                    : PromptDefaults.WorkflowExamplesJson);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Appends the specification guidance (волна 8) to a prompt, but only when the registry really exposes
        /// specification tools for this mode: a prompt must never describe tools that cannot be called.
        /// PLAN gets the read-only part only; ACT gets the editing part as well.
        /// </summary>
        public static string AppendSpecGuidance(
            string prompt,
            AgentMode mode,
            AgentToolRegistry registry,
            AgentPermissions allowed)
        {
            if (string.IsNullOrEmpty(prompt) || registry == null)
                return prompt;

            var hasSpecTools = registry.ForMode(mode, allowed).Any(tool => IsSpecTool(tool.Name));
            if (!hasSpecTools)
                return prompt;

            var sb = new StringBuilder(prompt);
            sb.Append("\n\n").Append(PromptDefaults.SpecReadInstructions);

            if (mode == AgentMode.Act)
                sb.Append("\n\n").Append(PromptDefaults.SpecEditInstructions);

            return sb.ToString();
        }

        /// <summary>Specification tools are recognised by the name prefix, the same rule as the registry guard.</summary>
        private static bool IsSpecTool(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var prefix in SpecToolPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Workflow tools are recognised by name prefixes, the same rule as the registry guard.</summary>
        private static bool IsWorkflowTool(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            foreach (var prefix in WorkflowToolPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Builds the Help-mode support prompt: the support persona plus the bundled user documentation.</summary>
        private static string BuildHelpSupportPrompt()
        {
            const string preamble = "Ты очень нежный и внимательный агент поддержки, вот твоя документация - ";
            var docs = HelpDocumentation.ReadAll();
            var body = string.IsNullOrWhiteSpace(docs)
                ? "Документация временно недоступна. Вежливо объясни пользователю, как пользоваться расширением Vibe Coder, на основе общих знаний."
                : docs;

            return preamble + "\n\n" + body + "\n\n" +
                "Отвечай на вопросы пользователя по этой документации на русском языке. " +
                "Будь вежливым, терпеливым и внимательным. Если в документации нет ответа, честно скажи об этом.";
        }

        /// <summary>
        /// Builds the Plan-mode system prompt enriched with a compact reference of the
        /// built-in studio documentation, so the agent can answer "how does the studio
        /// work" questions using the search_docs/read_docs tools.
        /// </summary>
        public static string BuildPlanWithDocumentation(AgentToolRegistry registry, ToolCallingMode toolCallingMode, AgentPermissions allowed, string docsDirectory = null)
        {
            var prompt = Build(AgentMode.Plan, registry, toolCallingMode, allowed);
            return AppendDocumentationReference(prompt, docsDirectory);
        }

        /// <summary>
        /// Appends a compact documentation reference to a prompt. When <paramref name="docsDirectory"/>
        /// is null the deployed docs directory is used.
        /// </summary>
        public static string AppendDocumentationReference(string prompt, string docsDirectory = null)
        {
            var directory = docsDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = HelpDocumentation.ResolveDocsDirectory();

            var index = DocumentationIndex.Build(directory);
            var reference = index.BuildReference();
            if (string.IsNullOrWhiteSpace(reference))
                return prompt;

            return prompt + "\n\n# Built-in studio documentation\n\n" +
                "Answer \"how does the studio work\" questions from the built-in documentation. " +
                "Use the search_docs tool to find relevant sections and read_docs to read a full document. " +
                "Available topics:\n" + reference;
        }

    }
}
