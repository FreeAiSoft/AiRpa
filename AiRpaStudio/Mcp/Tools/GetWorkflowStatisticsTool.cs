using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting detailed workflow statistics
    /// </summary>
    public class GetWorkflowStatisticsTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetWorkflowStatisticsTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_workflow_statistics",
                Description = "Gets detailed statistics about the workflow including activity counts, nesting depth, variables, and arguments",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    // Collect statistics
                    WorkflowStatistics stats = new WorkflowStatistics();
                    CollectStatistics(modelService.Root, stats, 0);

                    // Count variables and arguments
                    int variableCount = CountVariables(modelService.Root);
                    int argumentCount = CountArguments(modelService.Root);

                    return Success(new
                    {
                        message = "Workflow statistics collected",
                        totalActivities = stats.TotalActivities,
                        maxNestingDepth = stats.MaxDepth,
                        variables = variableCount,
                        arguments = argumentCount,
                        activityTypes = stats.ActivityTypeCounts,
                        controlFlow = new
                        {
                            sequences = stats.ActivityTypeCounts.ContainsKey("Sequence") ? stats.ActivityTypeCounts["Sequence"] : 0,
                            conditionals = stats.ActivityTypeCounts.ContainsKey("If") ? stats.ActivityTypeCounts["If"] : 0,
                            loops = GetLoopCount(stats.ActivityTypeCounts),
                            parallels = stats.ActivityTypeCounts.ContainsKey("Parallel") ? stats.ActivityTypeCounts["Parallel"] : 0,
                            tryCatch = stats.ActivityTypeCounts.ContainsKey("TryCatch") ? stats.ActivityTypeCounts["TryCatch"] : 0
                        },
                        workflowFile = _designer.ActiveWorkflowDesignerEx.CurrentWorkflowFile
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to get workflow statistics: {ex.Message}");
                }
            });
        }

        private class WorkflowStatistics
        {
            public int TotalActivities { get; set; }
            public int MaxDepth { get; set; }
            public Dictionary<string, int> ActivityTypeCounts { get; set; } = new Dictionary<string, int>();
        }

        private void CollectStatistics(ModelItem item, WorkflowStatistics stats, int depth)
        {
            if (item == null)
            {
                return;
            }

            stats.TotalActivities++;
            stats.MaxDepth = Math.Max(stats.MaxDepth, depth);

            // Count activity type
            string typeName = item.ItemType?.Name;
            if (!string.IsNullOrEmpty(typeName))
            {
                if (!stats.ActivityTypeCounts.ContainsKey(typeName))
                {
                    stats.ActivityTypeCounts[typeName] = 0;
                }
                stats.ActivityTypeCounts[typeName]++;
            }

            // Recursively process children
            foreach (ModelProperty property in item.Properties)
            {
                if (property.Value != null)
                {
                    CollectStatistics(property.Value, stats, depth + 1);
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem child in property.Collection)
                    {
                        CollectStatistics(child, stats, depth + 1);
                    }
                }
            }
        }

        private int CountVariables(ModelItem root)
        {
            int count = 0;
            if (root == null)
            {
                return count;
            }

            // Check if this item has Variables property
            ModelProperty variablesProperty = root.Properties["Variables"];
            if (variablesProperty?.Collection != null)
            {
                count += variablesProperty.Collection.Count;
            }

            // Recursively check children
            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    count += CountVariables(property.Value);
                }

                if (property.Collection != null && property.Name != "Variables")
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        count += CountVariables(item);
                    }
                }
            }

            return count;
        }

        private int CountArguments(ModelItem root)
        {
            if (root == null)
            {
                return 0;
            }

            // Check for Properties property (which contains arguments in ActivityBuilder)
            ModelProperty propertiesProperty = root.Properties["Properties"];
            return propertiesProperty?.Collection != null ? propertiesProperty.Collection.Count : 0;
        }

        private int GetLoopCount(Dictionary<string, int> activityTypes)
        {
            int count = 0;
            if (activityTypes.ContainsKey("While"))
            {
                count += activityTypes["While"];
            }

            if (activityTypes.ContainsKey("DoWhile"))
            {
                count += activityTypes["DoWhile"];
            }

            if (activityTypes.ContainsKey("ForEach"))
            {
                count += activityTypes["ForEach"];
            }

            return count;
        }
    }
}
