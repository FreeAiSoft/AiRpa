using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing all breakpoints in the workflow
    /// </summary>
    public class ListBreakpointsTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public ListBreakpointsTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_breakpoints",
                Description = "Lists all breakpoints set in the current workflow",
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

                    List<object> breakpoints = new List<object>();
                    CollectBreakpoints(modelService.Root, breakpoints);

                    return Success(new
                    {
                        message = $"Found {breakpoints.Count} breakpoint(s)",
                        count = breakpoints.Count,
                        breakpoints
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to list breakpoints: {ex.Message}");
                }
            });
        }

        private void CollectBreakpoints(ModelItem item, List<object> breakpoints)
        {
            if (item == null)
            {
                return;
            }

            // Check if this activity has a breakpoint
            bool? hasBreakpoint = item.Properties["Breakpoint"]?.ComputedValue as bool?;
            if (hasBreakpoint == true)
            {
                string activityId = item.GetCurrentValue()?.GetType().GetProperty("Id")?.GetValue(item.GetCurrentValue())?.ToString();
                string displayName = item.Properties["DisplayName"]?.ComputedValue?.ToString();
                string activityType = item.ItemType?.Name;

                breakpoints.Add(new
                {
                    activityId,
                    activityName = displayName,
                    activityType,
                    enabled = true
                });
            }

            // Recursively check child activities
            foreach (ModelProperty property in item.Properties)
            {
                if (property.Value != null)
                {
                    CollectBreakpoints(property.Value, breakpoints);
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem child in property.Collection)
                    {
                        CollectBreakpoints(child, breakpoints);
                    }
                }
            }
        }
    }
}
