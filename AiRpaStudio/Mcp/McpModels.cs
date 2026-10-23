namespace RPA.Mcp
{
    /// <summary>
    /// MCP Server information
    /// </summary>
    public class McpServerInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// MCP Tool definition
    /// </summary>
    public class McpTool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public object InputSchema { get; set; }
    }

    /// <summary>
    /// MCP Tool execution result
    /// </summary>
    public class McpToolResult
    {
        public bool Success { get; set; }
        public object Content { get; set; }
        public string Error { get; set; }
        
        /// <summary>
        /// Detailed validation error information (if applicable)
        /// </summary>
        public ValidationError ValidationError { get; set; }
    }
    
    /// <summary>
    /// Detailed validation error information
    /// </summary>
    public class ValidationError
    {
        /// <summary>
        /// Type of validation error
        /// </summary>
        public ValidationErrorType Type { get; set; }
        
        /// <summary>
        /// Human-readable error message
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// Name of the parameter with error
        /// </summary>
        public string ParameterName { get; set; }
        
        /// <summary>
        /// Expected parameter type
        /// </summary>
        public string ExpectedType { get; set; }
        
        /// <summary>
        /// Actual type received
        /// </summary>
        public string ActualType { get; set; }
        
        /// <summary>
        /// Full tool schema for reference
        /// </summary>
        public object ToolSchema { get; set; }
        
        /// <summary>
        /// Example of correct usage
        /// </summary>
        public string Example { get; set; }
    }
    
    /// <summary>
    /// Type of validation error
    /// </summary>
    public enum ValidationErrorType
    {
        MissingRequiredParameter,
        InvalidParameterType,
        InvalidParameterValue,
        InvalidParameterFormat,
        UnknownParameter
    }
    
    /// <summary>
    /// Result of parameter validation
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public ValidationErrorType ErrorType { get; set; }
        public string ErrorMessage { get; set; }
        public string ParameterName { get; set; }
        public string ExpectedType { get; set; }
        public string ActualType { get; set; }
    }

    /// <summary>
    /// MCP Request (JSON-RPC 2.0)
    /// </summary>
    public class McpRequest
    {
        public string JsonRpc { get; set; } = "2.0";
        public string Method { get; set; }
        public object Params { get; set; }
        public object Id { get; set; }
    }

    /// <summary>
    /// MCP Response (JSON-RPC 2.0)
    /// </summary>
    public class McpResponse
    {
        public string JsonRpc { get; set; } = "2.0";
        public object Result { get; set; }
        public McpError Error { get; set; }
        public object Id { get; set; }
    }

    /// <summary>
    /// MCP Error
    /// </summary>
    public class McpError
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
    }

    /// <summary>
    /// Operation result
    /// </summary>
    public class OperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}