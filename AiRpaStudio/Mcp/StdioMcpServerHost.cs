using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RPA.Mcp
{
    /// <summary>
    /// MCP Server host that communicates via stdio (standard input/output)
    /// Used for Claude Desktop and similar integrations
    /// </summary>
    public class StdioMcpServerHost
    {
        private readonly RpaMcpServer _server;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listenTask;

        public StdioMcpServerHost(RpaMcpServer server)
        {
            _server = server;
        }

        /// <summary>
        /// Start the MCP server listening on stdio
        /// </summary>
        public void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenAsync(_cancellationTokenSource.Token));
        }

        /// <summary>
        /// Stop the MCP server
        /// </summary>
        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _listenTask?.Wait();
        }

        /// <summary>
        /// Run the server and wait for completion
        /// </summary>
        public async Task RunAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            await ListenAsync(_cancellationTokenSource.Token);
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            try
            {
                using StreamReader reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
                using StreamWriter writer = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8);
                writer.AutoFlush = true;

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Read JSON-RPC request from stdin
                    string line = await reader.ReadLineAsync();

                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    try
                    {
                        // Parse request
                        McpRequest request = JsonConvert.DeserializeObject<McpRequest>(line);

                        if (request == null)
                        {
                            continue;
                        }

                        // Handle request
                        McpResponse response = await _server.HandleRequest(request);

                        // Write response to stdout
                        string responseJson = JsonConvert.SerializeObject(response);
                        await writer.WriteLineAsync(responseJson);
                    }
                    catch (JsonException ex)
                    {
                        // Invalid JSON - send error response
                        McpResponse errorResponse = new McpResponse
                        {
                            Error = new McpError
                            {
                                Code = -32700,
                                Message = "Parse error",
                                Data = ex.Message
                            },
                            Id = null
                        };

                        string errorJson = JsonConvert.SerializeObject(errorResponse);
                        await writer.WriteLineAsync(errorJson);
                    }
                    catch (Exception ex)
                    {
                        // General error - send error response
                        McpResponse errorResponse = new McpResponse
                        {
                            Error = new McpError
                            {
                                Code = -32603,
                                Message = "Internal error",
                                Data = ex.Message
                            },
                            Id = null
                        };

                        string errorJson = JsonConvert.SerializeObject(errorResponse);
                        await writer.WriteLineAsync(errorJson);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"MCP Server error: {ex.Message}");
            }
        }
    }
}