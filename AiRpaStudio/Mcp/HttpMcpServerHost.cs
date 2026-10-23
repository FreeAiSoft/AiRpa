using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RPA.Mcp
{
    /// <summary>
    /// MCP Server host that communicates via HTTP
    /// Used for external applications and web-based integrations
    /// </summary>
    public class HttpMcpServerHost
    {
        private readonly RpaMcpServer _server;
        private readonly int _port;
        private HttpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _listenTask;

        public HttpMcpServerHost(RpaMcpServer server, int port = 3000)
        {
            _server = server;
            _port = port;
        }

        /// <summary>
        /// Start the HTTP server
        /// </summary>
        public void Start()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();

            _listenTask = Task.Run(() => ListenAsync(_cancellationTokenSource.Token));

            Console.WriteLine($"MCP HTTP Server started on port {_port}");
        }

        /// <summary>
        /// Stop the HTTP server
        /// </summary>
        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _listener?.Stop();
            _listenTask?.Wait();

            Console.WriteLine("MCP HTTP Server stopped");
        }

        /// <summary>
        /// Run the server and wait for completion
        /// </summary>
        public async Task RunAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();

            Console.WriteLine($"MCP HTTP Server started on port {_port}");

            await ListenAsync(_cancellationTokenSource.Token);
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    HttpListenerContext context = await _listener.GetContextAsync();

                    // Process request in background
                    _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
                }
            }
            catch (HttpListenerException)
            {
                // Listener stopped
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"MCP HTTP Server error: {ex.Message}");
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                // Set CORS headers
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                // Handle OPTIONS request
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                // Only accept POST requests
                if (request.HttpMethod != "POST")
                {
                    response.StatusCode = 405; // Method Not Allowed
                    byte[] errorBuffer = Encoding.UTF8.GetBytes("Only POST requests are supported");
                    response.ContentLength64 = errorBuffer.Length;
                    await response.OutputStream.WriteAsync(errorBuffer, 0, errorBuffer.Length);
                    response.Close();
                    return;
                }

                // Read request body
                string requestBody;
                using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                // Parse MCP request
                McpRequest mcpRequest = JsonConvert.DeserializeObject<McpRequest>(requestBody);

                if (mcpRequest == null)
                {
                    SendErrorResponse(response, -32700, "Parse error", "Invalid JSON-RPC request");
                    return;
                }

                // Handle request
                McpResponse mcpResponse = await _server.HandleRequest(mcpRequest);

                // Send response
                string responseJson = JsonConvert.SerializeObject(mcpResponse);
                byte[] buffer = Encoding.UTF8.GetBytes(responseJson);

                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (JsonException ex)
            {
                SendErrorResponse(context.Response, -32700, "Parse error", ex.Message);
            }
            catch (Exception ex)
            {
                SendErrorResponse(context.Response, -32603, "Internal error", ex.Message);
            }
        }

        private void SendErrorResponse(HttpListenerResponse response, int code, string message, string data)
        {
            try
            {
                McpResponse errorResponse = new McpResponse
                {
                    Error = new McpError
                    {
                        Code = code,
                        Message = message,
                        Data = data
                    },
                    Id = null
                };

                string errorJson = JsonConvert.SerializeObject(errorResponse);
                byte[] buffer = Encoding.UTF8.GetBytes(errorJson);

                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;

                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Close();
            }
            catch
            {
                // Ignore errors when sending error response
            }
        }
    }
}
