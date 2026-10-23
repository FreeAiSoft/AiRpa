using System;
using System.Activities;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core;
using RPA.Views;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for adding activities to the workflow
    /// </summary>
    public class AddActivityTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public AddActivityTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "add_activity",
                Description = "Adds a new activity to the current workflow. Can create control flow activities (Sequence, If, While, ForEach) and primitive activities (WriteLine, Assign, Delay, etc.). Supports adding to specific parent containers with optional position control.",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        activityType = new
                        {
                            type = "string",
                            description = "Type of activity to add",
                            @enum = new[]
                            {
                                "Sequence", "If", "While", "DoWhile", "ForEach",
                                "WriteLine", "Assign", "Delay", "InvokeMethod",
                                "TryCatch", "Flowchart", "Parallel", "Pick",
                                "Switch", "FlowDecision", "FlowSwitch"
                            }
                        },
                        parentId = new
                        {
                            type = "string",
                            description = "ID of parent activity to add to (optional). If not specified, adds to root Implementation. Use get_activity_tree to get activity IDs."
                        },
                        position = new
                        {
                            type = "integer",
                            description = "Position index to insert activity (optional). 0=start, positive=specific position, not specified=end. If position exceeds collection size, adds to end."
                        },
                        properties = new
                        {
                            type = "object",
                            description = "Activity properties (e.g., DisplayName, Duration for Delay, Text for WriteLine)",
                            additionalProperties = true
                        },
                        arguments = new
                        {
                            type = "object",
                            description = "Activity arguments stored in Arguments collection",
                            additionalProperties = true
                        },
                        children = new
                        {
                            type = "array",
                            description = "Child activities for container activities",
                            items = new
                            {
                                type = "object"
                            }
                        }
                    },
                    required = new[] { "activityType" }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                // STEP 1: Validate arguments using the new validation system
                ValidationResult validation = ValidateArguments(arguments);
                if (!validation.IsValid)
                {
                    return ValidationError(validation);
                }

                try
                {
                    // Check if designer is active
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    // Parse arguments (now we know they're valid)
                    JObject args = JObject.FromObject(arguments);
                    string activityType = args["activityType"]?.ToString();
                    string parentId = args["parentId"]?.ToString();
                    int? position = args["position"]?.ToObject<int?>();

                    // Additional business logic validation
                    if (position.HasValue && position.Value < 0)
                    {
                        return ValidationError(
                            ValidationErrorType.InvalidParameterValue,
                            "position must be non-negative (0 or greater)",
                            "position",
                            "integer >= 0",
                            position.Value.ToString()
                        );
                    }

                    JObject properties = args["properties"] as JObject;
                    JObject activityArguments = args["arguments"] as JObject;
                    JArray children = args["children"] as JArray;

                    // Create activity
                    Activity activity = CreateActivity(activityType, properties, activityArguments, children);

                    if (activity == null)
                    {
                        return Error($"Failed to create activity of type '{activityType}'. Type may not be supported.");
                    }

                    // Add to designer with parent and position support
                    bool added = AddActivityToDesigner(activity, parentId, position);

                    return !added
                        ? Error($"Failed to add activity '{activityType}' to designer")
                        : Success(new
                        {
                            message = $"Activity '{activityType}' successfully added to workflow",
                            activityType,
                            displayName = activity.DisplayName,
                            id = activity.Id,
                            parentId = string.IsNullOrEmpty(parentId) ? "root.Implementation" : parentId,
                            position = position.HasValue ? $"index {position.Value}" : "end"
                        });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to add activity: {ex.Message}");
                }
            });
        }

