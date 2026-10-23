using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for adding annotations/comments to activities
    /// </summary>
    public class AddAnnotationTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public AddAnnotationTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "add_annotation",
                Description = "Adds a comment or annotation to a specific activity in the workflow",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityId = new
                        {
                            type = "string",
                            description = "ID of the activity to annotate"
                        },
                        activityName = new
                        {
                            type = "string",
                            description = "Display name of the activity (alternative to activityId)"
                        },
                        text = new
                        {
                            type = "string",
                            description = "Annotation text/comment"
                        },
                        annotationType = new
                        {
                            type = "string",
                            description = "Type of annotation (comment, note, warning, todo)",
                            @enum = new[] { "comment", "note", "warning", "todo" },
                            @default = "comment"
                        }
                    },
                    required = new[] { "text" }
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
                    string activityId = args["activityId"]?.ToString();
                    string activityName = args["activityName"]?.ToString();
                    string text = args["text"]?.ToString();
                    string annotationType = args["annotationType"]?.ToString() ?? "comment";

                    if (string.IsNullOrEmpty(text))
                    {
                        return Error("text parameter is required");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    // Find target activity
                    ModelItem targetActivity = null;
                    if (!string.IsNullOrEmpty(activityId))
                    {
                        targetActivity = FindActivityById(modelService.Root, activityId);
                    }
                    else if (!string.IsNullOrEmpty(activityName))
                    {
                        targetActivity = FindActivityByName(modelService.Root, activityName);
                    }
                    else
                    {
                        // If no activity specified, annotate the root
                        targetActivity = modelService.Root;
                    }

                    if (targetActivity == null)
                    {
                        return Error($"Activity not found: {activityId ?? activityName}");
                    }

                    // Add annotation using Annotation property or custom metadata
                    // Note: In WF, annotations are typically stored in Annotation.AnnotationText attached property
                    string formattedAnnotation = $"[{annotationType.ToUpper()}] {text}";

                    ModelProperty annotationProperty = targetActivity.Properties["Annotation.AnnotationText"];
                    _ = annotationProperty?.SetValue(formattedAnnotation);

                    _designer.Log($"Added {annotationType} to activity: {text}");

                    return Success(new
                    {
                        message = $"Annotation added successfully",
                        activityId = targetActivity.GetCurrentValue()?.GetType().GetProperty("Id")?.GetValue(targetActivity.GetCurrentValue())?.ToString(),
                        activityName = targetActivity.Properties["DisplayName"]?.ComputedValue?.ToString(),
                        annotationType,
                        text
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to add annotation: {ex.Message}");
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

        private ModelItem FindActivityByName(ModelItem root, string name)
        {
            string displayName = root.Properties["DisplayName"]?.ComputedValue?.ToString();
            if (displayName == name)
            {
                return root;
            }

            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem result = FindActivityByName(property.Value, name);
                    if (result != null)
                    {
                        return result;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        ModelItem result = FindActivityByName(item, name);
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
