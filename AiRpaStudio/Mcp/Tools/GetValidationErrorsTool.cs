using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Validation;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;
using RPA.Views;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for getting validation errors from the current workflow
    /// </summary>
    public class GetValidationErrorsTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public GetValidationErrorsTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "get_validation_errors",
                Description = "Gets validation errors from the current workflow in the designer. Returns all validation issues including errors and warnings for activities.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includeWarnings = new
                        {
                            type = "boolean",
                            description = "Include validation warnings in addition to errors",
                            @default = true
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
                    // Check if designer is active
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    WorkflowDesignerEx activeDesigner = _designer.ActiveWorkflowDesignerEx;

                    // Parse arguments
                    JObject args = arguments != null ? JObject.FromObject(arguments) : new JObject();
                    bool includeWarnings = args["includeWarnings"]?.ToObject<bool>() ?? true;

                    // Flush designer to ensure latest changes are reflected
                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        activeDesigner.Flush();
                    });

                    // Get the root activity from the designer
                    Activity rootActivity = null;
                    ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                    {
                        ModelService modelService = activeDesigner.Context.Services.GetService<ModelService>();
                        if (modelService?.Root != null)
                        {
                            ModelItem rootModelItem = modelService.Root;
                            ModelProperty implementationProperty = rootModelItem.Properties["Implementation"];

                            if (implementationProperty?.Value != null)
                            {
                                rootActivity = implementationProperty.Value.GetCurrentValue() as Activity;
                            }
                        }
                    });

                    if (rootActivity == null)
                    {
                        return Success(new
                        {
                            hasErrors = false,
                            hasWarnings = false,
                            errorCount = 0,
                            warningCount = 0,
                            errors = new List<object>(),
                            message = "Workflow is empty or implementation is not available."
                        });
                    }

                    // Validate the workflow using ActivityValidationServices
                    ValidationResults validationResults = ActivityValidationServices.Validate(rootActivity);

                    if (validationResults.Errors == null || validationResults.Errors.Count == 0)
                    {
                        return Success(new
                        {
                            hasErrors = false,
                            hasWarnings = false,
                            errorCount = 0,
                            warningCount = 0,
                            errors = new List<object>(),
                            message = "No validation issues found. Workflow is valid."
                        });
                    }

                    // Process validation errors
                    List<object> errorList = new List<object>();
                    int errorCount = 0;
                    int warningCount = 0;

                    foreach (System.Activities.Validation.ValidationError validationError in validationResults.Errors)
                    {
                        bool isWarning = validationError.IsWarning;

                        if (!includeWarnings && isWarning)
                        {
                            continue;
                        }

                        if (isWarning)
                        {
                            warningCount++;
                        }
                        else
                        {
                            errorCount++;
                        }

                        errorList.Add(new
                        {
                            type = isWarning ? "warning" : "error",
                            message = validationError.Message,
                            id = validationError.Id,
                            source = validationError.Source?.DisplayName ?? "Unknown source",
                            propertyName = validationError.PropertyName
                        });
                    }

                    return Success(new
                    {
                        hasErrors = errorCount > 0,
                        hasWarnings = warningCount > 0,
                        errorCount,
                        warningCount,
                        totalIssues = errorCount + warningCount,
                        errors = errorList,
                        message = errorCount > 0
                            ? $"Found {errorCount} error(s) and {warningCount} warning(s)"
                            : warningCount > 0
                                ? $"Found {warningCount} warning(s)"
                                : "No validation issues found"
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to get validation errors: {ex.Message}");
                }
            });
        }
    }
}
