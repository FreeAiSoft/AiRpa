using Newtonsoft.Json.Linq;
using RPA.Mcp;
using RPA.Mcp.Tools;

namespace AiRpaStudio.Tests;

public class JsonExampleGeneratorTests
{
    private static McpTool CreateSchema()
    {
        return new McpTool
        {
            Name = "read_file",
            InputSchema = JObject.Parse(@"{
                ""required"": [""path""],
                ""properties"": {
                    ""path"": { ""type"": ""string"", ""description"": ""The path"" },
                    ""startLine"": { ""type"": ""integer"" }
                }
            }")
        };
    }

    [Test]
    public void GenerateFromSchema_Null_ReturnsNoSchemaMessage()
    {
        Assert.That(JsonExampleGenerator.GenerateFromSchema(null), Is.EqualTo("// No schema available"));
    }

    [Test]
    public void GenerateMinimalExample_Null_ReturnsNoSchemaMessage()
    {
        Assert.That(JsonExampleGenerator.GenerateMinimalExample(null), Is.EqualTo("// No schema available"));
    }

    [Test]
    public void GenerateFromSchema_ProducesJsonRpcWithRequiredAndOptional()
    {
        string output = JsonExampleGenerator.GenerateFromSchema(CreateSchema());
        Assert.That(output, Does.Contain("```json"));
        Assert.That(output, Does.Contain("\"jsonrpc\": \"2.0\""));
        Assert.That(output, Does.Contain("\"method\": \"tools/call\""));
        Assert.That(output, Does.Contain("\"name\": \"read_file\""));
        Assert.That(output, Does.Contain("\"path\": \"example_path\", // REQUIRED - The path"));
        Assert.That(output, Does.Contain("\"startLine\": 0 // optional"));
    }

    [Test]
    public void GenerateMinimalExample_IncludesOnlyRequired()
    {
        string output = JsonExampleGenerator.GenerateMinimalExample(CreateSchema());
        Assert.That(output, Does.Contain("\"name\": \"read_file\""));
        Assert.That(output, Does.Contain("\"path\": \"example_path\""));
        Assert.That(output, Does.Not.Contain("startLine"));
    }
}
