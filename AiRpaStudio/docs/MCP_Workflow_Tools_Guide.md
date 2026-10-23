# MCP Workflow Tools Guide

This guide describes all available MCP (Model Context Protocol) tools for working with RPA Studio workflows.

## Table of Contents

1. [Activity Manipulation Tools](#activity-manipulation-tools)
2. [Variable Management Tools](#variable-management-tools)
3. [Argument Management Tools](#argument-management-tools)
4. [Navigation Tools](#navigation-tools)
5. [Workflow Tools](#workflow-tools)
6. [Debugging and Execution Tools](#debugging-and-execution-tools)
7. [Import and Dependency Tools](#import-and-dependency-tools)
8. [Template Tools](#template-tools)
9. [Refactoring Tools](#refactoring-tools)
10. [Analysis and Metrics Tools](#analysis-and-metrics-tools)
11. [Comment and Annotation Tools](#comment-and-annotation-tools)
12. [File System Tools](#file-system-tools)

---

## Activity Manipulation Tools

### move_activity

Moves an activity to a different position within the workflow.

**Parameters:**
- `activityId` (required): ID of activity to move
- `targetContainerId` (optional): ID of target container activity
- `newIndex` (required): New index position (0-based)

**Example:**
```json
{
  "activityId": "123",
  "newIndex": 2
}
```

### copy_activity

Creates a copy of an activity with all its settings.

**Parameters:**
- `activityId` (required): ID of activity to copy
- `targetContainerId` (optional): ID of target container for the copy
- `newDisplayName` (optional): Display name for the copied activity

**Example:**
```json
{
  "activityId": "123",
  "newDisplayName": "My Activity Copy"
}
```

### get_activity_details

Gets detailed information about an activity including all properties and parameters.

**Parameters:**
- `activityId` (optional): ID of activity
- `displayName` (optional): Display name of activity

**Example:**
```json
{
  "activityId": "123"
}
```

### set_activity_property

Sets a single property of an activity.

**Parameters:**
- `activityId` (optional): ID of activity
- `displayName` (optional): Display name of activity
- `propertyName` (required): Name of the property to set
- `propertyValue` (required): Value to set

**Example:**
```json
{
  "activityId": "123",
  "propertyName": "Text",
  "propertyValue": "Hello World"
}
```

---

## Variable Management Tools

### create_variable

Creates a new variable in the specified activity scope.

**Parameters:**
- `name` (required): Name of the variable
- `variableType` (optional): Type of variable (default: "String")
  - Supported types: String, Int32, Int64, Boolean, Double, Decimal, DateTime, Object
- `defaultValue` (optional): Default value for the variable
- `scopeActivityId` (optional): ID of activity to use as scope

**Example:**
```json
{
  "name": "counter",
  "variableType": "Int32",
  "defaultValue": "0"
}
```

### list_variables

Lists all variables in the workflow with their scopes and types.

**Parameters:**
- `scopeActivityId` (optional): ID of specific activity to list variables for

**Example:**
```json
{
  "scopeActivityId": "123"
}
```

### update_variable

Updates properties of an existing variable.

**Parameters:**
- `currentName` (required): Current name of the variable
- `scopeActivityId` (optional): ID of activity where variable is defined
- `newName` (optional): New name for the variable
- `newDefaultValue` (optional): New default value

**Example:**
```json
{
  "currentName": "counter",
  "newDefaultValue": "10"
}
```

### delete_variable

Deletes a variable from its scope.

**Parameters:**
- `name` (required): Name of the variable to delete
- `scopeActivityId` (optional): ID of activity where variable is defined

**Example:**
```json
{
  "name": "counter"
}
```

---

## Argument Management Tools

### create_argument

Creates a new argument for the workflow.

**Parameters:**
- `name` (required): Name of the argument
- `direction` (required): Direction of the argument
  - Values: "In", "Out", "InOut"
- `argumentType` (optional): Type of argument (default: "String")
- `defaultValue` (optional): Default value

**Example:**
```json
{
  "name": "inputText",
  "direction": "In",
  "argumentType": "String",
  "defaultValue": "Default text"
}
```

### list_arguments

Lists all arguments defined in the workflow.

**Example:**
```json
{}
```

---

## Navigation Tools

### get_activity_tree

Gets the complete activity tree showing hierarchical structure.

**Parameters:**
- `includeDetails` (optional): Include detailed information (default: false)

**Example:**
```json
{
  "includeDetails": true
}
```

### find_activity

Finds activities matching specific criteria.

**Parameters:**
- `activityType` (optional): Type of activity to find (e.g., "Sequence", "WriteLine")
- `displayName` (optional): Display name to search for (partial match)
- `propertyName` (optional): Property name to search in
- `propertyValue` (optional): Property value to match

**Example:**
```json
{
  "activityType": "WriteLine",
  "displayName": "Log"
}
```

### get_parent_activity

Gets the parent activity of a specified activity.

**Parameters:**
- `activityId` (required): ID of the activity to get parent for

**Example:**
```json
{
  "activityId": "123"
}
```

### get_child_activities

Gets all direct child activities of a specified activity.

**Parameters:**
- `activityId` (required): ID of the activity to get children for

**Example:**
```json
{
  "activityId": "123"
}
```

---

## Workflow Tools

### add_activity

Adds a new activity to the current workflow.

**Parameters:**
- `activityType` (required): Type of activity
  - Control Flow: Sequence, If, While, DoWhile, ForEach, Switch, Parallel, Pick
  - Primitives: WriteLine, Assign, Delay, InvokeMethod
  - Error Handling: TryCatch, Throw, Rethrow
  - Flow: Flowchart, FlowDecision, FlowSwitch
- `displayName` (optional): Display name for the activity
- `parameters` (optional): Activity-specific parameters
- `children` (optional): Child activities for container activities

**Example:**
```json
{
  "activityType": "WriteLine",
  "displayName": "Log Message",
  "parameters": {
    "text": "Hello World"
  }
}
```

### update_activity

Updates parameters of an existing activity.

**Parameters:**
- `activityId` (optional): ID of activity to update
- `displayName` (optional): Display name of activity
- `parameters` (required): New parameter values

**Example:**
```json
{
  "activityId": "123",
  "parameters": {
    "Text": "Updated message"
  }
}
```

### remove_activity

Removes an activity from the workflow.

**Parameters:**
- `activityId` (optional): ID of activity to remove
- `displayName` (optional): Display name of activity

**Example:**
```json
{
  "activityId": "123"
}
```

### list_activities

Lists all available activity types with descriptions.

**Parameters:**
- `category` (optional): Filter by category
  - Values: ControlFlow, Primitives, ErrorHandling, Flow, Collection, All

**Example:**
```json
{
  "category": "ControlFlow"
}
```

### get_workflow_info

Gets information about the current workflow.

**Example:**
```json
{}
```

### get_validation_errors

Gets validation errors in the current workflow.

**Example:**
```json
{}
```

### save_workflow

Saves the current workflow to file.
---

## Debugging and Execution Tools

### set_breakpoint

Sets a breakpoint on a specific activity for debugging purposes.

**Parameters:**
- `activityId` (optional): ID of the activity to set breakpoint on
- `activityName` (optional): Display name of the activity (alternative to activityId)
- `enabled` (optional): Whether the breakpoint is enabled (default: true)

**Example:**
```json
{
  "activityId": "123",
  "enabled": true
}
```

### remove_breakpoint

Removes a breakpoint from a specific activity.

**Parameters:**
- `activityId` (optional): ID of the activity to remove breakpoint from
- `activityName` (optional): Display name of the activity (alternative to activityId)

**Example:**
```json
{
  "activityId": "123"
}
```

### list_breakpoints

Lists all breakpoints set in the current workflow.

**Example:**
```json
{}
```

### run_workflow

Executes the current workflow.

**Parameters:**
- `arguments` (optional): Input arguments for the workflow execution

**Example:**
```json
{
  "arguments": {
    "inputText": "Hello",
    "counter": 5
  }
}
```

### debug_workflow

Executes the workflow in debug mode, respecting breakpoints.

**Parameters:**
- `arguments` (optional): Input arguments for the workflow execution
- `stepMode` (optional): Enable step-by-step execution mode (default: false)

**Example:**
```json
{
  "arguments": {},
  "stepMode": true
}
```

### stop_execution

Stops the currently running workflow execution.

**Parameters:**
- `force` (optional): Force stop without cleanup (default: false)

**Example:**
```json
{
  "force": false
}
```

---

## Import and Dependency Tools

### add_namespace

Adds a namespace import (using directive) to the workflow.

**Parameters:**
- `namespace` (required): Namespace to import (e.g., 'System.Collections.Generic')

**Example:**
```json
{
  "namespace": "System.Collections.Generic"
}
```

### list_namespaces

Lists all namespace imports (using directives) in the workflow.

**Example:**
```json
{}
```

### add_assembly_reference

Adds an assembly reference to the workflow project.

**Parameters:**
- `assemblyName` (required): Name or path of the assembly to reference

**Example:**
```json
{
  "assemblyName": "System.Net.Http"
}
```

### list_assemblies

Lists all assembly references in the workflow project.

**Example:**
```json
{}
```

---

## Template Tools

### create_from_template

Creates a new workflow from a template file.

**Parameters:**
- `templateName` (optional): Name of the template to use
- `templatePath` (optional): Full path to template file (alternative to templateName)
- `targetPath` (required): Path where the new workflow will be created
- `workflowName` (required): Name for the new workflow

**Example:**
```json
{
  "templateName": "BasicSequence",
  "targetPath": "./workflows",
  "workflowName": "MyNewWorkflow"
}
```

### list_templates

Lists all available workflow templates.

**Parameters:**
- `templatesPath` (optional): Custom path to templates directory

**Example:**
```json
{}
```

### save_as_template

Saves the current workflow as a template for future use.

**Parameters:**
- `templateName` (required): Name for the template
- `description` (optional): Description of the template
- `overwrite` (optional): Overwrite if template already exists (default: false)

**Example:**
```json
{
  "templateName": "MyTemplate",
  "description": "A reusable workflow pattern",
  "overwrite": false
}
```

---

## Refactoring Tools

### extract_to_workflow

Extracts a group of activities into a separate workflow file and replaces them with an InvokeWorkflow activity.

**Parameters:**
- `activityIds` (required): Array of activity IDs to extract
- `newWorkflowName` (required): Name for the new extracted workflow
- `newWorkflowPath` (required): Path where the new workflow will be saved

**Example:**
```json
{
  "activityIds": ["123", "124", "125"],
  "newWorkflowName": "ExtractedLogic",
  "newWorkflowPath": "./workflows"
}
```

### inline_workflow

Inlines an InvokeWorkflow activity by replacing it with the contents of the invoked workflow.

**Parameters:**
- `activityId` (optional): ID of the InvokeWorkflow activity to inline
- `activityName` (optional): Display name of the InvokeWorkflow activity (alternative to activityId)

**Example:**
```json
{
  "activityId": "123"
}
```

### rename_activity

Renames an activity and updates all references to it throughout the workflow.

**Parameters:**
- `activityId` (optional): ID of the activity to rename
- `currentName` (optional): Current display name of the activity (alternative to activityId)
- `newName` (required): New display name for the activity

**Example:**
```json
{
  "currentName": "Sequence1",
  "newName": "MainLogicSequence"
}
```

---

## Analysis and Metrics Tools

### analyze_workflow

Analyzes the workflow for complexity, potential issues, code smells, and best practice violations.

**Parameters:**
- `includeComplexity` (optional): Include complexity metrics (default: true)
- `includeIssues` (optional): Include potential issues and warnings (default: true)
- `includeSuggestions` (optional): Include improvement suggestions (default: true)

**Example:**
```json
{
  "includeComplexity": true,
  "includeIssues": true,
  "includeSuggestions": true
}
```

**Response includes:**
- Complexity score and level
- Total activities and max nesting depth
- Decision points count
- Activity type statistics
- Potential issues and warnings
- Improvement suggestions

### get_workflow_statistics

Gets detailed statistics about the workflow including activity counts, nesting depth, variables, and arguments.

**Example:**
```json
{}
```

**Response includes:**
- Total activities count
- Maximum nesting depth
- Variables count
- Arguments count
- Activity type breakdown
- Control flow statistics

---

## Comment and Annotation Tools

### add_annotation

Adds a comment or annotation to a specific activity in the workflow.

**Parameters:**
- `activityId` (optional): ID of the activity to annotate
- `activityName` (optional): Display name of the activity (alternative to activityId)
- `text` (required): Annotation text/comment
- `annotationType` (optional): Type of annotation (default: "comment")
  - Values: comment, note, warning, todo

**Example:**
```json
{
  "activityId": "123",
  "text": "This activity handles user authentication",
  "annotationType": "note"
}
```

### list_annotations

Lists all annotations and comments in the current workflow.

**Parameters:**
- `filterType` (optional): Filter by annotation type (default: "all")
  - Values: comment, note, warning, todo, all

**Example:**
```json
{
  "filterType": "todo"
}
```


**Example:**
```json
{}
```

---

## File System Tools

### list_files

Lists files and directories.

**Parameters:**
- `path` (required): Directory path
- `recursive` (optional): List files recursively

**Example:**
```json
{
  "path": ".",
  "recursive": true
}
```

### read_file

Reads the contents of a file.

**Parameters:**
- `path` (required): File path

**Example:**
```json
{
  "path": "workflow.xaml"
}
```

### get_file_info

Gets information about a file.

**Parameters:**
- `path` (required): File path

**Example:**
```json
{
  "path": "workflow.xaml"
}
```

### search_files

Searches for patterns in files.

**Parameters:**
- `path` (required): Directory to search in
- `regex` (required): Regular expression pattern
- `file_pattern` (optional): Glob pattern to filter files

**Example:**
```json
{
  "path": ".",
  "regex": "WriteLine",
  "file_pattern": "*.xaml"
}
```

---

## Best Practices

1. **Always check workflow state** before making changes
2. **Use IDs when possible** for precise activity targeting
3. **Validate changes** with `get_validation_errors` after modifications
4. **Save frequently** using `save_workflow`
5. **Use navigation tools** to understand workflow structure before editing
6. **Create variables in appropriate scope** for better organization
7. **Use descriptive names** for variables and arguments

## Error Handling

All tools return a standardized response:

**Success:**
```json
{
  "success": true,
  "content": { ... },
  "error": null
}
```

**Error:**
```json
{
  "success": false,
  "content": null,
  "error": "Error message"
}
```

## Common Workflows

### Creating a Complete Sequence

1. Use `add_activity` to create a Sequence
2. Use `create_variable` to add necessary variables
3. Use `add_activity` to add child activities
4. Use `update_activity` to configure parameters
5. Use `save_workflow` to persist changes

### Modifying Existing Activities

1. Use `find_activity` to locate the activity
2. Use `get_activity_details` to see current configuration
3. Use `set_activity_property` or `update_activity` to make changes
4. Use `get_validation_errors` to verify correctness

### Organizing Workflow Structure

1. Use `get_activity_tree` to see full structure
2. Use `move_activity` to reorganize
3. Use `copy_activity` to duplicate common patterns
4. Use `get_child_activities` to verify container contents