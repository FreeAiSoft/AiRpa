using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.Models;
using RPA.Shared.Models;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPA.Chat.Services.Workflow;
using RPA.Core.Generation;
using WorkflowEditResult = RPA.Chat.Services.Workflow.WorkflowEditResult;
using RPA.Mcp;
using RPA.Mcp.Tools;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// Фаза 6 плана «3 галочки»: палитра правки модели перенесена из чата в MCP
    /// (<c>xaml.delete_activity</c>, <c>xaml.move_activity</c>, <c>xaml.wrap_activity</c>,
    /// <c>xaml.undo</c>, <c>xaml.redo</c>, <c>xaml.add_variable</c>, <c>xaml.remove_variable</c>).
    /// <para>
    /// Инструменты оборачивают тот же <see cref="WorkflowEditRunner"/>, что использовали удалённые
    /// <c>activity_*</c>/<c>argument_set</c>/<c>variable_*</c>, поэтому здесь проверяется только перенос:
    /// разбор аргументов, делегирование в мост правок и метка способа <see cref="WorkflowWriteWay.Palette"/>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowMcpEditToolTests
    {
        [Test]
        public void Registry_RegistersAllPaletteEditTools()
        {
            McpToolRegistry registry = McpToolRegistry.CreateDefault();

            string[] names = registry.Schemas().Select(schema => schema.Name).ToArray();

            Assert.That(names, Is.SupersetOf(new[]
            {
                DeleteActivityTool.ToolName,
                MoveActivityTool.ToolName,
                WrapActivityTool.ToolName,
                UndoTool.ToolName,
                RedoTool.ToolName,
                AddVariableTool.ToolName,
                RemoveVariableTool.ToolName
            }));
        }

        [Test]
        public void Bridge_MarksPaletteEditToolsAsPaletteAndMutating()
        {
            var bridge = new StudioWorkflowMcpBridge(new RpaMcpServer(McpToolRegistry.CreateDefault(), wayProvider: () => WorkflowWriteWay.All));

            WorkflowMcpToolInfo[] tools = bridge.ListTools()
                .Where(tool => new[]
                {
                    DeleteActivityTool.ToolName,
                    MoveActivityTool.ToolName,
                    WrapActivityTool.ToolName,
                    UndoTool.ToolName,
                    RedoTool.ToolName,
                    AddVariableTool.ToolName,
                    RemoveVariableTool.ToolName
                }.Contains(tool.Name))
                .ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(tools.Length, Is.EqualTo(7), "все 7 инструментов правки должны быть в мосте");
                Assert.That(tools.Select(tool => tool.Way), Is.All.EqualTo(WorkflowWriteWay.Palette),
                    "инструменты правки модели — это палитра (галочка №1)");
                Assert.That(tools.Select(tool => tool.Mutates), Is.All.True,
                    "все 7 инструментов правки мутируют");
            });
        }

        [Test]
        public void Bridge_MappingMatchesRegistryForAllTools()
        {
            McpToolRegistry registry = McpToolRegistry.CreateDefault();
            var bridge = new StudioWorkflowMcpBridge(new RpaMcpServer(registry, wayProvider: () => WorkflowWriteWay.All));

            Dictionary<string, WorkflowMcpToolInfo> byName = bridge.ListTools().ToDictionary(tool => tool.Name);

            foreach (McpTool schema in registry.Schemas())
            {
                Assert.That(byName[schema.Name].Way, Is.EqualTo(registry.WayOf(schema.Name)),
                    "мост и реестр разошлись по способу для " + schema.Name);
                Assert.That(byName[schema.Name].Mutates, Is.EqualTo(registry.MutatesOf(schema.Name)),
                    "мост и реестр разошлись по мутации для " + schema.Name);
            }
        }

        [Test]
        public void Register_MutatingToolWithoutWay_Throws()
        {
            var registry = new McpToolRegistry();
            var tool = new FakeMcpTool("xaml.fake_mutating");

            var exception = Assert.Throws<ArgumentException>(() =>
                registry.Register(tool, WorkflowWriteWay.None, mutates: true));

            Assert.That(exception.Message, Does.Contain("меткой способа"),
                "мутирующий MCP-инструмент без метки способа должен быть пойман при регистрации");
        }

        [Test]
        public void DeleteActivity_MissingElementId_ReturnsParameterError()
        {
            var service = new FakeEditingService();

            McpToolResult result = Execute(new DeleteActivityTool(() => service, OpenOk), JObject.Parse("{\"path\":\"Main.xaml\"}"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.ValidationError?.ParameterName, Is.EqualTo("elementId"));
                Assert.That(service.Began, Is.False, "сервис не вызывается при неверных аргументах");
            });
        }

        [Test]
        public void DeleteActivity_DelegatesToEditingService()
        {
            var service = new FakeEditingService();

            McpToolResult result = Execute(
                new DeleteActivityTool(() => service, OpenOk),
                JObject.Parse("{\"path\":\"Main.xaml\",\"elementIds\":[\"root/Activities[0]\"],\"validate\":\"off\"}"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(service.Began, Is.True);
                Assert.That(service.LastOperation, Is.EqualTo("delete"));
            });
        }

        [Test]
        public void AddVariable_DelegatesToEditingService()
        {
            var service = new FakeEditingService();

            McpToolResult result = Execute(
                new AddVariableTool(() => service, OpenOk),
                JObject.Parse("{\"path\":\"Main.xaml\",\"name\":\"counter\",\"typeName\":\"Int32\",\"validate\":\"off\"}"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(service.LastOperation, Is.EqualTo("variable_add"));
            });
        }

        [Test]
        public void Undo_DelegatesToEditingService()
        {
            var service = new FakeEditingService();

            McpToolResult result = Execute(
                new UndoTool(() => service),
                JObject.Parse("{\"path\":\"Main.xaml\"}"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(service.LastOperation, Is.EqualTo("undo"));
            });
        }

        [Test]
        public void Redo_DelegatesToEditingService()
        {
            var service = new FakeEditingService();

            McpToolResult result = Execute(
                new RedoTool(() => service),
                JObject.Parse("{\"path\":\"Main.xaml\"}"));

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.True, result.Error);
                Assert.That(service.LastOperation, Is.EqualTo("redo"));
            });
        }

        private static McpToolResult Execute(IMcpTool tool, JObject arguments)
        {
            return tool.Execute(arguments).GetAwaiter().GetResult();
        }

        private static DesignerOpenResult OpenOk(string path)
        {
            return DesignerOpenResult.Success(path, null, false, false, true);
        }

        private sealed class FakeMcpTool : IMcpTool
        {
            private readonly string _name;

            internal FakeMcpTool(string name)
            {
                _name = name;
            }

            public McpTool GetSchema()
            {
                return new McpTool
                {
                    Name = _name,
                    Description = "fake",
                    InputSchema = new { type = "object" }
                };
            }

            public Task<McpToolResult> Execute(object arguments)
            {
                return Task.FromResult(new McpToolResult { Success = true });
            }
        }

        private sealed class FakeEditingService : IWorkflowEditingService
        {
            internal bool Began { get; private set; }

            internal string LastOperation { get; set; }

            public IWorkflowEditSession BeginEdit(string path, string label = null)
            {
                Began = true;
                return new FakeSession(this);
            }

            public WorkflowEditResult Undo(string path)
            {
                LastOperation = "undo";
                return WorkflowEditResult.Success("undo", "отменено").WithState(false);
            }

            public WorkflowEditResult Redo(string path)
            {
                LastOperation = "redo";
                return WorkflowEditResult.Success("redo", "возвращено").WithState(false);
            }

            public WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true)
            {
                return null;
            }

            public bool IsOpenInDesigner(string path)
            {
                return true;
            }
        }

        private sealed class FakeSession : IWorkflowEditSession
        {
            private readonly FakeEditingService _owner;

            internal FakeSession(FakeEditingService owner)
            {
                _owner = owner;
            }

            public string FilePath => "Main.xaml";

            public string Label => "xaml";

            public bool IsActive => true;

            public WorkflowEditResult Insert(WorkflowInsertRequest request) => Record("insert");

            public WorkflowEditResult Delete(string elementId) => Record("delete");

            public WorkflowEditResult Move(WorkflowMoveRequest request) => Record("move");

            public WorkflowEditResult Wrap(WorkflowWrapRequest request) => Record("wrap");

            public WorkflowEditResult SetArgument(WorkflowArgumentSetRequest request) => Record("argument_set");

            public WorkflowEditResult AddVariable(WorkflowVariableAddRequest request) => Record("variable_add");

            public WorkflowEditResult RemoveVariable(WorkflowVariableRemoveRequest request) => Record("variable_remove");

            public WorkflowEditResult Complete()
            {
                return WorkflowEditResult.Success(_owner.LastOperation, "готово")
                    .WithState(false)
                    .WithApplied(1, new[] { "root/Activities[0]" });
            }

            public WorkflowEditResult Revert()
            {
                return WorkflowEditResult.Success("revert", "отменено").WithState(false);
            }

            public WorkflowOutline Outline(int depth = 0, bool includeArguments = true)
            {
                return null;
            }

            public void Dispose()
            {
            }

            private WorkflowEditResult Record(string operation)
            {
                _owner.LastOperation = operation;
                return WorkflowEditResult.Success(operation, operation).WithState(true);
            }
        }
    }
}
