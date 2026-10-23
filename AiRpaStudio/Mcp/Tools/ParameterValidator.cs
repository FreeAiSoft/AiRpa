using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Validates MCP tool parameters against their JSON schema
    /// </summary>
    public class ParameterValidator
    {
        private readonly McpTool _schema;

        public ParameterValidator(McpTool schema)
        {
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        }

        /// <summary>
        /// Validates arguments against the tool schema
        /// </summary>
        public ValidationResult Validate(object arguments)
        {
            if (arguments == null)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorType = ValidationErrorType.MissingRequiredParameter,
                    ErrorMessage = "Arguments object is null"
                };
            }

            try
            {
                JObject jObject = JObject.FromObject(arguments);
                JObject schemaObj = JObject.FromObject(_schema.InputSchema);

                // 1. Check required parameters
                string[] required = schemaObj["required"]?.ToObject<string[]>() ?? Array.Empty<string>();
                foreach (string requiredParam in required)
                {
                    if (jObject[requiredParam] == null)
                    {
                        if (schemaObj["properties"] is JObject properties)
                        {
                            JToken paramSchema = properties[requiredParam];
                            string paramType = paramSchema?["type"]?.ToString() ?? "unknown";

                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorType = ValidationErrorType.MissingRequiredParameter,
                                ErrorMessage = $"Required parameter '{requiredParam}' is missing",
                                ParameterName = requiredParam,
                                ExpectedType = paramType
                            };
                        }
                    }
                }

                // 2. Check parameter types and values
                if (schemaObj["properties"] is JObject props)
                {
                    foreach (JProperty prop in jObject.Properties())
                    {
                        JToken paramSchema = props[prop.Name];
                        if (paramSchema == null)
                        {
                            // Unknown parameter
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorType = ValidationErrorType.UnknownParameter,
                                ErrorMessage = $"Unknown parameter '{prop.Name}'. This parameter is not defined in the tool schema.",
                                ParameterName = prop.Name
                            };
                        }

                        string expectedType = paramSchema["type"]?.ToString();
                        string actualType = GetJTokenType(prop.Value);

                        if (!IsTypeCompatible(expectedType, actualType, prop.Value))
                        {
                            return new ValidationResult
                            {
                                IsValid = false,
                                ErrorType = ValidationErrorType.InvalidParameterType,
                                ErrorMessage = $"Parameter '{prop.Name}' has invalid type. Expected '{expectedType}', got '{actualType}'",
                                ParameterName = prop.Name,
                                ExpectedType = expectedType,
                                ActualType = actualType
                            };
                        }

                        // 3. Check enum values
                        string[] enumValues = paramSchema["enum"]?.ToObject<string[]>();
                        if (enumValues != null && enumValues.Length > 0)
                        {
                            string value = prop.Value.ToString();
                            if (!enumValues.Contains(value))
                            {
                                return new ValidationResult
                                {
                                    IsValid = false,
                                    ErrorType = ValidationErrorType.InvalidParameterValue,
                                    ErrorMessage = $"Parameter '{prop.Name}' has invalid value '{value}'. Allowed values: {string.Join(", ", enumValues)}",
                                    ParameterName = prop.Name,
                                    ExpectedType = $"enum: [{string.Join(", ", enumValues)}]",
                                    ActualType = value
                                };
                            }
                        }
                    }
                }

                return new ValidationResult { IsValid = true };
            }
            catch (Exception ex)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ErrorType = ValidationErrorType.InvalidParameterFormat,
                    ErrorMessage = $"Error parsing arguments: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Gets the JSON type name from a JToken
        /// </summary>
        private string GetJTokenType(JToken token)
        {
            return token.Type switch
            {
                JTokenType.String => "string",
                JTokenType.Integer => "integer",
                JTokenType.Float => "number",
                JTokenType.Boolean => "boolean",
                JTokenType.Object => "object",
                JTokenType.Array => "array",
                JTokenType.Null => "null",
                _ => "unknown"
            };
        }

        /// <summary>
        /// Checks if the actual type is compatible with the expected type
        /// </summary>
        private bool IsTypeCompatible(string expectedType, string actualType, JToken value)
        {
            if (string.IsNullOrEmpty(expectedType))
            {
                return true; // No type specified, accept anything
            }

            // Null check
            if (actualType == "null")
            {
                return false; // Null values are not compatible (required params should catch this)
            }

            // Exact match
            if (expectedType == actualType)
            {
                return true;
            }

            // Number can be integer or float
            if (expectedType == "number" && (actualType == "integer" || actualType == "float"))
            {
                return true;
            }

            // Integer should not accept float
            if (expectedType == "integer" && actualType == "number")
            {
                // Check if it's actually an integer
                if (value.Type == JTokenType.Float)
                {
                    return false;
                }
                return true;
            }

            return false;
        }
    }
}