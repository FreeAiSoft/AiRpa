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
/// Этап 7 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: MCP-инструмент
/// <c>xaml.describe_contract</c>.
/// <para>
/// Контракт должен быть доступен агенту из студии, а не «из памяти модели»: инструмент отдаёт правила,
/// матрицу выбора инструмента, few-shot примеры и правило самокоррекции, умеет вернуть один пример
/// по имени и готовый текст для системного промпта, а на неизвестное имя отвечает структурированной
/// ошибкой с перечнем доступных примеров.
/// </para>
/// </summary>
[TestFixture]
public class AgentContractToolsTests
{
    [Test]
    public void Registry_RegistersDescribeContractTool()
    {
        McpToolRegistry registry = McpToolRegistry.CreateDefault();

        Assert.That(registry.TryGet(DescribeWorkflowContractTool.ToolName, out IMcpTool tool), Is.True,
            "инструмент контракта обязан быть зарегистрирован на старте MCP-сервера");
        Assert.That(tool, Is.TypeOf<DescribeWorkflowContractTool>());

        McpTool schema = tool.GetSchema();
        Assert.Multiple(() =>
        {
            Assert.That(schema.Name, Is.EqualTo(ContractToolNames.Describe));
            Assert.That(schema.Description, Is.Not.Null.And.Not.Empty, "описание для агента");
            Assert.That(schema.InputSchema, Is.Not.Null, "схема аргументов");
        });
    }

    [Test]
    public void Describe_ReturnsRulesToolSelectionExamplesAndSelfCorrection()
    {
        McpToolResult result = Execute(new DescribeWorkflowContractTool(), new JObject());
        var content = (JObject)result.Content;

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(content["ok"]?.Value<bool>(), Is.True);
            Assert.That(content["contract"]?.Value<string>(), Is.EqualTo(WorkflowAgentContract.Version));
            Assert.That(((JArray)content["rules"]).Count, Is.EqualTo(WorkflowAgentContract.Rules.Length),
                "все правила генерации отдаются агенту");
            Assert.That(((JArray)content["toolSelection"]).Count,
                Is.EqualTo(WorkflowAgentContract.ToolSelection.Length), "матрица выбора инструмента");
            Assert.That(((JArray)content["selfCorrection"]).Count,
                Is.EqualTo(WorkflowAgentContract.SelfCorrection.Length), "правило самокоррекции");
            Assert.That(((JArray)content["hints"]).Count, Is.GreaterThan(0), "подсказки: куда смотреть дальше");
            Assert.That(content["text"], Is.Null, "по умолчанию текст промпта не дублируется");
        });

        var examples = (JArray)content["examples"];
        Assert.That(examples.Count, Is.EqualTo(WorkflowAgentContract.Examples.Length));

        foreach (JObject example in examples.OfType<JObject>())
        {
            Assert.Multiple(() =>
            {
                Assert.That(example["name"]?.Value<string>(), Is.Not.Null.And.Not.Empty);
                Assert.That(example["scenario"]?.Value<string>(), Is.Not.Null.And.Not.Empty);
                Assert.That(example["payload"]?.Value<string>(), Is.Not.Null.And.Not.Empty,
                    "пример отдаётся целиком: агент подставляет его в инструмент как есть");
                Assert.That(example["tool"]?.Value<string>(),
                    Is.EqualTo(ContractToolNames.Json).Or.EqualTo(ContractToolNames.CodeFirst));
                Assert.That(example["format"]?.Value<string>(), Is.EqualTo("json").Or.EqualTo("csharp"));
                Assert.That(example["notes"]?.Value<string>(), Is.Not.Null.And.Not.Empty);
            });
        }
    }

    [Test]
    public void Describe_WithExample_ReturnsOnlyThatExampleWithPromptText()
    {
        McpToolResult result = Execute(
            new DescribeWorkflowContractTool(),
            JObject.Parse(@"{ ""example"": ""if"", ""format"": ""text"" }"));

        var content = (JObject)result.Content;
        var examples = (JArray)content["examples"];

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(examples.Count, Is.EqualTo(1), "фильтр по имени оставляет один пример");
            Assert.That(examples[0]?["name"]?.Value<string>(), Is.EqualTo("if"));
            Assert.That(content["text"]?.Value<string>(), Does.Contain("### if"),
                "text: готовый фрагмент промпта с примером");
            Assert.That(content["text"]?.Value<string>(), Does.Contain(ContractToolNames.Json));
        });
    }

    [Test]
    public void Describe_WithoutExample_TextFormat_ReturnsFullPrompt()
    {
        McpToolResult result = Execute(
            new DescribeWorkflowContractTool(),
            JObject.Parse(@"{ ""format"": ""text"" }"));

        var text = ((JObject)result.Content)["text"]?.Value<string>();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(text, Is.EqualTo(WorkflowAgentContract.Describe()),
                "text-формат — тот же текст, что проверен тестами контракта");
            Assert.That(text, Does.Contain("## Самокоррекция"));
        });
    }

    [Test]
    public void Describe_UnknownExample_ReturnsParameterErrorWithAvailableNames()
    {
        McpToolResult result = Execute(
            new DescribeWorkflowContractTool(),
            JObject.Parse(@"{ ""example"": ""no-such-example"" }"));

        var content = (JObject)result.Content;
        string hints = string.Join("\n", ((JArray)content["hints"]).Select(hint => hint.Value<string>()));

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(content["ok"]?.Value<bool>(), Is.False);
            Assert.That(result.ValidationError, Is.Not.Null, "ошибка аргумента, а не сбой инструмента");
            Assert.That(result.ValidationError.ParameterName, Is.EqualTo("example"));
            Assert.That(hints, Does.Contain("sequence-assign"), "агент видит доступные имена примеров");
            Assert.That(hints, Does.Contain("bookmark-persist"));
        });
    }

    private static McpToolResult Execute(IMcpTool tool, JObject arguments)
    {
        return tool.Execute(arguments).GetAwaiter().GetResult();
    }
}
