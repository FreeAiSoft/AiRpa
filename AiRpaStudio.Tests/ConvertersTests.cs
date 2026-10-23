using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using RPA.Converters;

namespace AiRpaStudio.Tests;

public class ConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Test]
    public void DocumentTitleConverter_NullValues_ReturnsEmptyString()
    {
        var c = new DocumentTitleConverter();
        Assert.That(c.Convert(null, typeof(string), null, Culture), Is.EqualTo(string.Empty));
    }

    [Test]
    public void DocumentTitleConverter_TooFewValues_ReturnsEmptyString()
    {
        var c = new DocumentTitleConverter();
        Assert.That(c.Convert(new object[] { "a.xaml" }, typeof(string), null, Culture), Is.EqualTo(string.Empty));
    }

    [Test]
    public void DocumentTitleConverter_EmptyFileName_ReturnsEmptyString()
    {
        var c = new DocumentTitleConverter();
        Assert.That(c.Convert(new object[] { "", false }, typeof(string), null, Culture), Is.EqualTo(string.Empty));
    }

    [Test]
    public void DocumentTitleConverter_NotEdited_ReturnsFileName()
    {
        var c = new DocumentTitleConverter();
        Assert.That(c.Convert(new object[] { "Main.xaml", false }, typeof(string), null, Culture), Is.EqualTo("Main.xaml"));
    }

    [Test]
    public void DocumentTitleConverter_Edited_AppendsAsterisk()
    {
        var c = new DocumentTitleConverter();
        Assert.That(c.Convert(new object[] { "Main.xaml", true }, typeof(string), null, Culture), Is.EqualTo("Main.xaml *"));
    }

    [Test]
    public void DocumentTitleConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new DocumentTitleConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack("x", Array.Empty<Type>(), null, Culture));
    }

    [Test]
    public void FileExtensionConverter_MatchingExtension_ReturnsTrue()
    {
        var c = new FileExtensionConverter();
        Assert.That(c.Convert("Main.xaml", typeof(bool), ".xaml", Culture), Is.EqualTo(true));
    }

    [Test]
    public void FileExtensionConverter_MatchingExtension_CaseInsensitive()
    {
        var c = new FileExtensionConverter();
        Assert.That(c.Convert("Main.xaml", typeof(bool), ".XAML", Culture), Is.EqualTo(true));
    }

    [Test]
    public void FileExtensionConverter_NonMatchingExtension_ReturnsFalse()
    {
        var c = new FileExtensionConverter();
        Assert.That(c.Convert("Main.xml", typeof(bool), ".xaml", Culture), Is.EqualTo(false));
    }

    [Test]
    public void FileExtensionConverter_NonStringValue_ReturnsFalse()
    {
        var c = new FileExtensionConverter();
        Assert.That(c.Convert(123, typeof(bool), ".xaml", Culture), Is.EqualTo(false));
    }

    [Test]
    public void FileExtensionConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new FileExtensionConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(true, typeof(string), ".xaml", Culture));
    }

    [Test]
    public void InverseBooleanToVisibilityConverter_True_ReturnsCollapsed()
    {
        var c = new InverseBooleanToVisibilityConverter();
        Assert.That(c.Convert(true, typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void InverseBooleanToVisibilityConverter_False_ReturnsVisible()
    {
        var c = new InverseBooleanToVisibilityConverter();
        Assert.That(c.Convert(false, typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void InverseBooleanToVisibilityConverter_NonBool_ReturnsVisible()
    {
        var c = new InverseBooleanToVisibilityConverter();
        Assert.That(c.Convert("x", typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void InverseBooleanToVisibilityConverter_ConvertBack_Visible_ReturnsFalse()
    {
        var c = new InverseBooleanToVisibilityConverter();
        Assert.That(c.ConvertBack(Visibility.Visible, typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void InverseBooleanToVisibilityConverter_ConvertBack_Collapsed_ReturnsTrue()
    {
        var c = new InverseBooleanToVisibilityConverter();
        Assert.That(c.ConvertBack(Visibility.Collapsed, typeof(bool), null, Culture), Is.EqualTo(true));
    }

    [Test]
    public void InverseBooleanToVisibilityConverter_ConvertBack_NonVisibility_ReturnsFalse()
    {
        var c = new InverseBooleanToVisibilityConverter();
        Assert.That(c.ConvertBack("x", typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void MultiConverter_Convert_ReturnsListOfValues()
    {
        var c = new MultiConverter();
        object result = c.Convert(new object[] { 1, "two" }, typeof(object), null, Culture);
        Assert.That(result, Is.InstanceOf<IList>());
        var list = (IList)result;
        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0], Is.EqualTo(1));
        Assert.That(list[1], Is.EqualTo("two"));
    }

    [Test]
    public void MultiConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new MultiConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, Array.Empty<Type>(), null, Culture));
    }

    [Test]
    public void TypeVisibilityCheck_MatchingType_ReturnsVisible()
    {
        var c = new TypeVisibilityCheck();
        Assert.That(c.Convert("abc", typeof(Visibility), typeof(string), Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void TypeVisibilityCheck_NonMatchingType_ReturnsCollapsed()
    {
        var c = new TypeVisibilityCheck();
        Assert.That(c.Convert(123, typeof(Visibility), typeof(string), Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void TypeVisibilityCheck_ConvertBack_ThrowsNotImplemented()
    {
        var c = new TypeVisibilityCheck();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, typeof(Visibility), null, Culture));
    }

    [Test]
    public void IsStoppedConverter_Null_ReturnsFalse()
    {
        var c = new IsStoppedConverter();
        Assert.That(c.Convert(null, typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void IsStoppedConverter_NonDesigner_ReturnsFalse()
    {
        var c = new IsStoppedConverter();
        Assert.That(c.Convert("x", typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void CanRunConverter_Null_ReturnsFalse()
    {
        var c = new CanRunConverter();
        Assert.That(c.Convert(null, typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void CanRunConverter_NonDesigner_ReturnsFalse()
    {
        var c = new CanRunConverter();
        Assert.That(c.Convert("x", typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void CanPauseConverter_Null_ReturnsFalse()
    {
        var c = new CanPauseConverter();
        Assert.That(c.Convert(null, typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void CanStopConverter_Null_ReturnsFalse()
    {
        var c = new CanStopConverter();
        Assert.That(c.Convert(null, typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void IsStoppedConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new IsStoppedConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, typeof(bool), null, Culture));
    }
}
