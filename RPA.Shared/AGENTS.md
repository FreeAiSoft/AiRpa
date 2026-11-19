# RPA.Shared — общий код студии (AI-агент + RPA)

- Сборка: `netstandard2.0`, SDK-style. Пакет-база — `AgentToolkit` (пространства `Agent.Core.*`).
- Это бывший локальный форк `Agent.Core` (сборка `Agent.Rpa`), переименованный в `RPA.Shared`:
  пространства имён `Agent.Rpa.*` → `RPA.Shared.*`.
- Состав:
  - `AgentTools\` — студийная версия агентского цикла (`AgentPromptBuilder`/`AgentRunner`/`AgentTool`/`AgentToolRegistry`/`IAgentTool`/`ToolSchemaConverter`) с WF-фильтром `WorkflowWriteWay`.
  - `Models\` — `StudioTheme`, `WorkflowWriteWay`.
  - `Services\` — `DocumentationIndex`/`DocumentationViewer`, `MsBuildLocator`, `ThemeColorMath`, `WebProxyBuilder`, `PromptDefaults`, `PromptPaths`, `MarkdownContent`.
  - `Services\Documents\` — читатели документов ТЗ (`docx`/`xlsx`/`pdf`/текст).
  - `Contracts\` — `IBuildToolchainAware`.
- Потребители: `RPA.Chat`, `AiRpaStudio`, `AiRpaStudio.Tests`, `Rpa.RobotPackaging`.
