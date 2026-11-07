using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;
using RPA.Mcp;
using RPA.Mcp.Tools;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 4 конвейера генерации XAML: MCP-инструменты палитры
/// (<c>xaml.create_workflow</c>, <c>xaml.add_activity</c>, <c>xaml.set_argument</c>, <c>xaml.get_workflow</c>).
/// <para>
/// Проверяется то, что видит агент: инструменты зарегистрированы на старте сервера, схема каждого
/// описывает аргументы, разбор JSON даёт правильный запрос, а ответ всегда в формате
/// <c>{ok, filePath, activityId, errors[], hints[]}</c>.
/// </para>
/// </summary>
[TestFixture]
public class PaletteToolsTests
{
    // --- Регистрация инструментов. ---

    [Test]
    public void Registry_RegistersAllPaletteTools()
    {
        McpToolRegistry registry = McpToolRegistry.CreateDefault();

        string[] names = registry.Schemas().Select(schema => schema.Name).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EquivalentTo(new[]
            {
                CreateWorkflowTool.ToolName,
                CreateWorkflowFromCodeTool.ToolName,
                CreateWorkflowFromJsonTool.ToolName,
                DescribeWorkflowContractTool.ToolName,
                AddActivityTool.ToolName,
                SetArgumentTool.ToolName,
                GetWorkflowTool.ToolName
            }));

