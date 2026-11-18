using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Agent.Core.Models;
using Agent.Rpa.Models;

namespace Agent.Rpa.AgentTools
{
    /// <summary>A tool the agent can invoke from the chat: read-only or mutating.</summary>
    public interface IAgentTool
    {
        /// <summary>Machine name used in the tool_call JSON.</summary>
        string Name { get; }

        /// <summary>Human-readable description shown in the system prompt.</summary>
        string Description { get; }

        /// <summary>JSON schema describing the accepted arguments (shown in the system prompt).</summary>
        string ParametersSchema { get; }

        /// <summary>True when the tool changes state (files, solutions, builds, KB).</summary>
        bool Mutates { get; }

        /// <summary>
        /// Workflow writing way the tool belongs to (MCP palette / JSON AST / code-first), or
        /// <see cref="WorkflowWriteWay.None"/> for tools outside the three toggles.
        /// </summary>
        WorkflowWriteWay Way { get; }

        /// <summary>Permission category the tool belongs to (gated by the settings).</summary>
        AgentPermissions Permission { get; }

        /// <summary>True when executing this tool completes the run (e.g. submit_and_exit).</summary>
        bool CompletesRun { get; }

        /// <summary>Executes the tool and returns a text result fed back to the model.</summary>
        Task<string> ExecuteAsync(JObject arguments, CancellationToken cancellationToken);

        /// <summary>
        /// Produces a short text for the chat progress line (e.g. a file path), or null when
        /// the full execution result should be shown instead.
        /// </summary>
        Task<string> GetDisplayResultAsync(JObject arguments, CancellationToken cancellationToken);
    }
}
