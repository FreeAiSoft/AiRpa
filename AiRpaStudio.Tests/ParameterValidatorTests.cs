using Newtonsoft.Json.Linq;
using RPA.Mcp;
using RPA.Mcp.Tools;

namespace AiRpaStudio.Tests;

public class ParameterValidatorTests
{
    private static McpTool CreateSchema()
    {
        return new McpTool
        {
            Name = "test",
            InputSchema = JObject.Parse(@"{
                ""required"": [""name""],
                ""properties"": {
                    ""name"": { ""type"": ""string"" },
                    ""count"": { ""type"": ""integer"" },
                    ""active"": { ""type"": ""boolean"" },
                    ""ratio"": { ""type"": ""number"" },
                    ""mode"": { ""type"": ""string"", ""enum"": [""fast"", ""slow""] }
                }
            }")
        };
    }

    [Test]
    public void Validate_NullArguments_ReturnsMissingRequiredParameter()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(null);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(ValidationErrorType.MissingRequiredParameter));
        Assert.That(result.ErrorMessage, Is.EqualTo("Arguments object is null"));
    }

    [Test]
    public void Validate_MissingRequired_ReportsParameter()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { count = 1 });
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(ValidationErrorType.MissingRequiredParameter));
        Assert.That(result.ParameterName, Is.EqualTo("name"));
        Assert.That(result.ExpectedType, Is.EqualTo("string"));
    }

    [Test]
    public void Validate_ValidArguments_IsValid()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x" });
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_UnknownParameter_ReportsUnknown()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x", foo = 1 });
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(ValidationErrorType.UnknownParameter));
        Assert.That(result.ParameterName, Is.EqualTo("foo"));
    }

    [Test]
    public void Validate_WrongType_ReportsInvalidType()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x", count = "abc" });
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(ValidationErrorType.InvalidParameterType));
        Assert.That(result.ParameterName, Is.EqualTo("count"));
        Assert.That(result.ExpectedType, Is.EqualTo("integer"));
        Assert.That(result.ActualType, Is.EqualTo("string"));
    }

    [Test]
    public void Validate_IntegerWithFloat_ReportsInvalidType()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x", count = 1.5 });
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(ValidationErrorType.InvalidParameterType));
    }

    [Test]
    public void Validate_InvalidEnum_ReportsInvalidValue()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x", mode = "unknown" });
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(ValidationErrorType.InvalidParameterValue));
        Assert.That(result.ParameterName, Is.EqualTo("mode"));
    }

    [Test]
    public void Validate_ValidEnum_IsValid()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x", mode = "fast" });
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_NumberAcceptsInteger()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x", ratio = 3 });
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void Validate_NullValue_ReportsInvalidType()
    {
        var validator = new ParameterValidator(CreateSchema());
        var result = validator.Validate(new { name = "x", count = (int?)null });
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(ValidationErrorType.InvalidParameterType));
    }
}
