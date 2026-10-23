using System;
using System.Threading.Tasks;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Base class for MCP tool implementations
    /// </summary>
    public abstract class BaseMcpTool : IMcpTool
    {
        /// <summary>
        /// Get the schema definition for this tool
        /// </summary>
        public abstract McpTool GetSchema();

        /// <summary>
        /// Execute the tool with given arguments
        /// </summary>
        public abstract Task<McpToolResult> Execute(object arguments);

        /// <summary>
        /// Create a success result
        /// </summary>
        protected McpToolResult Success(object content, string message = null)
        {
            return new McpToolResult
            {
                Success = true,
                Content = content ?? new { message = message ?? "Operation completed successfully" },
                Error = null
            };
        }

        /// <summary>
        /// Create an error result
        /// </summary>
        protected McpToolResult Error(string error)
        {
            return new McpToolResult
            {
                Success = false,
                Content = null,
                Error = error
            };
        }

        /// <summary>
        /// Validates arguments against the tool schema
        /// </summary>
        protected ValidationResult ValidateArguments(object arguments)
        {
            McpTool schema = GetSchema();
            ParameterValidator validator = new ParameterValidator(schema);
            return validator.Validate(arguments);
        }

        /// <summary>
        /// Creates a validation error result with detailed information
        /// </summary>
        protected McpToolResult ValidationError(
            ValidationErrorType type,
            string message,
            string parameterName = null,
            string expectedType = null,
            string actualType = null)
        {
            McpTool schema = GetSchema();
            string example = GenerateExample();

            return new McpToolResult
            {
                Success = false,
                Error = message,
                ValidationError = new ValidationError
                {
                    Type = type,
                    Message = message,
                    ParameterName = parameterName,
                    ExpectedType = expectedType,
                    ActualType = actualType,
                    ToolSchema = schema.InputSchema,
                    Example = example
                }
            };
        }

        /// <summary>
        /// Creates a validation error result from a ValidationResult
        /// </summary>
        protected McpToolResult ValidationError(ValidationResult validationResult)
        {
            return ValidationError(
                validationResult.ErrorType,
                validationResult.ErrorMessage,
                validationResult.ParameterName,
                validationResult.ExpectedType,
                validationResult.ActualType
            );
        }

        /// <summary>
        /// Generates an example of correct usage for this tool
        /// </summary>
        protected virtual string GenerateExample()
        {
            McpTool schema = GetSchema();
            return JsonExampleGenerator.GenerateFromSchema(schema);
        }

        /// <summary>
        /// Validates arguments and executes the function if valid
        /// </summary>
        protected McpToolResult ValidateAndExecute(
            object arguments,
            Func<object, McpToolResult> executeFunc)
        {
            if (executeFunc == null)
            {
                throw new ArgumentNullException(nameof(executeFunc));
            }

            ValidationResult validationResult = ValidateArguments(arguments);

            return !validationResult.IsValid ? ValidationError(validationResult) : executeFunc(arguments);
        }

        /// <summary>
        /// Async version: Validates arguments and executes the function if valid
        /// </summary>
        protected async Task<McpToolResult> ValidateAndExecuteAsync(
            object arguments,
            Func<object, Task<McpToolResult>> executeFunc)
        {
            if (executeFunc == null)
            {
                throw new ArgumentNullException(nameof(executeFunc));
            }

            ValidationResult validationResult = ValidateArguments(arguments);

            return !validationResult.IsValid ? ValidationError(validationResult) : await executeFunc(arguments);
        }
    }
}
