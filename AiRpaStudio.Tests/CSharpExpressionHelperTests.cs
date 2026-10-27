using System;
using System.Activities.Expressions;
using Microsoft.CSharp.Activities;
using RPA.CSharpExpressionEditor;

namespace AiRpaStudio.Tests;

public class CSharpExpressionHelperTests
{
    [Test]
    public void CreateExpressionFromString_Int_ReturnsLiteral()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("42", false, typeof(int));
        Assert.That(expr, Is.InstanceOf<Literal<int>>());
        Assert.That(((Literal<int>)expr).Value, Is.EqualTo(42));
    }

    [Test]
    public void CreateExpressionFromString_QuotedString_ReturnsLiteral()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("\"hello\"", false, typeof(string));
        Assert.That(expr, Is.InstanceOf<Literal<string>>());
        Assert.That(((Literal<string>)expr).Value, Is.EqualTo("hello"));
    }

    [Test]
    public void CreateExpressionFromString_UnquotedString_ReturnsCSharpValue()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("hello", false, typeof(string));
        Assert.That(expr, Is.InstanceOf<CSharpValue<string>>());
    }

    [Test]
    public void CreateExpressionFromString_BoolTrue_ReturnsLiteral()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("true", false, typeof(bool));
        Assert.That(expr, Is.InstanceOf<Literal<bool>>());
        Assert.That(((Literal<bool>)expr).Value, Is.True);
    }

    [Test]
    public void CreateExpressionFromString_InvalidInt_ReturnsCSharpValue()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("abc", false, typeof(int));
        Assert.That(expr, Is.InstanceOf<CSharpValue<int>>());
        Assert.That(((CSharpValue<int>)expr).ExpressionText, Is.EqualTo("abc"));
    }

    [Test]
    public void CreateExpressionFromString_Char_ReturnsLiteral()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("'a'", false, typeof(char));
        Assert.That(expr, Is.InstanceOf<Literal<char>>());
        Assert.That(((Literal<char>)expr).Value, Is.EqualTo('a'));
    }

    [Test]
    public void CreateExpressionFromString_TimeSpan_ReturnsLiteral()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("00:01:00", false, typeof(TimeSpan));
        Assert.That(expr, Is.InstanceOf<Literal<TimeSpan>>());
        Assert.That(((Literal<TimeSpan>)expr).Value, Is.EqualTo(TimeSpan.FromMinutes(1)));
    }

    [Test]
    public void CreateExpressionFromString_LocationExpression_ReturnsCSharpReference()
    {
        var expr = CSharpExpressionHelper.CreateExpressionFromString("42", true, typeof(int));
        Assert.That(expr, Is.InstanceOf<CSharpReference<int>>());
    }

    [Test]
    public void TryCreateLiteral_Int_ReturnsTrue()
    {
        Assert.That(CSharpExpressionHelper.TryCreateLiteral(typeof(int), "42", out var expr), Is.True);
        Assert.That(expr, Is.InstanceOf<Literal<int>>());
        Assert.That(((Literal<int>)expr).Value, Is.EqualTo(42));
    }

    [Test]
    public void TryCreateLiteral_Char_ReturnsTrue()
    {
        Assert.That(CSharpExpressionHelper.TryCreateLiteral(typeof(char), "'a'", out var expr), Is.True);
        Assert.That(expr, Is.InstanceOf<Literal<char>>());
        Assert.That(((Literal<char>)expr).Value, Is.EqualTo('a'));
    }

    [Test]
    public void TryCreateLiteral_QuotedString_ReturnsTrue()
    {
        Assert.That(CSharpExpressionHelper.TryCreateLiteral(typeof(string), "\"hi\"", out var expr), Is.True);
        Assert.That(((Literal<string>)expr).Value, Is.EqualTo("hi"));
    }

    [Test]
    public void TryCreateLiteral_UnquotedString_ReturnsFalse()
    {
        Assert.That(CSharpExpressionHelper.TryCreateLiteral(typeof(string), "hi", out _), Is.False);
    }

    [Test]
    public void TryCreateLiteral_BoolCaseMismatch_ReturnsFalse()
    {
        Assert.That(CSharpExpressionHelper.TryCreateLiteral(typeof(bool), "True", out _), Is.False);
    }

    [Test]
    public void TryCreateLiteral_InvalidInt_ReturnsFalse()
    {
        Assert.That(CSharpExpressionHelper.TryCreateLiteral(typeof(int), "abc", out _), Is.False);
    }

    [Test]
    public void TryCreateLiteral_CallResult_Int_Succeeds()
    {
        var result = CSharpExpressionHelper.TryCreateLiteral(typeof(int), "42");
        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public void TryCreateLiteral_CallResult_UnquotedString_FailsWithMessage()
    {
        var result = CSharpExpressionHelper.TryCreateLiteral(typeof(string), "hi");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Is.EqualTo("Не удалось преобразовать выражение к нужному типу"));
    }
}
