using RPA.Mcp;

namespace AiRpaStudio.Tests;

public class McpModelsTests
{
    [Test]
    public void McpRequest_Defaults_JsonRpc20()
    {
        var r = new McpRequest();
        Assert.That(r.JsonRpc, Is.EqualTo("2.0"));
        Assert.That(r.Method, Is.Null);
        Assert.That(r.Params, Is.Null);
        Assert.That(r.Id, Is.Null);
    }

    [Test]
    public void McpResponse_Defaults_JsonRpc20()
    {
        var r = new McpResponse();
        Assert.That(r.JsonRpc, Is.EqualTo("2.0"));
        Assert.That(r.Result, Is.Null);
        Assert.That(r.Error, Is.Null);
        Assert.That(r.Id, Is.Null);
    }

    [Test]
    public void McpError_Defaults()
    {
        var e = new McpError();
        Assert.That(e.Code, Is.EqualTo(0));
        Assert.That(e.Message, Is.Null);
        Assert.That(e.Data, Is.Null);
    }

    [Test]
    public void McpServerInfo_Defaults()
    {
        var i = new McpServerInfo();
        Assert.That(i.Name, Is.Null);
        Assert.That(i.Version, Is.Null);
        Assert.That(i.Description, Is.Null);
    }

    [Test]
    public void McpTool_Defaults()
    {
        var t = new McpTool();
        Assert.That(t.Name, Is.Null);
        Assert.That(t.Description, Is.Null);
        Assert.That(t.InputSchema, Is.Null);
    }

    [Test]
    public void McpToolResult_Defaults()
    {
        var r = new McpToolResult();
        Assert.That(r.Success, Is.False);
        Assert.That(r.Content, Is.Null);
        Assert.That(r.Error, Is.Null);
        Assert.That(r.ValidationError, Is.Null);
    }

    [Test]
    public void ValidationError_Defaults()
    {
        var e = new ValidationError();
        Assert.That(e.Type, Is.EqualTo(ValidationErrorType.MissingRequiredParameter));
        Assert.That(e.Message, Is.Null);
    }

    [Test]
    public void ValidationResult_Defaults()
    {
        var r = new ValidationResult();
        Assert.That(r.IsValid, Is.False);
        Assert.That(r.ErrorType, Is.EqualTo(ValidationErrorType.MissingRequiredParameter));
        Assert.That(r.ErrorMessage, Is.Null);
    }

    [Test]
    public void OperationResult_Defaults()
    {
        var r = new OperationResult();
        Assert.That(r.Success, Is.False);
        Assert.That(r.Message, Is.Null);
    }
}