            foreach (McpTool schema in registry.Schemas())
            {
                Assert.That(schema.Description, Is.Not.Null.And.Not.Empty, $"{schema.Name}: описание для агента");
                Assert.That(schema.InputSchema, Is.Not.Null, $"{schema.Name}: схема аргументов");
            }
        });
    }

    [Test]
    public void Server_ExposesPaletteToolsWithoutAnyAdditionalRegistration()
    {
        var server = new RpaMcpServer(McpToolRegistry.CreateDefault());

        McpTool[] tools = server.ListTools().GetAwaiter().GetResult().ToArray();

        Assert.That(tools.Select(tool => tool.Name), Has.Some.StartsWith("xaml."),
            "реестр инструментов обязан быть заполнен на старте MCP-сервера");
    }

    [Test]
    public void Server_UnknownTool_ReportsAvailableNames()
    {
        var server = new RpaMcpServer(McpToolRegistry.CreateDefault());

        McpToolResult result = server.CallTool("xaml.нет-такого", null).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("xaml.add_activity"), "в ошибке перечислены доступные инструменты");
        });
    }

    // --- xaml.create_workflow. ---

    [Test]
    public void CreateWorkflow_WithoutPath_ReturnsParameterError()
    {
        var service = new FakePaletteService();

        McpToolResult result = Execute(new CreateWorkflowTool(() => service), new JObject());

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError?.ParameterName, Is.EqualTo("path"));
            Assert.That(result.Error, Does.Contain("xaml.path"));
            Assert.That(service.CreateRequest, Is.Null, "сервис не вызывается при неверных аргументах");
        });
    }

    [Test]
    public void CreateWorkflow_ParsesStepsExpressionsAndChildren()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{
            ""path"": ""C:\\wf\\Main.xaml"",
            ""project"": ""RPA-1"",
            ""activities"": [
                {
                    ""type"": ""WriteLine"",
                    ""displayName"": ""Приветствие"",
                    ""arguments"": { ""Text"": { ""expression"": ""\""привет\"""", ""type"": ""String"" } }
                },
                {
                    ""type"": ""If"",
                    ""arguments"": { ""Condition"": { ""expression"": ""true"" } },
                    ""children"": { ""Then"": [ { ""type"": ""WriteLine"" } ] }
                }
            ]
        }");

        Execute(new CreateWorkflowTool(() => service), arguments);

        PaletteCreateWorkflowRequest request = service.CreateRequest;

        Assert.That(request, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(request.FilePath, Is.EqualTo(@"C:\wf\Main.xaml"));
            Assert.That(request.ProjectName, Is.EqualTo("RPA-1"));
            Assert.That(request.Activities, Has.Count.EqualTo(2));
            Assert.That(request.Activities[0].DisplayName, Is.EqualTo("Приветствие"));
            Assert.That(request.Activities[0].Arguments["Text"].Expression, Is.EqualTo("\"привет\""));
            Assert.That(request.Activities[1].Children["Then"], Has.Count.EqualTo(1));
            Assert.That(request.Activities[1].Children["Then"][0].ActivityType, Is.EqualTo("WriteLine"));
        });
    }

    [Test]
    public void CreateWorkflow_StepWithoutType_ReturnsParameterError()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""activities"": [ { ""displayName"": ""без типа"" } ] }");

        McpToolResult result = Execute(new CreateWorkflowTool(() => service), arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("не задан тип активности"));
            Assert.That(service.CreateRequest, Is.Null);
        });
    }

    // --- xaml.add_activity. ---

    [Test]
    public void AddActivity_ParsesTypeArgumentsChildrenAndPosition()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{
            ""path"": ""C:\\wf\\Main.xaml"",
            ""type"": ""ForEach"",
            ""typeArguments"": [ ""String"" ],
            ""arguments"": { ""Values"": { ""expression"": ""new string[0]"" } },
            ""children"": { ""Body"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": { ""expression"": ""item"" } } } ] },
            ""parentId"": ""root"",
            ""container"": ""Activities"",
            ""index"": 2
        }");

        McpToolResult result = Execute(new AddActivityTool(() => service), arguments);

        PaletteAddActivityRequest request = service.AddRequest;

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(request, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(request.Activity.ActivityType, Is.EqualTo("ForEach"));
            Assert.That(request.Activity.TypeArguments, Is.EqualTo(new[] { "String" }));
            Assert.That(request.Activity.Children["Body"], Has.Count.EqualTo(1));
            Assert.That(request.ParentActivityId, Is.EqualTo("root"));
            Assert.That(request.ContainerName, Is.EqualTo("Activities"));
            Assert.That(request.Index, Is.EqualTo(2));
        });
    }

    [Test]
    public void AddActivity_NegativeIndex_ReturnsParameterError()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""type"": ""WriteLine"", ""index"": -1 }");

        McpToolResult result = Execute(new AddActivityTool(() => service), arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError?.ParameterName, Is.EqualTo("index"));
            Assert.That(service.AddRequest, Is.Null);
        });
    }

    // --- xaml.set_argument. ---

    [Test]
    public void SetArgument_PassesExpression()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{
            ""path"": ""C:\\wf\\Main.xaml"",
            ""activityId"": ""root/Activities[1]"",
            ""argument"": ""Text"",
            ""expression"": ""\""готово\""""
        }");

        Execute(new SetArgumentTool(() => service), arguments);

        PaletteSetArgumentRequest request = service.SetRequest;

        Assert.That(request, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(request.ActivityId, Is.EqualTo("root/Activities[1]"));
            Assert.That(request.ArgumentName, Is.EqualTo("Text"));
            Assert.That(request.Value.IsExpression, Is.True);
            Assert.That(request.Value.Expression, Is.EqualTo("\"готово\""));
        });
    }

    [Test]
    public void SetArgument_AcceptsLiteralValue()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{
            ""path"": ""C:\\wf\\Main.xaml"",
            ""activityId"": ""root/Activities[1]"",
            ""argument"": ""Delay"",
            ""value"": 5
        }");

        Execute(new SetArgumentTool(() => service), arguments);

        Assert.Multiple(() =>
        {
            Assert.That(service.SetRequest.Value.HasLiteral, Is.True);
            Assert.That(service.SetRequest.Value.Literal, Is.EqualTo(5));
        });
    }

    [Test]
    public void SetArgument_WithoutValue_ReturnsParameterError()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""activityId"": ""root"", ""argument"": ""Text"" }");

        McpToolResult result = Execute(new SetArgumentTool(() => service), arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("ни expression, ни value"));
            Assert.That(service.SetRequest, Is.Null);
        });
    }

    // --- xaml.get_workflow и формат ответа. ---

    [Test]
    public void GetWorkflow_PassesIncludeXamlFlag()
    {
        var service = new FakePaletteService();
        var arguments = JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""includeXaml"": true }");

        McpToolResult result = Execute(new GetWorkflowTool(() => service), arguments);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(service.GetRequest.IncludeXaml, Is.True);
            Assert.That(((JObject)result.Content)["ok"]?.Value<bool>(), Is.True);
        });
    }

    [Test]
    public void Respond_Failure_KeepsStructuredErrorContent()
    {
        var service = new FakePaletteService
        {
            NextResult = PaletteResult.Failure(new[] { "активность «Нет» не найдена в палитре" }, new[] { "используйте Sequence" })
        };

        McpToolResult result = Execute(new AddActivityTool(() => service),
            JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""type"": ""Нет"" }"));

        var content = (JObject)result.Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("не найдена в палитре"));
            Assert.That(content["ok"]?.Value<bool>(), Is.False);
            Assert.That(content["errors"]?.ToObject<string[]>(), Has.Length.EqualTo(1));
            Assert.That(content["hints"]?.ToObject<string[]>(), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public void Respond_Success_ReportsWorkflowIdFilePathAndActivityId()
    {
        var service = new FakePaletteService
        {
            NextResult = PaletteResult.Success(@"C:\wf\Main.xaml", "RPA_1.Main", "root/Activities[2]")
        };

        McpToolResult result = Execute(new AddActivityTool(() => service),
            JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""type"": ""WriteLine"" }"));

        var content = (JObject)result.Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(content["workflowId"]?.Value<string>(), Is.EqualTo("RPA_1.Main"));
            Assert.That(content["filePath"]?.Value<string>(), Is.EqualTo(@"C:\wf\Main.xaml"));
            Assert.That(content["activityId"]?.Value<string>(), Is.EqualTo("root/Activities[2]"));
        });
    }

    private static McpToolResult Execute(IMcpTool tool, JObject arguments)
    {
        return tool.Execute(arguments).GetAwaiter().GetResult();
    }

    /// <summary>Поддельный сервис палитры: запоминает запросы и возвращает заданный результат.</summary>
    private sealed class FakePaletteService : IPaletteWorkflowService
    {
        internal PaletteCreateWorkflowRequest CreateRequest { get; private set; }

        internal PaletteAddActivityRequest AddRequest { get; private set; }

        internal PaletteSetArgumentRequest SetRequest { get; private set; }

        internal PaletteGetWorkflowRequest GetRequest { get; private set; }

        internal PaletteResult NextResult { get; set; } =
            PaletteResult.Success(@"C:\wf\Main.xaml", "RPA_1.Main", "root/Activities[0]");

        public PaletteResult CreateWorkflow(PaletteCreateWorkflowRequest request)
        {
            CreateRequest = request;
            return NextResult;
        }

        public PaletteResult AddActivity(PaletteAddActivityRequest request)
        {
            AddRequest = request;
            return NextResult;
        }

        public PaletteResult SetArgument(PaletteSetArgumentRequest request)
        {
            SetRequest = request;
            return NextResult;
        }

        public PaletteResult GetWorkflow(PaletteGetWorkflowRequest request)
        {
            GetRequest = request;
            return NextResult;
        }
    }
}
