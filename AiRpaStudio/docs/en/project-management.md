# Project Management

## Overview

Project management in the studio is handled by `ProjectManager`. A project groups
workflow files, activities and build settings.

## Features

- Create a new project (creation wizard).
- Open an existing project/solution (`.sln`, `.csproj`).
- Manage project files: add, rename, delete.
- Build the project with MSBuild.
- View compiler errors and warnings.

## Structure

A studio project is a regular .NET Framework 4.8 project (WPF/library) that
contains workflow files (`.xaml`) and activity classes.

## See also

- `en/settings.md` — studio settings.
- `en/nuget-manager.md` — NuGet package management.
