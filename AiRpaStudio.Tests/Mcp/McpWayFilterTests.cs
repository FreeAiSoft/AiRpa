using System.Linq;
using Agent.Core.Models;
using RPA.Shared.Models;
using NUnit.Framework;
using RPA.Mcp;
using RPA.Mcp.Tools;

namespace AiRpaStudio.Tests.Mcp
{
    /// <summary>
    /// Фаза 11 плана «3 галочки»: MCP-сервер фильтрует способы написания WF для внешних клиентов.
    /// Read-only инструменты (<c>xaml.get_workflow</c>, <c>xaml.describe_contract</c>) не зависят от
    /// галочек и остаются видимыми всегда.
    /// </summary>
    [TestFixture]
    public class McpWayFilterTests
    {
        [Test]
        public void Registry_TryGet_FiltersByWay()
        {
            McpToolRegistry registry = McpToolRegistry.CreateDefault();

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryGet(CreateWorkflowTool.ToolName, WorkflowWriteWay.CodeFirst, out _), Is.False,
                    "палитра не видна, когда включён только code-first");
                Assert.That(registry.TryGet(CreateWorkflowTool.ToolName, WorkflowWriteWay.Palette, out _), Is.True);
                Assert.That(registry.TryGet(CreateWorkflowFromCodeTool.ToolName, WorkflowWriteWay.CodeFirst, out _), Is.True);
                Assert.That(registry.TryGet(GetWorkflowTool.ToolName, WorkflowWriteWay.None, out _), Is.True,
                    "read-only поддержка не зависит от галочек");
            });
        }

        [Test]
        public void ListTools_RespectsAllowedWays()
        {
            var server = new RpaMcpServer(McpToolRegistry.CreateDefault(), wayProvider: () => WorkflowWriteWay.CodeFirst);

            string[] names = server.ListTools().GetAwaiter().GetResult().Select(tool => tool.Name).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(names, Does.Contain(CreateWorkflowFromCodeTool.ToolName));
                Assert.That(names, Does.Contain(GetWorkflowTool.ToolName), "read-only поддержка видна всегда");
                Assert.That(names, Does.Not.Contain(CreateWorkflowTool.ToolName), "палитра скрыта");
                Assert.That(names, Does.Not.Contain(CreateWorkflowFromJsonTool.ToolName), "JSON AST скрыт");
            });
        }

        [Test]
        public void CallTool_BlockedWay_ReturnsDisabledError()
        {
            var server = new RpaMcpServer(McpToolRegistry.CreateDefault(), wayProvider: () => WorkflowWriteWay.None);

            McpToolResult result = server.CallTool(CreateWorkflowTool.ToolName, null).GetAwaiter().GetResult();

            Assert.Multiple(() =>
            {
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Does.Contain("palette (designer)"));
                Assert.That(result.Error, Does.Contain("turned off"));
            });
        }

        [Test]
        public void CallTool_AllowedReadOnly_IsNotBlockedByWays()
        {
            var server = new RpaMcpServer(McpToolRegistry.CreateDefault(), wayProvider: () => WorkflowWriteWay.None);

            McpToolResult result = server.CallTool(GetWorkflowTool.ToolName, null).GetAwaiter().GetResult();

            Assert.That(result.Error, Does.Not.Contain("turned off"),
                "read-only инструмент не должен блокироваться выключенными галочками");
        }
    }
}
