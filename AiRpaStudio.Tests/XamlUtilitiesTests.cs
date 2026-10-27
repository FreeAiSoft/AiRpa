using System;
using System.ComponentModel;
using RPA.CSharpExpressionEditor;

namespace AiRpaStudio.Tests;

public class XamlUtilitiesTests
{
    [Test]
    public void GetConverter_Int_ReturnsInt32Converter()
    {
        Assert.That(XamlUtilities.GetConverter(typeof(int)), Is.InstanceOf<Int32Converter>());
    }

    [Test]
    public void GetConverter_String_ReturnsStringConverter()
    {
        Assert.That(XamlUtilities.GetConverter(typeof(string)), Is.InstanceOf<StringConverter>());
    }

    [Test]
    public void GetConverter_Bool_ReturnsBooleanConverter()
    {
        Assert.That(XamlUtilities.GetConverter(typeof(bool)), Is.InstanceOf<BooleanConverter>());
    }

    [Test]
    public void GetConverter_DateTime_ReturnsDateTimeConverter()
    {
        Assert.That(XamlUtilities.GetConverter(typeof(DateTime)), Is.InstanceOf<DateTimeConverter>());
    }

    [Test]
    public void GetConverter_Object_ReturnsBaseConverter()
    {
        var converter = XamlUtilities.GetConverter(typeof(object));
        Assert.That(converter, Is.Not.Null);
        Assert.That(converter.GetType(), Is.EqualTo(typeof(TypeConverter)));
    }
}
