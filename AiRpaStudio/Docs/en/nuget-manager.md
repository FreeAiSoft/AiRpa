# NuGet Package Manager

## Overview

The NuGet package manager (`RPA.NuGet`) lets you search, browse, install, update
and remove NuGet packages for the current project. Its layout and feature set
follow the Visual Studio 2026 package manager, but the studio panel is
**not rounded**: corners are square and zones are separated by 1px lines.

## Location

The manager opens as a **document-pane tab** (the "NuGet" menu item, or the
project context menu → "Add dependency"). The tab can be dragged into a side
dock; there the panel switches to a narrow mode where package details replace
the list and a back button returns to the list.

## Features

- Three tabs: **Browse**, **Installed** and **Updates**.
- Search with `Ctrl+L`, clear with `Esc`, "Load more" paging and a "Cancel"
  button for long requests.
- The **Include prerelease** switch affects both search and update checks.
- Package source selection (`Package source`) read from `NuGet.Config`
  (solution, user, global); the gear icon opens the configuration file. When
  source mapping is off, the panel shows a "Package source mapping is off."
  notice with a link.
- Package row: icon, popularity star, id, authors, description, download count
  and version.
- Details pane: version picker (latest stable or any listed version), a large
  **Install / Update / Downgrade / Uninstall** button, an **Options** block
  (prerelease, run `msbuild -t:restore` after install) and the **README** /
  **Package Details** tabs (description, authors, license, publish date,
  downloads, project URL, report abuse, tags).
- A package README is read from the `.nupkg` downloaded **from the selected
  source** (private feeds included) and cached in memory; if it cannot be
  fetched, the reason is shown in the panel status line.
- The **Options** chevron is a backgroundless button with a Material icon:
  hovering does not highlight it, only the icon shows the expanded state.
- Bulk update of every outdated package on the Updates tab.
- Installing updates `packages.config` or `<PackageReference>` in the `.csproj`;
  legacy projects also get the package extracted into `packages\`.

- Package row buttons (shown on hover): **install/update** is an arrow,
  **uninstall** is a cross. Their column is narrow (37px) and the buttons are
  16×16, so revealing them never shifts the id, version or description.
- The package logo is also shown in the details pane header; when a package has
  no icon, a monogram (the first letter of the id) is used, as in the list row.

## See also

- `en/project-management.md` — project management.
- `NuGet_Panel_Redesign_Plan.md` — design plan and visual rules of the panel.
