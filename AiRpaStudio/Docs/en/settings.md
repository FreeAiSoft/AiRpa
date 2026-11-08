# Settings

## Overview

Studio settings are stored through `Properties.Settings` and persisted with the
`Save()` method. Localization (RU/EN) is selected via `Properties.Settings.Language`.

## Main settings

- Interface language (RU/EN).
- AI provider settings for the chat (API key, model, base URL).
- Agent permissions (`AgentPermissions`): read files, edit, run commands, web
  access, sub-agents.
- Proxy settings (WebProxy).
- Speech-to-text (STT) options.

## Storage

- Application settings — `Properties.Settings`.
- Chat settings — JSON in `%AppData%\AiRpaStudio\Chat\chat_settings.json`.

## See also

- `en/mcp-server.md` — MCP server settings.
