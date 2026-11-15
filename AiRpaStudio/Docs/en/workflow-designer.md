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

## Unsaved buffers of closed documents

Closing a modified tab (code/XAML editor or workflow designer) does not lose its
buffer: the buffer is stashed in the unsaved-buffer collection.

- **Closing a tab** — the buffer of a modified document is stashed (serialized XAML
  for a designer, text plus the dirty flag for a code editor).
- **Save All** — writes both open dirty documents and stashed buffers to disk, then
  removes the written entries from the collection.
- **Reopening a file** — the buffer and state are loaded from the collection into the
  tab and the entry is removed (the open tab now owns the buffer); a later "Save"
  persists it.
- **Deleting a file** from the project tree discards the buffer (does not resurrect
  the deleted file).
- **Closing the studio / switching project** — the "Save / Don't Save / Cancel" dialog
  is shown; "Don't Save" clears the unsaved-buffer collection.

## See also

- `en/activities.md` — activity reference.
- `en/project-management.md` — project management.
