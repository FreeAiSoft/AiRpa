# Activities

## Overview

An activity is the basic building block of a workflow. Each activity describes a
single action: working with the browser, files, the operating system, flow
control, and so on.

## Categories

- `Activities` — base and general activities.
- `Activities.Browser` — browser automation (launch, navigate, click, type, JS).
- `Activities.Files` — file and folder operations.
- `Activities.OS` — operating system interaction.
- `Activities.Flow` — flow control (conditions, loops).
- `Activities.Debug` — debugging activities.
- `Rpa.Activities.Primitives` — primitive activities.

## Creating custom activities

Activities are created from the `ActivityLibrary` template (see `VSTemplates`).
Each activity is a class deriving from a base activity class, with an XAML
designer for visual editing.

## See also

- `en/workflow-designer.md` — the workflow designer.
