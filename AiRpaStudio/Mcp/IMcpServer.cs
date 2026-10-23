using System.Collections.Generic;
using System.Threading.Tasks;

namespace RPA.Mcp
{
    /// <summary>
    /// Interface for MCP Server implementation
    /// </summary>
    public interface IMcpServer
    {
        /// <summary>
        /// Initialize the MCP server
        /// </summary>
        Task<McpServerInfo> Initialize();

        /// <summary>
        /// List all available tools
        /// </summary>
        Task<IEnumerable<McpTool>> ListTools();

        /// <summary>
        /// Call a specific tool
        /// </summary>
        Task<McpToolResult> CallTool(string name, object arguments);
    }
}