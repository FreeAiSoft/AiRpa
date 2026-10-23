using System.Threading.Tasks;

namespace RPA.Mcp
{
    /// <summary>
    /// Interface for MCP Tool implementation
    /// </summary>
    public interface IMcpTool
    {
        /// <summary>
        /// Get tool schema definition
        /// </summary>
        McpTool GetSchema();

        /// <summary>
        /// Execute the tool with given arguments
        /// </summary>
        Task<McpToolResult> Execute(object arguments);
    }
}