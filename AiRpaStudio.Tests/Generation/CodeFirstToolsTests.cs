using System;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;
using RPA.Mcp;
using RPA.Mcp.Tools;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 5 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: MCP-инструмент
/// <c>xaml.create_workflow_from_code</c>.
/// <para>
/// Проверяется контракт с агентом: инструмент зарегистрирован, схема требует <c>path</c> и <c>code</c>,
/// аргументы разбираются в запрос, а ответ всегда в формате
/// <c>{ok, entryPoint, diagnostics[], errors[], hints[]}</c> — с машиночитаемой диагностикой компиляции.
/// </para>
/// </summary>
[TestFixture]
public class CodeFirstToolsTests
{
    [Test]
    public void Registry_RegistersCodeFirstTool()
    {
        McpToolRegistry registry = McpToolRegistry.CreateDefault();

        Assert.That(registry.TryGet(CreateWorkflowFromCodeTool.ToolName, out IMcpTool tool), Is.True,
            "инструмент Code-First обязан быть зарегистрирован на старте MCP-сервера");
        Assert.That(tool, Is.TypeOf<CreateWorkflowFromCodeTool>());
    }

    [Test]
    public void CreateFromCode_WithoutPath_ReturnsParameterError()
    {
        var service = new FakeCodeFirstService();

        McpToolResult result = Execute(new CreateWorkflowFromCodeTool(() => service), new JObject());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError?.ParameterName, Is.EqualTo("path"));
            Assert.That(service.LastRequest, Is.Null, "сервис не вызывается при неверных аргументах");
        });
    }

    [Test]
    public void CreateFromCode_WithoutCode_ReturnsParameterError()
    {
        var service = new FakeCodeFirstService();
        var arguments = JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"" }");

        McpToolResult result = Execute(new CreateWorkflowFromCodeTool(() => service), arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError?.ParameterName, Is.EqualTo("code"));
            Assert.That(result.Error, Does.Contain("xaml.code"));
            Assert.That(service.LastRequest, Is.Null);
        });
    }

    [Test]
    public void CreateFromCode_ParsesArgumentsIntoRequest()
    {
        var service = new FakeCodeFirstService();
        var arguments = JObject.Parse(@"{
            ""path"": ""C:\\wf\\Main.xaml"",
            ""code"": ""public static class Workflow { }"",
            ""project"": ""RPA-1"",
            ""namespace"": ""RPA_1"",
            ""className"": ""Main""
        }");

        Execute(new CreateWorkflowFromCodeTool(() => service), arguments);

        CodeFirstWorkflowRequest request = service.LastRequest;

        Assert.That(request, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(request.FilePath, Is.EqualTo(@"C:\wf\Main.xaml"));
            Assert.That(request.Code, Does.Contain("public static class Workflow"));
            Assert.That(request.ProjectName, Is.EqualTo("RPA-1"));
            Assert.That(request.NamespaceName, Is.EqualTo("RPA_1"));
            Assert.That(request.ClassName, Is.EqualTo("Main"));
        });
    }

    [Test]
    public void CreateFromCode_Success_ReportsWorkflowAndTab()
    {
        var service = new FakeCodeFirstService
        {
            Result = new CodeFirstResult
            {
                Ok = true,
                WorkflowId = "RPA_1.Main",
                FilePath = @"C:\wf\Main.xaml",
                EntryPoint = "Workflow.Build",
                ActivityCount = 4,
                Opened = true,
                Activated = true,
                Xaml = "<Activity />"
            }
        };

        McpToolResult result = Execute(
            new CreateWorkflowFromCodeTool(() => service),
            JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""code"": ""..."", ""project"": ""RPA-1"" }"));

        var content = (JObject)result.Content;

        Assert.That(result.Success, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(content["ok"].Value<bool>(), Is.True);
            Assert.That(content["workflowId"].Value<string>(), Is.EqualTo("RPA_1.Main"));
            Assert.That(content["entryPoint"].Value<string>(), Is.EqualTo("Workflow.Build"));
            Assert.That(content["activityCount"].Value<int>(), Is.EqualTo(4));
            Assert.That(content["opened"].Value<bool>(), Is.True);
            Assert.That(content["xaml"], Is.Null, "успешный ответ не тащит весь XAML агенту");
        });
    }

    [Test]
    public void CreateFromCode_Failure_ReturnsMachineReadableDiagnostics()
    {
        var service = new FakeCodeFirstService
        {
            Result = new CodeFirstResult
            {
                Ok = false,
                Xaml = "<Activity />"
            }
        };

        service.Result.Diagnostics.Add(new RoslynDiagnostic("CS0103", "error", 12, 5, "имя не существует", "проверьте using"));
        service.Result.Errors.Add("CS0103 (строка 12, столбец 5): имя не существует");
        service.Result.Hints.Add("проверьте using");

        McpToolResult result = Execute(
            new CreateWorkflowFromCodeTool(() => service),
            JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""code"": ""..."", ""project"": ""RPA-1"" }"));

        var content = (JObject)result.Content;
        JToken diagnostic = (content["diagnostics"] as JArray)?.FirstOrDefault();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(diagnostic, Is.Not.Null, "диагностика обязательна для самокоррекции агента");
            Assert.That(diagnostic["code"].Value<string>(), Is.EqualTo("CS0103"));
            Assert.That(diagnostic["line"].Value<int>(), Is.EqualTo(12));
            Assert.That(diagnostic["column"].Value<int>(), Is.EqualTo(5));
            Assert.That(diagnostic["hint"].Value<string>(), Is.EqualTo("проверьте using"));
            Assert.That(content["errors"], Is.Not.Null);
            Assert.That(content["xaml"].Value<string>(), Is.EqualTo("<Activity />"),
                "при ошибке агент видит, что именно собралось");
        });
    }

    private static McpToolResult Execute(IMcpTool tool, JObject arguments)
    {
        return tool.Execute(arguments).GetAwaiter().GetResult();
    }

    /// <summary>Поддельный сервис: проверяется разбор аргументов и формат ответа, а не компиляция.</summary>
    private sealed class FakeCodeFirstService : ICodeFirstWorkflowService
    {
        internal CodeFirstResult Result { get; set; } = new CodeFirstResult();

        internal CodeFirstWorkflowRequest LastRequest { get; private set; }

        public CodeFirstResult Create(CodeFirstWorkflowRequest request)
        {
            LastRequest = request;
            return Result;
        }
    }
}
