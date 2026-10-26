# MCP Server

## Overview

The studio ships an MCP server (`RPA.Mcp`) that exposes tools to external clients
over the Model Context Protocol.

## Transport modes

- `stdio` — over standard input/output.
- `http` — over an HTTP endpoint.

## Startup

The MCP server is started through the `--mcp-server` command-line switch in
`App.xaml.cs`. The integration is described in `Models\McpIntegration.cs`.

## Important

The new built-in studio chat is not connected to the MCP server: the MCP server
is a standalone integration, while the chat uses its own agent tool set.

## See also

- `en/settings.md` — settings.
