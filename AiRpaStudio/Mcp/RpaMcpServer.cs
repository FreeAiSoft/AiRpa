using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RPA.Core;
using RPA.Mcp.Tools;

namespace RPA.Mcp
{
    /// <summary>
    /// Main MCP Server implementation for RPA Studio
    /// Provides tools for AI agents to interact with workflow designer
    /// </summary>
    public class RpaMcpServer : IMcpServer
    {
        private readonly Dictionary<string, IMcpTool> _tools;
        private readonly CustomWfDesigner _designer;
        private readonly string _projectRoot;

        public RpaMcpServer()
        {
            _designer = CustomWfDesigner.GetInstance();
            _projectRoot = Directory.GetCurrentDirectory();

            // Register all available tools
            _tools = new Dictionary<string, IMcpTool>
            {
                // Workflow tools
                ["add_activity"] = new AddActivityTool(_designer),
                ["list_activities"] = new ListActivitiesTool(),
                //["get_workflow_info"] = new GetWorkflowInfoTool(_designer),
                ["get_validation_errors"] = new GetValidationErrorsTool(_designer),
                ["remove_activity"] = new RemoveActivityTool(_designer),
                ["update_activity"] = new UpdateActivityTool(_designer),
                ["save_workflow"] = new SaveWorkflowTool(_designer),
                ["get_open_tabs"] = new GetOpenTabsTool(_designer),
                ["set_active_tab"] = new SetActiveTabTool(_designer),
                ["create_activity_file"] = new CreateActivityFileTool(_designer),

                // Activity manipulation tools
                ["move_activity"] = new MoveActivityTool(_designer),
                ["copy_activity"] = new CopyActivityTool(_designer),
                ["get_activity_details"] = new GetActivityDetailsTool(_designer),
                ["set_activity_property"] = new SetActivityPropertyTool(_designer),

                // Variable management tools
                ["create_variable"] = new CreateVariableTool(_designer),
                ["list_variables"] = new ListVariablesTool(_designer),
                ["update_variable"] = new UpdateVariableTool(_designer),
                ["delete_variable"] = new DeleteVariableTool(_designer),

                // Argument management tools
                ["create_argument"] = new CreateArgumentTool(_designer),
                ["list_arguments"] = new ListArgumentsTool(_designer),

                // Navigation tools
                ["get_activity_tree"] = new GetActivityTreeTool(_designer),
                ["find_activity"] = new FindActivityTool(_designer),
                ["get_parent_activity"] = new GetParentActivityTool(_designer),
                ["get_child_activities"] = new GetChildActivitiesTool(_designer),

                // Debugging and execution tools
                ["set_breakpoint"] = new SetBreakpointTool(_designer),
                ["remove_breakpoint"] = new RemoveBreakpointTool(_designer),
                ["list_breakpoints"] = new ListBreakpointsTool(_designer),
                ["run_workflow"] = new RunWorkflowTool(_designer),
                ["debug_workflow"] = new DebugWorkflowTool(_designer),
                ["stop_execution"] = new StopExecutionTool(_designer),

                // Import and dependency tools
                ["add_namespace"] = new AddNamespaceTool(_designer),
                ["list_namespaces"] = new ListNamespacesTool(_designer),
                ["add_assembly_reference"] = new AddAssemblyReferenceTool(_designer),
                ["list_assemblies"] = new ListAssembliesTool(_designer),

                // Template tools
                ["create_from_template"] = new CreateFromTemplateTool(_designer),
                ["list_templates"] = new ListTemplatesTool(_designer),
                ["save_as_template"] = new SaveAsTemplateTool(_designer),

                // Refactoring tools
                ["extract_to_workflow"] = new ExtractToWorkflowTool(_designer),
                ["inline_workflow"] = new InlineWorkflowTool(_designer),
                ["rename_activity"] = new RenameActivityTool(_designer),

                // Analysis and metrics tools
                ["analyze_workflow"] = new AnalyzeWorkflowTool(_designer),
                ["get_workflow_statistics"] = new GetWorkflowStatisticsTool(_designer),

                // Comment and annotation tools
                ["add_annotation"] = new AddAnnotationTool(_designer),
                ["list_annotations"] = new ListAnnotationsTool(_designer),

                // File system tools
                ["list_files"] = new ListFilesTool(),
                ["read_file"] = new ReadFileTool(_projectRoot),
                ["get_file_info"] = new GetFileInfoTool(_projectRoot),
                ["search_files"] = new SearchFilesTool(_projectRoot)
            };
        }

        /// <summary>
        /// Initialize the MCP server
        /// </summary>
        public Task<McpServerInfo> Initialize()
        {
            return Task.FromResult(new McpServerInfo
            {
                Name = "rpa-studio",
                Version = "1.0.0",
                Description = "MCP Server for RPA Studio - provides tools for workflow automation and AI integration"
            });
        }

        /// <summary>
        /// List all available tools
        /// </summary>
        public Task<IEnumerable<McpTool>> ListTools()
        {
            List<McpTool> tools = _tools.Values.Select(tool => tool.GetSchema()).ToList();
            return Task.FromResult<IEnumerable<McpTool>>(tools);
        }

        /// <summary>
        /// Call a specific tool with arguments
        /// </summary>
        public async Task<McpToolResult> CallTool(string name, object arguments)
        {
            if (!_tools.TryGetValue(name, out IMcpTool tool))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Tool '{name}' not found. Available tools: {string.Join(", ", _tools.Keys)}"
                };
            }

            try
            {
                return await tool.Execute(arguments);
            }
            catch (Exception ex)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Error executing tool '{name}': {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Handle JSON-RPC 2.0 request
        /// </summary>
        public async Task<McpResponse> HandleRequest(McpRequest request)
        {
            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        McpServerInfo info = await Initialize();
                        return new McpResponse
                        {
                            Result = info,
                            Id = request.Id
                        };

                    case "tools/list":
                        IEnumerable<McpTool> tools = await ListTools();
                        return new McpResponse
                        {
                            Result = new { tools },
                            Id = request.Id
                        };

                    case "tools/call":
                        dynamic callParams = request.Params;
                        dynamic toolName = callParams?.name?.ToString();
                        dynamic arguments = callParams?.arguments;

                        if (string.IsNullOrEmpty(toolName))
                        {
                            return new McpResponse
                            {
                                Error = new McpError
                                {
                                    Code = -32602,
                                    Message = "Invalid params: 'name' is required"
                                },
                                Id = request.Id
                            };
                        }

                        dynamic result = await CallTool(toolName, arguments);
                        return new McpResponse
                        {
                            Result = result,
                            Id = request.Id
                        };

                    default:
                        return new McpResponse
                        {
                            Error = new McpError
                            {
                                Code = -32601,
                                Message = $"Method '{request.Method}' not found"
                            },
                            Id = request.Id
                        };
                }
            }
            catch (Exception ex)
            {
                return new McpResponse
                {
                    Error = new McpError
                    {
                        Code = -32603,
                        Message = "Internal error",
                        Data = ex.Message
                    },
                    Id = request.Id
                };
            }
        }
    }
}
