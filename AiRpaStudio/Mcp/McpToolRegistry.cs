using System;
using System.Collections.Generic;
using System.Linq;
using RPA.Core.Generation;
using RPA.Mcp.Tools;

namespace RPA.Mcp
{
    /// <summary>
    /// Реестр MCP-инструментов студии (этап 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>).
    /// <para>
    /// До этого этапа <c>RpaMcpServer._tools</c> был пустой коллекцией: каркас <see cref="IMcpTool"/>
    /// существовал, но зарегистрировать инструмент было негде. Реестр — единственное место регистрации:
    /// «инструмент есть, но недоступен по имени» больше не возможен.
    /// </para>
    /// </summary>
    internal sealed class McpToolRegistry
    {
        private readonly Dictionary<string, IMcpTool> _tools =
            new Dictionary<string, IMcpTool>(StringComparer.OrdinalIgnoreCase);

        internal int Count => _tools.Count;

        internal IReadOnlyList<string> Names => _tools.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        internal IReadOnlyList<IMcpTool> Tools => _tools.Values.ToArray();

        /// <summary>Регистрирует инструмент; повторная регистрация того же имени заменяет реализацию.</summary>
        internal void Register(IMcpTool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            McpTool schema = tool.GetSchema();
            if (string.IsNullOrWhiteSpace(schema?.Name))
            {
                throw new ArgumentException("У инструмента нет имени в схеме", nameof(tool));
            }

            _tools[schema.Name.Trim()] = tool;
        }

        internal bool TryGet(string name, out IMcpTool tool)
        {
            tool = null;
            return !string.IsNullOrWhiteSpace(name) && _tools.TryGetValue(name.Trim(), out tool);
        }

        internal IReadOnlyList<McpTool> Schemas()
        {
            return _tools.Values
                .Select(tool => tool.GetSchema())
                .Where(schema => schema != null)
                .OrderBy(schema => schema.Name, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Инструменты студии: палитра <c>xaml.*</c> (этап 4), Code-First (этап 5) и JSON AST (этап 6). Сервисы
        /// создаются при первом вызове, поэтому регистрация на старте не зависит от готовности UI-потока.
        /// </summary>
        internal static McpToolRegistry CreateDefault(Action<string> log = null)
        {
            IPaletteWorkflowService palette = null;
            IPaletteWorkflowService PaletteService()
            {
                return palette ?? (palette = PaletteWorkflowService.ForStudio(log));
            }

            var registry = new McpToolRegistry();
            registry.Register(new CreateWorkflowTool(PaletteService));
            registry.Register(new AddActivityTool(PaletteService));
            registry.Register(new SetArgumentTool(PaletteService));
            registry.Register(new GetWorkflowTool(PaletteService));

            // Code-First (этап 5 плана): тот же конвейер, но вход — код агента, а не шаги палитры.
            ICodeFirstWorkflowService codeFirst = null;
            ICodeFirstWorkflowService CodeFirstService()
            {
                return codeFirst ?? (codeFirst = CodeFirstWorkflowService.ForStudio(log));
            }

            registry.Register(new CreateWorkflowFromCodeTool(CodeFirstService));

            // JSON AST (этап 6 плана): детерминированная сборка по строгой схеме, тот же конвейер.
            IJsonWorkflowService jsonAst = null;
            IJsonWorkflowService JsonAstService()
            {
                return jsonAst ?? (jsonAst = JsonWorkflowService.ForStudio(log));
            }

            registry.Register(new CreateWorkflowFromJsonTool(JsonAstService));


            // Контракт для агента (этап 7 плана): правила генерации, матрица выбора инструмента,
            // few-shot примеры и самокоррекция. Ничего не собирает — отдаёт то, что проверено тестами.
            registry.Register(new DescribeWorkflowContractTool());

            return registry;
        }
    }
}
