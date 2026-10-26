# Workflow Designer

## Overview

The workflow designer is the main working surface of the studio. It is built on
`System.Activities.Presentation.WorkflowDesigner` (Windows Workflow Foundation)
and lets you visually assemble RPA processes from activities.

## Key features

- Create, open and save workflow files (`.xaml`).
- Drag and drop activities from the toolbox onto the designer surface.
- Edit the properties of the selected activity in the properties panel.
- Edit tracking: the "Save" and "Save All" buttons enable only after a workflow
  is modified (see `WorkflowEditTracking`).
- Roslyn-powered expression editor with IntelliSense.

## Lifecycle

1. Create or open a project.
2. Create a new workflow or open an existing one.
3. Add activities from the available libraries.
4. Save your changes.

## See also

- `en/activities.md` — activity reference.
- `en/project-management.md` — project management.
