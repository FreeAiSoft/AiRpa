# Реестр миграции `Agent.Rpa.*` → `RPA.Shared.*`

## Пространства имён

| Было (форк `Agent.Core`, сборка `Agent.Rpa`) | Стало (`RPA.Shared`) |
|---|---|
| `Agent.Rpa.AgentTools` | `RPA.Shared.AgentTools` |
| `Agent.Rpa.Contracts` | `RPA.Shared.Contracts` |
| `Agent.Rpa.Models` | `RPA.Shared.Models` |
| `Agent.Rpa.Services` | `RPA.Shared.Services` |
| `Agent.Rpa.Services.Documents` | `RPA.Shared.Services.Documents` |

## Типы

| Тип(ы) | Группа | Новое место |
|---|---|---|
| `AgentPromptBuilder`, `AgentRunner`, `AgentTool`, `AgentToolRegistry`, `IAgentTool`, `ToolSchemaConverter` | C — доработанные (WF-фильтр `WorkflowWriteWay`) | `RPA.Shared.AgentTools` (оставлены локальными, см. примечание) |
| `PromptDefaults`, `PromptPaths` | связаны с `AgentPromptBuilder` | `RPA.Shared.Services` |
| `StudioTheme` (+`StudioThemes`), `WorkflowWriteWay` | B — чисто студийные | `RPA.Shared.Models` |
| `DocumentationIndex`(+`DocumentationEntry`/`DocumentationSearchResult`), `DocumentationViewer`, `MarkdownContent`, `MsBuildLocator`(+`MsBuildLocation`/`MsBuildToolchain`/`IMsBuildEnvironment`), `ThemeColorMath`, `WebProxyBuilder` | B — чисто студийные | `RPA.Shared.Services` |
| `DocumentText`(+`DocumentTextStatus`/`DocumentTextReadOptions`/`DocumentTextResult`/`DocumentTextReaders`/`IDocumentTextReader`), `OpenXmlExcelReader`, `OpenXmlWordReader`, `PdfTextReader`, `PlainTextReader` | B — чисто студийные | `RPA.Shared.Services.Documents` |
| `IBuildToolchainAware` | B — чисто студийные | `RPA.Shared.Contracts` |

## Примечание по AgentTools (группа C)

Форковый `AgentTools` разошёлся с пакетным `Agent.Core.AgentTools` в обе стороны:
форк — более старая база + WF-фильтр `WorkflowWriteWay` + `spec_*`/docs-подсказки в промпте +
`AgentToolCall.FullResult`; пакет — новее (approval-гейт, детектор повторов, resume-снапшоты,
`AgentToolRegistry.Disable`/`TryGetExecutableTool`).

Поэтому «тонкая обёртка WF-фильтра поверх пакетного AgentTools» изменила бы поведение агентского
цикла (появились бы approval-гейты и т.п.), что нарушает правило «функциональность не должна меняться».
Принятое решение: весь форковый `AgentTools` перенесён в `RPA.Shared.AgentTools` как package-backed
код (использует пакетные `Agent.Core.Context/Contracts/Models/Services`), а не переписан на
пакетный `Agent.Core.AgentTools`. Возможный отдельный шаг — переезд на пакетный `Agent.Core.AgentTools`
с переносом approval-гейтов и т.п. (уже за рамками этого плана).