#pragma warning disable IDE0060 // Remove unused parameter
        private Activity CreateActivity(string activityType, JObject properties, JObject arguments, JArray children)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            Activity activity;

            // Create activity based on type
            switch (activityType)
            {
                case "Sequence":
                    Sequence sequence = new Sequence();
                    if (children != null && children.Count > 0)
                    {
                        foreach (JObject child in children.Cast<JObject>())
                        {
                            Activity childActivity = CreateActivity(
                                child["activityType"]?.ToString(),
                                child["properties"] as JObject,
                                child["arguments"] as JObject,
                                child["children"] as JArray
                            );
                            if (childActivity != null)
                            {
                                sequence.Activities.Add(childActivity);
                            }
                        }
                    }
                    activity = sequence;
                    break;

                case "WriteLine":
                    WriteLine writeLine = new WriteLine();
                    // Set Text property if provided
                    JToken textToken = properties?.GetValue("Text", StringComparison.OrdinalIgnoreCase);
                    if (textToken != null)
                    {
                        writeLine.Text = textToken.ToString();
                    }
                    activity = writeLine;
                    break;

                case "Delay":
                    Delay delay = new Delay();
                    // Set Duration property if provided
                    JToken durationToken = properties?.GetValue("Duration", StringComparison.OrdinalIgnoreCase);
                    if (durationToken != null)
                    {
                        if (TimeSpan.TryParse(durationToken.ToString(), out TimeSpan duration))
                        {
                            delay.Duration = duration;
                        }
                    }
                    activity = delay;
                    break;

                case "Assign":
                    Assign assign = new Assign();
                    // Note: Assign requires To and Value properties which need variable references
                    // This is a simplified implementation
                    activity = assign;
                    break;

                case "If":
                    If ifActivity = new If();
                    // Note: If requires Condition which needs expression
                    if (children != null && children.Count > 0)
                    {
                        if (children[0] != null)
                        {
                            ifActivity.Then = CreateActivity(
                                children[0]["activityType"]?.ToString(),
                                children[0]["properties"] as JObject,
                                children[0]["arguments"] as JObject,
                                children[0]["children"] as JArray
                            );
                        }
                        if (children.Count > 1 && children[1] != null)
                        {
                            ifActivity.Else = CreateActivity(
                                children[1]["activityType"]?.ToString(),
                                children[1]["properties"] as JObject,
                                children[1]["arguments"] as JObject,
                                children[1]["children"] as JArray
                            );
                        }
                    }
                    activity = ifActivity;
                    break;

                case "While":
                    While whileActivity = new While();
                    if (children != null && children.Count > 0)
                    {
                        whileActivity.Body = CreateActivity(
                            children[0]["activityType"]?.ToString(),
                            children[0]["properties"] as JObject,
                            children[0]["arguments"] as JObject,
                            children[0]["children"] as JArray
                        );
                    }
                    activity = whileActivity;
                    break;

                case "DoWhile":
                    DoWhile doWhile = new DoWhile();
                    if (children != null && children.Count > 0)
                    {
                        doWhile.Body = CreateActivity(
                            children[0]["activityType"]?.ToString(),
                            children[0]["properties"] as JObject,
                            children[0]["arguments"] as JObject,
                            children[0]["children"] as JArray
                        );
                    }
                    activity = doWhile;
                    break;

                case "ForEach":
                    // Note: ForEach<T> requires generic type parameter
                    // This is a simplified version
                    activity = new Sequence(); // Placeholder
                    break;

                case "TryCatch":
                    TryCatch tryCatch = new TryCatch();
                    if (children != null && children.Count > 0)
                    {
                        tryCatch.Try = CreateActivity(
                            children[0]["activityType"]?.ToString(),
                            children[0]["properties"] as JObject,
                            children[0]["arguments"] as JObject,
                            children[0]["children"] as JArray
                        );
                    }
                    activity = tryCatch;
                    break;

                case "Flowchart":
                    Flowchart flowchart = new Flowchart();
                    activity = flowchart;
                    break;

                case "Parallel":
                    System.Activities.Statements.Parallel parallel = new System.Activities.Statements.Parallel();
                    if (children != null)
                    {
                        foreach (JObject child in children.Cast<JObject>())
                        {
                            Activity childActivity = CreateActivity(
                                child["activityType"]?.ToString(),
                                child["properties"] as JObject,
                                child["arguments"] as JObject,
                                child["children"] as JArray
                            );
                            if (childActivity != null)
                            {
                                parallel.Branches.Add(childActivity);
                            }
                        }
                    }
                    activity = parallel;
                    break;

                case "Pick":
                    Pick pick = new Pick();
                    activity = pick;
                    break;

                default:
                    return null;
            }

            // Set display name from properties if provided
            if (activity != null)
            {
                string displayName = properties?.GetValue("DisplayName", StringComparison.OrdinalIgnoreCase)?.ToString();
                if (!string.IsNullOrEmpty(displayName))
                {
                    activity.DisplayName = displayName;
                }
            }

            return activity;
        }

        /// <summary>
        /// Adds the created activity to the workflow designer with support for parent selection and positioning
        /// </summary>
        /// <param name="activity">Activity to add</param>
        /// <param name="parentId">ID of parent activity (optional, uses root Implementation if not specified)</param>
        /// <param name="position">Position index to insert (optional, adds to end if not specified)</param>
        private bool AddActivityToDesigner(Activity activity, string parentId, int? position)
        {
            try
            {
                WorkflowDesignerEx designer = _designer.ActiveWorkflowDesignerEx;
                if (designer == null)
                {
                    _designer.Log("No active workflow designer");
                    return false;
                }

                ModelService modelService = designer.Context.Services.GetService<ModelService>();
                ModelTreeManager modelTreeManager = designer.Context.Services.GetService<ModelTreeManager>();

                if (modelService?.Root == null || modelTreeManager == null)
                {
                    _designer.Log("Failed to get required services");
                    return false;
                }

                // Create ModelItem for the new activity
                ModelItem modelItem = modelTreeManager.CreateModelItem(null, activity);
                ModelItem root = modelService.Root;

                // Determine target container
                ModelItem targetContainer;

                if (string.IsNullOrEmpty(parentId))
                {
                    // MODE 1: Simple - add to Implementation
                    targetContainer = GetOrCreateImplementation(root, modelTreeManager);
                }
                else
                {
                    // MODE 2: Extended - add to specified parent
                    targetContainer = FindModelItemById(root, parentId);

                    if (targetContainer == null)
                    {
                        _designer.Log($"Parent activity with id '{parentId}' not found");
                        return false;
                    }

                    // Special handling for Flowchart
                    if (targetContainer.ItemType == typeof(Flowchart))
                    {
                        modelItem = WrapInFlowStep(activity, modelTreeManager);
                    }
                }

                // Get activities collection from target container
                ModelItemCollection collection = GetActivitiesCollection(targetContainer);

                if (collection == null)
                {
                    _designer.Log($"Target container does not support child activities");
                    return false;
                }

                // Add activity with position support
                bool success = false;
                ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                {
                    success = AddActivityAtIndex(collection, modelItem, position);
                    if (success)
                    {
                        designer.Flush();
                    }
                });

                return success;
            }
            catch (Exception ex)
            {
                _designer.Log($"Failed to add to designer: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets or creates the Implementation property of the root ActivityBuilder
        /// </summary>
        private ModelItem GetOrCreateImplementation(ModelItem root, ModelTreeManager modelTreeManager)
        {
            if (root.ItemType != typeof(ActivityBuilder))
            {
                throw new Exception($"Unexpected root type: {root.ItemType.Name}. Expected ActivityBuilder.");
            }

            ModelItem implementation = root.Properties["Implementation"].Value;

            if (implementation == null)
            {
                // Create new Sequence if Implementation is empty
                ModelItem sequenceItem = modelTreeManager.CreateModelItem(null, new Sequence());

                ((MainView)_designer.ViewModel.View).Dispatcher.Invoke(() =>
                {
                    _ = root.Properties["Implementation"].SetValue(sequenceItem);
                });

                return sequenceItem;
            }

            return implementation;
        }

        /// <summary>
        /// Finds a ModelItem by activity ID
        /// </summary>
        private ModelItem FindModelItemById(ModelItem root, string id)
        {
            return FindModelItemById(root, id, new HashSet<ModelItem>());
        }

        /// <summary>
        /// Finds a ModelItem by activity ID (internal implementation with cycle detection)
        /// </summary>
        private ModelItem FindModelItemById(ModelItem root, string id, HashSet<ModelItem> visited)
        {
            if (root == null)
            {
                return null;
            }

            // Prevent infinite recursion from circular references
            if (!visited.Add(root))
            {
                return null;
            }

            Activity activity = root.GetCurrentValue() as Activity;
            if (activity?.Id == id)
            {
                return root;
            }

            // Recursive search in properties and collections
            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    ModelItem found = FindModelItemById(property.Value, id, visited);
                    if (found != null)
                    {
                        return found;
                    }
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        ModelItem found = FindModelItemById(item, id, visited);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the activities collection from a container (supports Sequence, Parallel, Flowchart, etc.)
        /// </summary>
        private ModelItemCollection GetActivitiesCollection(ModelItem parent)
        {
            if (parent == null)
            {
                return null;
            }

            // Check various collection types
            if (parent.Properties["Activities"]?.Collection != null)
            {
                return parent.Properties["Activities"].Collection;  // Sequence, Parallel
            }

            if (parent.Properties["Branches"]?.Collection != null)
            {
                return parent.Properties["Branches"].Collection;     // Parallel branches
            }

            if (parent.Properties["Nodes"]?.Collection != null)
            {
                return parent.Properties["Nodes"].Collection;        // Flowchart
            }

            return null;
        }

        /// <summary>
        /// Adds activity to collection at specified position
        /// </summary>
        private bool AddActivityAtIndex(ModelItemCollection collection, ModelItem modelItem, int? position)
        {
            if (collection == null)
            {
                return false;
            }

            try
            {
                if (!position.HasValue || position.Value < 0 || position.Value >= collection.Count)
                {
                    // Add to end
                    collection.Add(modelItem);
                }
                else
                {
                    // Insert at position
                    collection.Insert(position.Value, modelItem);
                }
                return true;
            }
            catch (Exception ex)
            {
                _designer.Log($"Failed to add activity at position: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Wraps an activity in a FlowStep for Flowchart compatibility
        /// </summary>
        private ModelItem WrapInFlowStep(Activity activity, ModelTreeManager modelTreeManager)
        {
            FlowStep flowStep = new FlowStep
            {
                Action = activity
            };
            return modelTreeManager.CreateModelItem(null, flowStep);
        }
    }
}
