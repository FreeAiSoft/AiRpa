using System;
using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using RPA.CSharpExpressionEditor;

namespace AiRpaStudio.Tests;

public class ExpressionHelperTests
{
    [Test]
    public void CanTypeBeSerializedAsLiteral_Null_ReturnsFalse()
    {
        Assert.That(ExpressionHelper.CanTypeBeSerializedAsLiteral(null), Is.False);
    }

    [Test]
    public void CanTypeBeSerializedAsLiteral_Object_ReturnsFalse()
    {
        Assert.That(ExpressionHelper.CanTypeBeSerializedAsLiteral(typeof(object)), Is.False);
    }

    [Test]
    public void CanTypeBeSerializedAsLiteral_Int_ReturnsTrue()
    {
        Assert.That(ExpressionHelper.CanTypeBeSerializedAsLiteral(typeof(int)), Is.True);
    }

    [Test]
    public void CanTypeBeSerializedAsLiteral_String_ReturnsTrue()
    {
        Assert.That(ExpressionHelper.CanTypeBeSerializedAsLiteral(typeof(string)), Is.True);
    }

    [Test]
    public void CanTypeBeSerializedAsLiteral_TimeSpan_ReturnsTrue()
    {
        Assert.That(ExpressionHelper.CanTypeBeSerializedAsLiteral(typeof(TimeSpan)), Is.True);
    }

    [Test]
    public void CanTypeBeSerializedAsLiteral_DateTime_ReturnsTrue()
    {
        Assert.That(ExpressionHelper.CanTypeBeSerializedAsLiteral(typeof(DateTime)), Is.True);
    }

    [Test]
    public void CanTypeBeSerializedAsLiteral_Decimal_ReturnsFalse()
    {
        Assert.That(ExpressionHelper.CanTypeBeSerializedAsLiteral(typeof(decimal)), Is.False);
    }

    [Test]
    public void IsExpression_NonResultActivity_ReturnsFalse()
    {
        Assert.That(ExpressionHelper.IsExpression(new Sequence()), Is.False);
    }

    [Test]
    public void IsExpression_ResultActivity_ReturnsTrue()
    {
        Assert.That(ExpressionHelper.IsExpression(new Literal<int>(42)), Is.True);
    }

    [Test]
    public void IsGenericLocationExpressionType_NonLocation_ReturnsFalse()
    {
        Assert.That(ExpressionHelper.IsGenericLocationExpressionType(new Literal<int>(42)), Is.False);
    }

    [Test]
    public void IsGenericLocationExpressionType_Location_ReturnsTrue()
    {
        var variable = new Variable<string>("name");
        Assert.That(ExpressionHelper.IsGenericLocationExpressionType(new VariableReference<string>(variable)), Is.True);
    }

    [Test]
    public void TryCreateLiteral_Int_ReturnsLiteral()
    {
        var expr = ExpressionHelper.TryCreateLiteral(typeof(int), "42", new ParserContext());
        Assert.That(expr, Is.InstanceOf<Literal<int>>());
        Assert.That(((Literal<int>)expr).Value, Is.EqualTo(42));
    }

    [Test]
    public void TryCreateLiteral_QuotedString_ReturnsLiteral()
    {
        var expr = ExpressionHelper.TryCreateLiteral(typeof(string), "\"hi\"", new ParserContext());
        Assert.That(expr, Is.InstanceOf<Literal<string>>());
        Assert.That(((Literal<string>)expr).Value, Is.EqualTo("hi"));
    }

    [Test]
    public void TryCreateLiteral_UnquotedString_ReturnsNull()
    {
        Assert.That(ExpressionHelper.TryCreateLiteral(typeof(string), "hi", new ParserContext()), Is.Null);
    }

    [Test]
    public void TryCreateLiteral_InvalidInt_ReturnsNull()
    {
        Assert.That(ExpressionHelper.TryCreateLiteral(typeof(int), "abc", new ParserContext()), Is.Null);
    }

    [Test]
    public void GetExpressionString_IntLiteral_ReturnsText()
    {
        Assert.That(ExpressionHelper.GetExpressionString(new Literal<int>(42)), Is.EqualTo("42"));
    }

    [Test]
    public void GetExpressionString_StringLiteral_AddsQuotes()
    {
        Assert.That(ExpressionHelper.GetExpressionString(new Literal<string>("hi")), Is.EqualTo("\"hi\""));
    }

    [Test]
    public void GetExpressionString_VariableValue_ReturnsVariableName()
    {
        var variable = new Variable<int>("counter");
        Assert.That(ExpressionHelper.GetExpressionString(new VariableValue<int>(variable)), Is.EqualTo("counter"));
    }
}
