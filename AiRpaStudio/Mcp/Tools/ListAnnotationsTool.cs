using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for listing all annotations/comments in the workflow
    /// </summary>
    public class ListAnnotationsTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public ListAnnotationsTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "list_annotations",
                Description = "Lists all annotations and comments in the current workflow",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        filterType = new
                        {
                            type = "string",
                            description = "Filter by annotation type (optional)",
                            @enum = new[] { "comment", "note", "warning", "todo", "all" },
                            @default = "all"
                        }
                    }
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

                    List<object> annotations = new List<object>();
                    CollectAnnotations(modelService.Root, annotations);

                    return Success(new
                    {
                        message = $"Found {annotations.Count} annotation(s)",
                        count = annotations.Count,
                        annotations
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to list annotations: {ex.Message}");
                }
            });
        }

        private void CollectAnnotations(ModelItem item, List<object> annotations)
        {
            if (item == null)
            {
                return;
            }

            // Check for annotation
            ModelProperty annotationProperty = item.Properties["Annotation.AnnotationText"];
            string annotationText = annotationProperty?.ComputedValue?.ToString();

            if (!string.IsNullOrEmpty(annotationText))
            {
                string activityId = item.GetCurrentValue()?.GetType().GetProperty("Id")?.GetValue(item.GetCurrentValue())?.ToString();
                string displayName = item.Properties["DisplayName"]?.ComputedValue?.ToString();
                string activityType = item.ItemType?.Name;

                // Parse annotation type if formatted
                string annotationType = "comment";
                string text = annotationText;

                if (annotationText.StartsWith("[") && annotationText.Contains("]"))
                {
                    int endBracket = annotationText.IndexOf("]");
                    annotationType = annotationText.Substring(1, endBracket - 1).ToLower();
                    text = annotationText.Substring(endBracket + 1).Trim();
                }

                annotations.Add(new
                {
                    activityId,
                    activityName = displayName,
                    activityType,
                    annotationType,
                    text
                });
            }

            // Recursively check child activities
            foreach (ModelProperty property in item.Properties)
            {
                if (property.Value != null)
                {
                    CollectAnnotations(property.Value, annotations);
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem child in property.Collection)
                    {
                        CollectAnnotations(child, annotations);
                    }
                }
            }
        }
    }
}
