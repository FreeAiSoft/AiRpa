using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing all available activity types
    /// </summary>
    public class ListActivitiesTool : BaseMcpTool
    {
        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_activities",
                Description = "Lists all available activity types with descriptions and parameters. Can optionally filter by category.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        category = new
                        {
                            type = "string",
                            description = "Filter by category (optional)",
                            @enum = new[] { "ControlFlow", "Primitives", "ErrorHandling", "Flow", "Collection", "All" }
                        }
                    }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                JObject args = JObject.FromObject(arguments);
                string category = args["category"]?.ToString() ?? "All";

                List<object> activities = GetActivitiesByCategory(category);

                return Success(new
                {
                    category,
                    count = activities.Count,
                    activities
                });
            });
        }

        private List<object> GetActivitiesByCategory(string category)
        {
            List<object> allActivities = new List<object>
            {
                // Control Flow Activities
                new
                {
                    type = "Sequence",
                    displayName = "Sequence",
                    category = "ControlFlow",
                    description = "Executes activities in sequential order. Container for other activities.",
                    parameters = new[]
                    {
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "If",
                    displayName = "If",
                    category = "ControlFlow",
                    description = "Conditional activity. Executes Then branch if condition is true, otherwise Else branch.",
                    parameters = new[]
                    {
                        new { name = "Condition", type = "boolean", required = true, description = "Condition expression" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "While",
                    displayName = "While",
                    category = "ControlFlow",
                    description = "Loop activity. Executes body while condition is true.",
                    parameters = new[]
                    {
                        new { name = "Condition", type = "boolean", required = true, description = "Loop condition" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "DoWhile",
                    displayName = "Do While",
                    category = "ControlFlow",
                    description = "Loop activity. Executes body at least once, then continues while condition is true.",
                    parameters = new[]
                    {
                        new { name = "Condition", type = "boolean", required = true, description = "Loop condition" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "ForEach",
                    displayName = "For Each",
                    category = "ControlFlow",
                    description = "Iterates over a collection and executes body for each item.",
                    parameters = new[]
                    {
                        new { name = "Values", type = "IEnumerable", required = true, description = "Collection to iterate" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "Switch",
                    displayName = "Switch",
                    category = "ControlFlow",
                    description = "Multi-branch conditional activity. Executes a case based on expression value.",
                    parameters = new[]
                    {
                        new { name = "Expression", type = "object", required = true, description = "Expression to evaluate" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },

                // Primitive Activities
                new
                {
                    type = "WriteLine",
                    displayName = "Write Line",
                    category = "Primitives",
                    description = "Writes text to console output.",
                    parameters = new[]
                    {
                        new { name = "Text", type = "string", required = true, description = "Text to write" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "Assign",
                    displayName = "Assign",
                    category = "Primitives",
                    description = "Assigns a value to a variable.",
                    parameters = new[]
                    {
                        new { name = "To", type = "variable", required = true, description = "Target variable" },
                        new { name = "Value", type = "object", required = true, description = "Value to assign" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "Delay",
                    displayName = "Delay",
                    category = "Primitives",
                    description = "Delays execution for specified duration.",
                    parameters = new[]
                    {
                        new { name = "Duration", type = "TimeSpan", required = true, description = "Duration to wait" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "InvokeMethod",
                    displayName = "Invoke Method",
                    category = "Primitives",
                    description = "Invokes a method on an object or type.",
                    parameters = new[]
                    {
                        new { name = "TargetObject", type = "object", required = false, description = "Target object (null for static)" },
                        new { name = "MethodName", type = "string", required = true, description = "Method name to invoke" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },

                // Error Handling Activities
                new
                {
                    type = "TryCatch",
                    displayName = "Try Catch",
                    category = "ErrorHandling",
                    description = "Exception handling activity. Executes Try block and catches exceptions.",
                    parameters = new[]
                    {
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "Throw",
                    displayName = "Throw",
                    category = "ErrorHandling",
                    description = "Throws an exception.",
                    parameters = new[]
                    {
                        new { name = "Exception", type = "Exception", required = true, description = "Exception to throw" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "Rethrow",
                    displayName = "Rethrow",
                    category = "ErrorHandling",
                    description = "Rethrows the current exception in a catch block.",
                    parameters = new[]
                    {
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },

                // Flow Activities
                new
                {
                    type = "Flowchart",
                    displayName = "Flowchart",
                    category = "Flow",
                    description = "Visual workflow designer with flowchart nodes.",
                    parameters = new[]
                    {
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "FlowDecision",
                    displayName = "Flow Decision",
                    category = "Flow",
                    description = "Decision node in flowchart. Routes to True or False branch.",
                    parameters = new[]
                    {
                        new { name = "Condition", type = "boolean", required = true, description = "Decision condition" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "FlowSwitch",
                    displayName = "Flow Switch",
                    category = "Flow",
                    description = "Multi-branch decision node in flowchart.",
                    parameters = new[]
                    {
                        new { name = "Expression", type = "object", required = true, description = "Expression to evaluate" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },

                // Collection Activities
                new
                {
                    type = "AddToCollection",
                    displayName = "Add To Collection",
                    category = "Collection",
                    description = "Adds an item to a collection.",
                    parameters = new[]
                    {
                        new { name = "Collection", type = "ICollection", required = true, description = "Target collection" },
                        new { name = "Item", type = "object", required = true, description = "Item to add" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "RemoveFromCollection",
                    displayName = "Remove From Collection",
                    category = "Collection",
                    description = "Removes an item from a collection.",
                    parameters = new[]
                    {
                        new { name = "Collection", type = "ICollection", required = true, description = "Target collection" },
                        new { name = "Item", type = "object", required = true, description = "Item to remove" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "ClearCollection",
                    displayName = "Clear Collection",
                    category = "Collection",
                    description = "Clears all items from a collection.",
                    parameters = new[]
                    {
                        new { name = "Collection", type = "ICollection", required = true, description = "Target collection" },
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },

                // Parallel Activities
                new
                {
                    type = "Parallel",
                    displayName = "Parallel",
                    category = "ControlFlow",
                    description = "Executes multiple branches in parallel.",
                    parameters = new[]
                    {
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                },
                new
                {
                    type = "Pick",
                    displayName = "Pick",
                    category = "ControlFlow",
                    description = "Waits for multiple triggers and executes the first one that completes.",
                    parameters = new[]
                    {
                        new { name = "DisplayName", type = "string", required = false, description = "Name shown in designer" }
                    }
                }
            };

            // Filter by category if specified
            return category != "All"
                ? allActivities.FindAll(a =>
                {
                    dynamic activity = a;
                    return activity.category == category;
                })
                : allActivities;
        }
    }
}