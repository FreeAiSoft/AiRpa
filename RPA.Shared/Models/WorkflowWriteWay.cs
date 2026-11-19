using System;

namespace RPA.Shared.Models
{
    /// <summary>
    /// AiRpa-specific: the MCP-based ways the agent can write a workflow.
    /// Kept out of the shared Agent.Core package because it is RPA-studio specific.
    /// </summary>
    [Flags]
    public enum WorkflowWriteWay
    {
        /// <summary>No MCP-based workflow writing way enabled.</summary>
        None = 0,
        /// <summary>Palette/designer writing (xaml.create_workflow, xaml.add_activity, xaml.set_argument, ...).</summary>
        Palette = 1,
        /// <summary>JSON AST writing (xaml.create_workflow_from_json).</summary>
        JsonAst = 2,
        /// <summary>Code-first writing (xaml.create_workflow_from_code).</summary>
        CodeFirst = 4,
        /// <summary>All MCP-based workflow writing ways enabled.</summary>
        All = Palette | JsonAst | CodeFirst
    }
}