using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Generates JSON-RPC 2.0 command examples from MCP tool schemas
    /// </summary>
    public static class JsonExampleGenerator
    {
        /// <summary>
        /// Generates a complete JSON-RPC 2.0 example from a tool schema
        /// </summary>
        public static string GenerateFromSchema(McpTool schema)
        {
            if (schema == null)
            {
                return "// No schema available";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"jsonrpc\": \"2.0\",");
            sb.AppendLine("  \"method\": \"tools/call\",");
            sb.AppendLine("  \"params\": {");
            sb.AppendLine($"    \"name\": \"{schema.Name}\",");
            sb.AppendLine("    \"arguments\": {");

            try
            {
                JObject schemaObj = JObject.FromObject(schema.InputSchema);
                JObject properties = schemaObj["properties"] as JObject;
                string[] required = schemaObj["required"]?.ToObject<string[]>() ?? new string[0];

                if (properties != null)
                {
                    System.Collections.Generic.List<JProperty> propList = properties.Properties().ToList();
                    for (int i = 0; i < propList.Count; i++)
                    {
                        JProperty prop = propList[i];
                        JObject propSchema = prop.Value as JObject;
                        string propType = propSchema?["type"]?.ToString() ?? "string";
                        string description = propSchema?["description"]?.ToString() ?? "";
                        bool isRequired = required.Contains(prop.Name);

                        string exampleValue = GenerateExampleValue(propType, propSchema, prop.Name);
                        string comma = i < propList.Count - 1 ? "," : "";
                        string reqMark = isRequired ? " // REQUIRED" : " // optional";

                        // Add description as comment if available
                        if (!string.IsNullOrEmpty(description) && description.Length < 60)
                        {
                            sb.AppendLine($"      \"{prop.Name}\": {exampleValue}{comma}{reqMark} - {description}");
                        }
                        else
                        {
                            sb.AppendLine($"      \"{prop.Name}\": {exampleValue}{comma}{reqMark}");
                        }
                    }
                }
                else
                {
                    sb.AppendLine("      // No parameters");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"      // Error generating example: {ex.Message}");
            }

            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"id\": 1");
            sb.AppendLine("}");
            sb.AppendLine("```");

            return sb.ToString();
        }

        /// <summary>
        /// Generates an example value for a parameter based on its type
        /// </summary>
        private static string GenerateExampleValue(string type, JObject schema, string paramName)
        {
            // Check for enum values first
            string[] enumValues = schema?["enum"]?.ToObject<string[]>();
            if (enumValues != null && enumValues.Length > 0)
            {
                return $"\"{enumValues[0]}\"";
            }

            // Generate value based on type
            return type switch
            {
                "string" => $"\"example_{paramName}\"",
                "integer" => "0",
                "number" => "0.0",
                "boolean" => "true",
                "object" => "{}",
                "array" => "[]",
                _ => "null"
            };
        }

        /// <summary>
        /// Generates a compact example showing only required parameters
        /// </summary>
        public static string GenerateMinimalExample(McpTool schema)
        {
            if (schema == null)
            {
                return "// No schema available";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"jsonrpc\": \"2.0\",");
            sb.AppendLine("  \"method\": \"tools/call\",");
            sb.AppendLine("  \"params\": {");
            sb.AppendLine($"    \"name\": \"{schema.Name}\",");
            sb.AppendLine("    \"arguments\": {");

            try
            {
                JObject schemaObj = JObject.FromObject(schema.InputSchema);
                JObject properties = schemaObj["properties"] as JObject;
                string[] required = schemaObj["required"]?.ToObject<string[]>() ?? new string[0];

                if (properties != null && required.Length > 0)
                {
                    for (int i = 0; i < required.Length; i++)
                    {
                        string paramName = required[i];
                        JObject propSchema = properties[paramName] as JObject;
                        string propType = propSchema?["type"]?.ToString() ?? "string";

                        string exampleValue = GenerateExampleValue(propType, propSchema, paramName);
                        string comma = i < required.Length - 1 ? "," : "";

                        sb.AppendLine($"      \"{paramName}\": {exampleValue}{comma}");
                    }
                }
                else
                {
                    sb.AppendLine("      // No required parameters");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"      // Error generating example: {ex.Message}");
            }

            sb.AppendLine("    }");
            sb.AppendLine("  },");
            sb.AppendLine("  \"id\": 1");
            sb.AppendLine("}");
            sb.AppendLine("```");

            return sb.ToString();
        }
    }
}