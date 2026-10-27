using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPA.Mcp;

namespace AiRpaStudio.Tests;

public class McpCommandHelperTests
{
    [Test]
    public void CreateInitializeRequest_SerializesInitializeMethod()
    {
        var req = JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateInitializeRequest());
        Assert.That(req.JsonRpc, Is.EqualTo("2.0"));
        Assert.That(req.Method, Is.EqualTo("initialize"));
        Assert.That(req.Params, Is.Not.Null);
        Assert.That(req.Id, Is.Not.Null);
    }

    [Test]
    public void CreateListToolsRequest_SerializesToolsListMethod()
    {
        var req = JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateListToolsRequest());
        Assert.That(req.JsonRpc, Is.EqualTo("2.0"));
        Assert.That(req.Method, Is.EqualTo("tools/list"));
    }

    [Test]
    public void RequestIds_IncreaseMonotonically()
    {
        long id1 = Convert.ToInt64(JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateInitializeRequest()).Id);
        long id2 = Convert.ToInt64(JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateListToolsRequest()).Id);
        Assert.That(id2, Is.GreaterThan(id1));
    }

    [Test]
    public void CreateToolCallRequest_EmbedsNameAndArguments()
    {
        var req = JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateToolCallRequest("read_file", new { path = "Main.xaml" }));
        var p = (JObject)req.Params;
        Assert.That(req.Method, Is.EqualTo("tools/call"));
        Assert.That(p["name"].Value<string>(), Is.EqualTo("read_file"));
        Assert.That(p["arguments"]["path"].Value<string>(), Is.EqualTo("Main.xaml"));
    }

    [Test]
    public void CreateListFilesRequest_EmbedsPathAndFlags()
    {
        var req = JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateListFilesRequest(".", true, false));
        var p = (JObject)req.Params;
        Assert.That(p["name"].Value<string>(), Is.EqualTo("list_files"));
        Assert.That(p["arguments"]["path"].Value<string>(), Is.EqualTo("."));
        Assert.That(p["arguments"]["recursive"].Value<bool>(), Is.True);
        Assert.That(p["arguments"]["includeHidden"].Value<bool>(), Is.False);
    }

    [Test]
    public void CreateReadFileRequest_EmbedsPathAndLines()
    {
        var req = JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateReadFileRequest("Main.xaml", 1, 10));
        var p = (JObject)req.Params;
        Assert.That(p["name"].Value<string>(), Is.EqualTo("read_file"));
        Assert.That(p["arguments"]["path"].Value<string>(), Is.EqualTo("Main.xaml"));
        Assert.That(p["arguments"]["startLine"].Value<int>(), Is.EqualTo(1));
        Assert.That(p["arguments"]["endLine"].Value<int>(), Is.EqualTo(10));
    }

    [Test]
    public void CreateSearchFilesRequest_EmbedsPatternAndOptions()
    {
        var req = JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateSearchFilesRequest(".", "public class", "*.cs", 42));
        var p = (JObject)req.Params;
        Assert.That(p["name"].Value<string>(), Is.EqualTo("search_files"));
        Assert.That(p["arguments"]["pattern"].Value<string>(), Is.EqualTo("public class"));
        Assert.That(p["arguments"]["filePattern"].Value<string>(), Is.EqualTo("*.cs"));
        Assert.That(p["arguments"]["maxResults"].Value<int>(), Is.EqualTo(42));
    }

    [Test]
    public void CreateGetFileInfoRequest_EmbedsPath()
    {
        var req = JsonConvert.DeserializeObject<McpRequest>(McpCommandHelper.CreateGetFileInfoRequest("Main.xaml"));
        var p = (JObject)req.Params;
        Assert.That(p["name"].Value<string>(), Is.EqualTo("get_file_info"));
        Assert.That(p["arguments"]["path"].Value<string>(), Is.EqualTo("Main.xaml"));
    }

    [Test]
    public void ParseResponse_DeserializesJsonRpcResponse()
    {
        var resp = McpCommandHelper.ParseResponse("{\"jsonrpc\":\"2.0\",\"result\":{\"ok\":true},\"id\":7}");
        Assert.That(resp.JsonRpc, Is.EqualTo("2.0"));
        Assert.That(resp.Result, Is.Not.Null);
        Assert.That(resp.Error, Is.Null);
        Assert.That(Convert.ToInt32(resp.Id), Is.EqualTo(7));
    }

    [Test]
    public void ConvertSimpleCommandToJsonRpc_WithAction_ProducesToolCall()
    {
        string result = McpCommandHelper.ConvertSimpleCommandToJsonRpc("{\"action\":\"list_files\",\"path\":\".\"}");
        var req = JsonConvert.DeserializeObject<McpRequest>(result);
        var p = (JObject)req.Params;
        Assert.That(req.Method, Is.EqualTo("tools/call"));
        Assert.That(p["name"].Value<string>(), Is.EqualTo("list_files"));
        Assert.That(p["arguments"]["path"].Value<string>(), Is.EqualTo("."));
    }

    [Test]
    public void ConvertSimpleCommandToJsonRpc_WithoutAction_ReturnsAsIs()
    {
        const string input = "{\"method\":\"initialize\"}";
        Assert.That(McpCommandHelper.ConvertSimpleCommandToJsonRpc(input), Is.EqualTo(input));
    }

    [Test]
    public void ConvertSimpleCommandToJsonRpc_InvalidJson_ReturnsAsIs()
    {
        const string input = "not json";
        Assert.That(McpCommandHelper.ConvertSimpleCommandToJsonRpc(input), Is.EqualTo(input));
    }
}
