using System;
using System.Collections.Generic;
using System.Linq;
using Agent.Core.Models;
using RPA.Shared.Models;
using RPA.Core.Generation;
using RPA.Chat.Services.Workflow;
using RPA.Mcp.Tools;

namespace RPA.Mcp
{
    /// <summary>
    /// Реестр MCP-инструментов студии (этап 4 конвейера генерации XAML).
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

        private readonly Dictionary<string, WorkflowWriteWay> _ways =
            new Dictionary<string, WorkflowWriteWay>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, bool> _mutates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        internal int Count => _tools.Count;

        internal IReadOnlyList<string> Names => _tools.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        internal IReadOnlyList<IMcpTool> Tools => _tools.Values.ToArray();

        /// <summary>Регистрирует инструмент; повторная регистрация того же имени заменяет реализацию.</summary>
        internal void Register(IMcpTool tool, WorkflowWriteWay way = WorkflowWriteWay.None, bool mutates = false)
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

            string name = schema.Name.Trim();

            // Страж Фазы 12: мутирующий MCP-инструмент обязан нести метку способа (Palette/JsonAst/CodeFirst).
            // Иначе галочка способа его не спрячет, а мост пометит read-only — и правка протечёт в Plan.
            if (mutates && way == WorkflowWriteWay.None)
            {
                throw new ArgumentException(
                    $"Мутирующий MCP-инструмент «{name}» должен быть зарегистрирован с меткой способа " +
                    "(WorkflowWriteWay.Palette/JsonAst/CodeFirst): без неё галочка способа не спрячет его.",
                    nameof(way));
            }
            _tools[name] = tool;
            _ways[name] = way;
            _mutates[name] = mutates;
        }

        internal bool TryGet(string name, out IMcpTool tool)
        {
            tool = null;
            return !string.IsNullOrWhiteSpace(name) && _tools.TryGetValue(name.Trim(), out tool);
        }

        /// <summary>Способ написания WF по имени инструмента; не-WF/read-only инструменты — <c>None</c>.</summary>
        internal WorkflowWriteWay WayOf(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _ways.TryGetValue(name.Trim(), out WorkflowWriteWay way)
                ? way
                : WorkflowWriteWay.None;
        }

        /// <summary>Мутирует ли инструмент (пишет файлы/модель/сборку). Единственный источник для моста.</summary>
        internal bool MutatesOf(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _mutates.TryGetValue(name.Trim(), out bool mutates) && mutates;
        }

        /// <summary>Возвращает инструмент, если он разрешён маской <paramref name="allowedWays"/>.</summary>
        internal bool TryGet(string name, WorkflowWriteWay allowedWays, out IMcpTool tool)
        {
            if (!TryGet(name, out tool))
            {
                return false;
            }

            return IsWayAllowed(name, allowedWays);
        }

        private bool IsWayAllowed(string name, WorkflowWriteWay allowedWays)
        {
            WorkflowWriteWay way = WayOf(name);
            return way == WorkflowWriteWay.None || (allowedWays & way) != 0;
        }

        internal IReadOnlyList<McpTool> Schemas()
        {
            return Schemas(WorkflowWriteWay.All);
        }

        /// <summary>Схемы инструментов, разрешённых маской <paramref name="allowedWays"/>.</summary>
        internal IReadOnlyList<McpTool> Schemas(WorkflowWriteWay allowedWays)
        {
            return _tools.Values
                .Where(tool => IsWayAllowed(tool.GetSchema()?.Name, allowedWays))
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
            registry.Register(new CreateWorkflowTool(PaletteService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new AddActivityTool(PaletteService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new SetArgumentTool(PaletteService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new GetWorkflowTool(PaletteService));

            // Правки модели конструктора, перенесённые из чата (план «3 галочки», Фаза 6): тот же
            // WorkflowEditRunner/IWorkflowEditingService, что использовали activity_delete/move/wrap/undo/redo
            // и variable_add/variable_remove. Сервис создаётся по требованию (StudioWorkflowEditingService.Default).
            IWorkflowEditingService editing = null;
            IWorkflowEditingService EditingService()
            {
                return editing ?? (editing = RPA.Core.Workflow.StudioWorkflowEditingService.Default);
            }

            registry.Register(new DeleteActivityTool(EditingService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new MoveActivityTool(EditingService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new WrapActivityTool(EditingService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new UndoTool(EditingService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new RedoTool(EditingService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new AddVariableTool(EditingService), WorkflowWriteWay.Palette, mutates: true);
            registry.Register(new RemoveVariableTool(EditingService), WorkflowWriteWay.Palette, mutates: true);

            // Code-First (этап 5 плана): тот же конвейер, но вход — код агента, а не шаги палитры.
            ICodeFirstWorkflowService codeFirst = null;
            ICodeFirstWorkflowService CodeFirstService()
            {
                return codeFirst ?? (codeFirst = CodeFirstWorkflowService.ForStudio(log));
            }

            registry.Register(new CreateWorkflowFromCodeTool(CodeFirstService), WorkflowWriteWay.CodeFirst, mutates: true);

            // JSON AST (этап 6 плана): детерминированная сборка по строгой схеме, тот же конвейер.
            IJsonWorkflowService jsonAst = null;
            IJsonWorkflowService JsonAstService()
            {
                return jsonAst ?? (jsonAst = JsonWorkflowService.ForStudio(log));
            }

            registry.Register(new CreateWorkflowFromJsonTool(JsonAstService), WorkflowWriteWay.JsonAst, mutates: true);


            // Контракт для агента (этап 7 плана): правила генерации, матрица выбора инструмента,
            // few-shot примеры и самокоррекция. Ничего не собирает — отдаёт то, что проверено тестами.
            registry.Register(new DescribeWorkflowContractTool());

            return registry;
        }
    }
}
