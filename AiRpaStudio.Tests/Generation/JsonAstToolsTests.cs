using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RPA.Core.Generation;
using RPA.Mcp;
using RPA.Mcp.Tools;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 6 конвейера генерации XAML: MCP-инструмент
/// <c>xaml.create_workflow_from_json</c>.
/// <para>
/// Контракт с агентом: инструмент зарегистрирован на старте сервера, требует <c>path</c> и <c>ast</c>,
/// умеет отдавать схему и whitelist'ы (<c>describe: true</c>), разбирает описание и объектом, и
/// JSON-строкой, а ответ всегда в формате <c>{ok, workflowId, errors[], hints[]}</c>.
/// </para>
/// </summary>
[TestFixture]
public class JsonAstToolsTests
{
    private readonly List<string> _temporaryDirectories = new List<string>();

    [TearDown]
    public void CleanupTemporaryDirectories()
    {
        foreach (string directory in _temporaryDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception)
            {
                // Каталог мог остаться заблокированным — на результат теста это не влияет.
            }
        }

        _temporaryDirectories.Clear();
    }

    [Test]
    public void Registry_RegistersJsonAstTool()
    {
        McpToolRegistry registry = McpToolRegistry.CreateDefault();

        Assert.That(registry.TryGet(CreateWorkflowFromJsonTool.ToolName, out IMcpTool tool), Is.True,
            "инструмент JSON AST обязан быть зарегистрирован на старте MCP-сервера");
        Assert.That(tool, Is.TypeOf<CreateWorkflowFromJsonTool>());
    }

    [Test]
    public void Describe_ReturnsSchemaWhitelistAndExample()
    {
        McpToolResult result = Execute(
            new CreateWorkflowFromJsonTool(() => new FakeJsonService()),
            JObject.Parse(@"{ ""describe"": true }"));

        var content = (JObject)result.Content;
        var activities = (JArray)content["activities"];
        var types = (JArray)content["types"];

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(content["schema"]?["additionalProperties"]?.Value<bool>(), Is.False,
                "схема отдаётся агенту вместе с признаком строгости");
            Assert.That(activities?.Select(token => token.Value<string>()), Does.Contain("Sequence"));
            Assert.That(types?.Select(token => token.Value<string>()), Does.Contain("String"));
            Assert.That(content["example"]?.Value<string>(), Does.Contain("activities"),
                "пример описания прилагается: агент не угадывает структуру");
        });
    }

    [Test]
    public void CreateFromJson_WithoutPath_ReturnsParameterError()
    {
        var service = new FakeJsonService();

        McpToolResult result = Execute(
            new CreateWorkflowFromJsonTool(() => service),
            JObject.Parse(@"{ ""ast"": { ""activities"": [] } }"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError?.ParameterName, Is.EqualTo("path"));
            Assert.That(service.LastRequest, Is.Null, "сервис не вызывается при неверных аргументах");
        });
    }

    [Test]
    public void CreateFromJson_WithoutAst_ReturnsParameterErrorWithSchemaHints()
    {
        var service = new FakeJsonService();

        McpToolResult result = Execute(
            new CreateWorkflowFromJsonTool(() => service),
            JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"" }"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ValidationError?.ParameterName, Is.EqualTo("ast"));
            Assert.That(result.Error, Does.Contain("xaml.ast"));
            Assert.That(((JObject)result.Content)["hints"].ToString(), Does.Contain("activities"));
            Assert.That(service.LastRequest, Is.Null);
        });
    }

    [Test]
    public void CreateFromJson_ParsesObjectAndArguments()
    {
        var service = new FakeJsonService();
        var arguments = JObject.Parse(@"{
            ""path"": ""C:\\wf\\Main.xaml"",
            ""ast"": { ""activities"": [ { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""\""x\"""" } } ] },
            ""project"": ""RPA-1"",
            ""namespace"": ""RPA_1"",
            ""className"": ""Main""
        }");

        Execute(new CreateWorkflowFromJsonTool(() => service), arguments);

        JsonWorkflowRequest request = service.LastRequest;

        Assert.That(request, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(request.FilePath, Is.EqualTo(@"C:\wf\Main.xaml"));
            Assert.That(request.Json, Does.Contain("WriteLine"), "описание передано сервису текстом JSON");
            Assert.That(request.ProjectName, Is.EqualTo("RPA-1"));
            Assert.That(request.NamespaceName, Is.EqualTo("RPA_1"));
            Assert.That(request.ClassName, Is.EqualTo("Main"));
        });
    }

    [Test]
    public void CreateFromJson_AcceptsJsonString()
    {
        var service = new FakeJsonService();
        var arguments = new JObject
        {
            ["path"] = @"C:\wf\Main.xaml",
            ["ast"] = @"{ ""activities"": [ { ""type"": ""Sequence"" } ] }"
        };

        Execute(new CreateWorkflowFromJsonTool(() => service), arguments);

        Assert.That(service.LastRequest?.Json, Does.Contain("\"activities\""),
            "агент может прислать описание строкой — она уходит в валидатор как есть");
    }

    [Test]
    public void CreateFromJson_Success_ReportsWorkflowAndVariables()
    {
        var service = new FakeJsonService
        {
            Result = new JsonWorkflowResult
            {
                Ok = true,
                WorkflowId = "RPA_1.Main",
                FilePath = @"C:\wf\Main.xaml",
                ActivityCount = 4,
                Opened = true,
                Activated = true,
                Xaml = "<Activity />"
            }
        };

        service.Result.Variables.Add("output");
        service.Result.Namespaces.Add("System");

        McpToolResult result = Execute(
            new CreateWorkflowFromJsonTool(() => service),
            JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""ast"": { ""activities"": [] } }"));

        var content = (JObject)result.Content;

        Assert.That(result.Success, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(content["ok"].Value<bool>(), Is.True);
            Assert.That(content["workflowId"].Value<string>(), Is.EqualTo("RPA_1.Main"));
            Assert.That(content["activityCount"].Value<int>(), Is.EqualTo(4));
            Assert.That(content["opened"].Value<bool>(), Is.True);
            Assert.That(content["variables"].ToString(), Does.Contain("output"));
            Assert.That(content["xaml"], Is.Null, "успешный ответ не тащит весь XAML агенту");
        });
    }

    [Test]
    public void CreateFromJson_Failure_ReturnsErrorsAndXaml()
    {
        var service = new FakeJsonService
        {
            Result = new JsonWorkflowResult { Ok = false, Xaml = "<Activity />" }
        };

        service.Result.Errors.Add("$.activities[0].type: активность «Teleport» отсутствует в whitelist палитры");
        service.Result.Hints.Add("доступные активности: Sequence, WriteLine");

        McpToolResult result = Execute(
            new CreateWorkflowFromJsonTool(() => service),
            JObject.Parse(@"{ ""path"": ""C:\\wf\\Main.xaml"", ""ast"": { ""activities"": [] } }"));

        var content = (JObject)result.Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(content["errors"].ToString(), Does.Contain("whitelist"), "ошибка с путём до поля");
            Assert.That(content["hints"].ToString(), Does.Contain("Sequence"));
            Assert.That(content["xaml"].Value<string>(), Is.EqualTo("<Activity />"),
                "при ошибке агент видит, что именно собралось");
        });
    }

    [Test]
    public void CreateFromJson_RealService_WritesWorkflowFile()
    {
        string root = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(root);

        string directory = Path.Combine(root, "RPA-1");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "Main.xaml");

        var arguments = JObject.Parse(@"{
            ""ast"": {
                ""variables"": [ { ""name"": ""output"", ""type"": ""String"" } ],
                ""activities"": [
                    { ""type"": ""Assign"", ""arguments"": { ""To"": ""output"", ""Value"": ""\""готово\"""" } },
                    { ""type"": ""WriteLine"", ""arguments"": { ""Text"": ""output"" } }
                ]
            }
        }");

        arguments["path"] = path;

        McpToolResult result = Execute(
            new CreateWorkflowFromJsonTool(() => new JsonWorkflowService()),
            arguments);

        var content = (JObject)result.Content;

        Assert.That(result.Success, Is.True, result.Error);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True, "файл процесса записан сервисом JSON AST");
            Assert.That(File.ReadAllText(path), Does.Contain("x:Class=\"RPA_1.Main\""),
                "класс и сборка проекта выведены из пути");
            Assert.That(content["workflowId"].Value<string>(), Is.EqualTo("RPA_1.Main"));
            Assert.That(content["variables"].ToString(), Does.Contain("output"));
        });
    }

    private static McpToolResult Execute(IMcpTool tool, JObject arguments)
    {
        return tool.Execute(arguments).GetAwaiter().GetResult();
    }

    /// <summary>Поддельный сервис: проверяется разбор аргументов и формат ответа, а не сборка процесса.</summary>
    private sealed class FakeJsonService : IJsonWorkflowService
    {
        internal JsonWorkflowResult Result { get; set; } = new JsonWorkflowResult();

        internal JsonWorkflowRequest LastRequest { get; private set; }

        public JsonWorkflowResult Create(JsonWorkflowRequest request)
        {
            LastRequest = request;
            return Result;
        }
    }
}
