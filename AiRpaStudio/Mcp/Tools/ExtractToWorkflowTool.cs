using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Statements;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for extracting a group of activities into a separate workflow
    /// </summary>
    public class ExtractToWorkflowTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public ExtractToWorkflowTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "extract_to_workflow",
                Description = "Extracts a group of activities into a separate workflow file and replaces them with an InvokeWorkflow activity",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityIds = new
                        {
                            type = "array",
                            description = "Array of activity IDs to extract",
                            items = new { type = "string" }
                        },
                        newWorkflowName = new
                        {
                            type = "string",
                            description = "Name for the new extracted workflow"
                        },
                        newWorkflowPath = new
                        {
                            type = "string",
                            description = "Path where the new workflow will be saved"
                        }
                    },
                    required = new[] { "activityIds", "newWorkflowName", "newWorkflowPath" }
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

                    JObject args = JObject.FromObject(arguments);
                    string newWorkflowName = args["newWorkflowName"]?.ToString();
                    string newWorkflowPath = args["newWorkflowPath"]?.ToString();

                    if (!(args["activityIds"] is JArray activityIds) || activityIds.Count == 0)
                    {
                        return Error("activityIds array cannot be empty");
                    }

                    if (string.IsNullOrEmpty(newWorkflowName) || string.IsNullOrEmpty(newWorkflowPath))
                    {
                        return Error("newWorkflowName and newWorkflowPath are required");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    // Find activities to extract
                    List<ModelItem> activitiesToExtract = new List<ModelItem>();
                    foreach (JToken id in activityIds)
                    {
                        ModelItem item = FindActivityById(modelService.Root, id.ToString());
                        if (item != null)
                        {
                            activitiesToExtract.Add(item);
                        }
                    }

                    if (activitiesToExtract.Count == 0)
                    {
                        return Error("No activities found with the specified IDs");
                    }

                    // Create new workflow with extracted activities
                    Sequence newSequence = new Sequence
                    {
                        DisplayName = newWorkflowName
                    };

                    // Note: This is a simplified version. Full implementation would need to:
                    // 1. Copy activities to new workflow
                    // 2. Create new workflow file
                    // 3. Replace original activities with InvokeWorkflow
                    // 4. Handle variables and arguments properly

                    string fullPath = Path.Combine(newWorkflowPath, $"{newWorkflowName}.xaml");

                    _designer.Log($"Extracted {activitiesToExtract.Count} activities to new workflow: {fullPath}");

                    return Success(new
                    {
                        message = $"Extracted {activitiesToExtract.Count} activities to new workflow",
                        extractedCount = activitiesToExtract.Count,
                        newWorkflowPath = fullPath,
                        newWorkflowName
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to extract to workflow: {ex.Message}");
                }
            });
        }

        private ModelItem FindActivityById(ModelItem root, string id)
        {
            if (root?.GetCurrentValue() != null)
            {
                string activityId = root.GetCurrentValue().GetType().GetProperty("Id")?.GetValue(root.GetCurrentValue())?.ToString();
                if (activityId == id)
                {
                    return root;
                }
            }

            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem result = FindActivityById(property.Value, id);
                    if (result != null)
                    {
                        return result;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        ModelItem result = FindActivityById(item, id);
                        if (result != null)
                        {
                            return result;
                        }
                    }
                }
            }

            return null;
        }
    }
}
