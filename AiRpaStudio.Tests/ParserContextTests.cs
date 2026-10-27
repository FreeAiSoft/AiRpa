using System;
using System.Activities;
using System.ComponentModel;
using System.Xaml;
using RPA.CSharpExpressionEditor;

namespace AiRpaStudio.Tests;

public class ParserContextTests
{
    [Test]
    public void DefaultCtor_DoesNotThrow()
    {
        Assert.That(new ParserContext(), Is.Not.Null);
    }

    [Test]
    public void Initialize_Null_ReturnsFalse()
    {
        Assert.That(new ParserContext().Initialize(null), Is.False);
    }

    [Test]
    public void LookupPrefix_CreatesAndCachesPrefix()
    {
        var ctx = new ParserContext();
        string prefix = ctx.LookupPrefix("urn:test");
        Assert.That(prefix, Does.StartWith("__"));
        Assert.That(ctx.LookupPrefix("urn:test"), Is.EqualTo(prefix));
        Assert.That(ctx.LookupPrefix("urn:other"), Is.Not.EqualTo(prefix));
    }

    [Test]
    public void GetService_ReturnsSelfForXamlInterfaces()
    {
        var ctx = new ParserContext();
        Assert.That(ctx.GetService(typeof(IXamlNameResolver)), Is.SameAs(ctx));
        Assert.That(ctx.GetService(typeof(INamespacePrefixLookup)), Is.SameAs(ctx));
        Assert.That(ctx.GetService(typeof(IXamlNamespaceResolver)), Is.SameAs(ctx));
        Assert.That(ctx.GetService(typeof(object)), Is.Null);
    }

    [Test]
    public void GetValueSerializerFor_ReturnsNull()
    {
        var ctx = new ParserContext();
        Assert.That(ctx.GetValueSerializerFor(typeof(int)), Is.Null);
        Assert.That(ctx.GetValueSerializerFor((PropertyDescriptor)null), Is.Null);
    }

    [Test]
    public void GetFixupToken_ReturnsNull()
    {
        var ctx = new ParserContext();
        Assert.That(ctx.GetFixupToken(new[] { "x" }), Is.Null);
        Assert.That(ctx.GetFixupToken(new[] { "x" }, true), Is.Null);
    }

    [Test]
    public void OnComponentChanging_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => new ParserContext().OnComponentChanging());
    }

    [Test]
    public void OnComponentChanged_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => new ParserContext().OnComponentChanged());
    }

    [Test]
    public void Defaults_ContainerNullRootNullFixupFalse()
    {
        var ctx = new ParserContext();
        Assert.That(ctx.Container, Is.Null);
        Assert.That(ctx.Root, Is.Null);
        Assert.That(ctx.IsFixupTokenAvailable, Is.False);
    }
}
