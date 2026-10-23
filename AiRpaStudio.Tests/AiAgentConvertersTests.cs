using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RPA.AiAgent.Converters;
using RPA.AiAgent.Models;

namespace AiRpaStudio.Tests;

public class AiAgentConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Test]
    public void BoolToVisibilityConverter_True_ReturnsVisible()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.Convert(true, typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void BoolToVisibilityConverter_False_ReturnsCollapsed()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.Convert(false, typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void BoolToVisibilityConverter_True_WithInverse_ReturnsCollapsed()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.Convert(true, typeof(Visibility), "Inverse", Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void BoolToVisibilityConverter_False_WithInverse_ReturnsVisible()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.Convert(false, typeof(Visibility), "Inverse", Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void BoolToVisibilityConverter_NonBool_ReturnsCollapsed()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.Convert("x", typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void BoolToVisibilityConverter_ConvertBack_Visible_ReturnsTrue()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.ConvertBack(Visibility.Visible, typeof(bool), null, Culture), Is.EqualTo(true));
    }

    [Test]
    public void BoolToVisibilityConverter_ConvertBack_Collapsed_ReturnsFalse()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.ConvertBack(Visibility.Collapsed, typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void BoolToVisibilityConverter_ConvertBack_Visible_WithInverse_ReturnsFalse()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.ConvertBack(Visibility.Visible, typeof(bool), "Inverse", Culture), Is.EqualTo(false));
    }

    [Test]
    public void BoolToVisibilityConverter_ConvertBack_NonVisibility_ReturnsFalse()
    {
        var c = new BoolToVisibilityConverter();
        Assert.That(c.ConvertBack("x", typeof(bool), null, Culture), Is.EqualTo(false));
    }

    [Test]
    public void BoolToColorConverter_True_ReturnsTrueColor()
    {
        var c = new BoolToColorConverter();
        var brush = (SolidColorBrush)c.Convert(true, typeof(Brush), "#4CAF50|#999999", Culture);
        Assert.That(brush.Color, Is.EqualTo(Color.FromRgb(0x4C, 0xAF, 0x50)));
    }

    [Test]
    public void BoolToColorConverter_False_ReturnsFalseColor()
    {
        var c = new BoolToColorConverter();
        var brush = (SolidColorBrush)c.Convert(false, typeof(Brush), "#4CAF50|#999999", Culture);
        Assert.That(brush.Color, Is.EqualTo(Color.FromRgb(0x99, 0x99, 0x99)));
    }

    [Test]
    public void BoolToColorConverter_MalformedParameter_ReturnsTransparent()
    {
        var c = new BoolToColorConverter();
        var brush = (SolidColorBrush)c.Convert(true, typeof(Brush), "singleColor", Culture);
        Assert.That(brush.Color, Is.EqualTo(Colors.Transparent));
    }

    [Test]
    public void BoolToColorConverter_NonBool_ReturnsTransparent()
    {
        var c = new BoolToColorConverter();
        var brush = (SolidColorBrush)c.Convert("x", typeof(Brush), "#4CAF50|#999999", Culture);
        Assert.That(brush.Color, Is.EqualTo(Colors.Transparent));
    }

    [Test]
    public void BoolToColorConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new BoolToColorConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, typeof(bool), null, Culture));
    }

    [Test]
    public void MessageAlignmentConverter_User_ReturnsRight()
    {
        var c = new MessageAlignmentConverter();
        Assert.That(c.Convert("user", typeof(HorizontalAlignment), null, Culture), Is.EqualTo(HorizontalAlignment.Right));
    }

    [Test]
    public void MessageAlignmentConverter_Assistant_ReturnsLeft()
    {
        var c = new MessageAlignmentConverter();
        Assert.That(c.Convert("assistant", typeof(HorizontalAlignment), null, Culture), Is.EqualTo(HorizontalAlignment.Left));
    }

    [Test]
    public void MessageAlignmentConverter_System_ReturnsCenter()
    {
        var c = new MessageAlignmentConverter();
        Assert.That(c.Convert("system", typeof(HorizontalAlignment), null, Culture), Is.EqualTo(HorizontalAlignment.Center));
    }

    [Test]
    public void MessageAlignmentConverter_Unknown_ReturnsLeft()
    {
        var c = new MessageAlignmentConverter();
        Assert.That(c.Convert("unknown", typeof(HorizontalAlignment), null, Culture), Is.EqualTo(HorizontalAlignment.Left));
        Assert.That(c.Convert(123, typeof(HorizontalAlignment), null, Culture), Is.EqualTo(HorizontalAlignment.Left));
    }

    [Test]
    public void MessageAlignmentConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new MessageAlignmentConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, typeof(HorizontalAlignment), null, Culture));
    }

    [Test]
    public void MessageBackgroundConverter_User_ReturnsLightBlue()
    {
        var c = new MessageBackgroundConverter();
        var brush = (SolidColorBrush)c.Convert("user", typeof(Brush), null, Culture);
        Assert.That(brush.Color, Is.EqualTo(Color.FromRgb(220, 237, 255)));
    }

    [Test]
    public void MessageBackgroundConverter_Assistant_ReturnsLightGray()
    {
        var c = new MessageBackgroundConverter();
        var brush = (SolidColorBrush)c.Convert("assistant", typeof(Brush), null, Culture);
        Assert.That(brush.Color, Is.EqualTo(Color.FromRgb(240, 240, 240)));
    }

    [Test]
    public void MessageBackgroundConverter_System_ReturnsLightYellow()
    {
        var c = new MessageBackgroundConverter();
        var brush = (SolidColorBrush)c.Convert("system", typeof(Brush), null, Culture);
        Assert.That(brush.Color, Is.EqualTo(Color.FromRgb(255, 243, 205)));
    }

    [Test]
    public void MessageBackgroundConverter_Unknown_ReturnsWhite()
    {
        var c = new MessageBackgroundConverter();
        var brush = (SolidColorBrush)c.Convert("unknown", typeof(Brush), null, Culture);
        Assert.That(brush.Color, Is.EqualTo(Colors.White));
    }

    [Test]
    public void MessageBackgroundConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new MessageBackgroundConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, typeof(Brush), null, Culture));
    }

    [Test]
    public void NullToVisibilityConverter_Null_ReturnsCollapsed()
    {
        var c = new NullToVisibilityConverter();
        Assert.That(c.Convert(null, typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void NullToVisibilityConverter_NonNull_ReturnsVisible()
    {
        var c = new NullToVisibilityConverter();
        Assert.That(c.Convert("x", typeof(Visibility), null, Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void NullToVisibilityConverter_Null_WithInverse_ReturnsVisible()
    {
        var c = new NullToVisibilityConverter();
        Assert.That(c.Convert(null, typeof(Visibility), "Inverse", Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void NullToVisibilityConverter_NonNull_WithInverse_ReturnsCollapsed()
    {
        var c = new NullToVisibilityConverter();
        Assert.That(c.Convert("x", typeof(Visibility), "Inverse", Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void NullToVisibilityConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new NullToVisibilityConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, typeof(bool), null, Culture));
    }

    [Test]
    public void ProviderTypeToVisibilityConverter_MatchingProvider_ReturnsVisible()
    {
        var c = new ProviderTypeToVisibilityConverter();
        Assert.That(c.Convert(ProviderType.Ollama, typeof(Visibility), "Ollama", Culture), Is.EqualTo(Visibility.Visible));
    }

    [Test]
    public void ProviderTypeToVisibilityConverter_NonMatchingProvider_ReturnsCollapsed()
    {
        var c = new ProviderTypeToVisibilityConverter();
        Assert.That(c.Convert(ProviderType.OpenRouter, typeof(Visibility), "Ollama", Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ProviderTypeToVisibilityConverter_NonProviderValue_ReturnsCollapsed()
    {
        var c = new ProviderTypeToVisibilityConverter();
        Assert.That(c.Convert("x", typeof(Visibility), "Ollama", Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ProviderTypeToVisibilityConverter_InvalidParameter_ReturnsCollapsed()
    {
        var c = new ProviderTypeToVisibilityConverter();
        Assert.That(c.Convert(ProviderType.Ollama, typeof(Visibility), "NotAProvider", Culture), Is.EqualTo(Visibility.Collapsed));
    }

    [Test]
    public void ProviderTypeToVisibilityConverter_ConvertBack_ThrowsNotImplemented()
    {
        var c = new ProviderTypeToVisibilityConverter();
        Assert.Throws<NotImplementedException>(() => c.ConvertBack(null, typeof(Visibility), null, Culture));
    }

    [Test]
    public void MessageContentParser_Null_ReturnsEmpty()
    {
        Assert.That(MessageContentParser.Parse(null), Is.Empty);
    }

    [Test]
    public void MessageContentParser_EmptyString_ReturnsEmpty()
    {
        Assert.That(MessageContentParser.Parse(string.Empty), Is.Empty);
    }

    [Test]
    public void MessageContentParser_PlainText_ReturnsSingleTextPart()
    {
        List<MessageContentPart> parts = MessageContentParser.Parse("just text");
        Assert.That(parts.Count, Is.EqualTo(1));
        Assert.That(parts[0].Type, Is.EqualTo(ContentPartType.Text));
        Assert.That(parts[0].Content, Is.EqualTo("just text"));
        Assert.That(parts[0].Language, Is.Null);
    }

    [Test]
    public void MessageContentParser_CodeBlock_ReturnsCodePartWithLanguage()
    {
        List<MessageContentPart> parts = MessageContentParser.Parse("```json\n{\"a\":1}\n```");
        Assert.That(parts.Count, Is.EqualTo(1));
        Assert.That(parts[0].Type, Is.EqualTo(ContentPartType.CodeBlock));
        Assert.That(parts[0].Language, Is.EqualTo("json"));
        Assert.That(parts[0].Content, Is.EqualTo("{\"a\":1}"));
    }

    [Test]
    public void MessageContentParser_CodeBlockWithoutLanguage_UsesTextLanguage()
    {
        List<MessageContentPart> parts = MessageContentParser.Parse("```\ncode\n```");
        Assert.That(parts.Count, Is.EqualTo(1));
        Assert.That(parts[0].Type, Is.EqualTo(ContentPartType.CodeBlock));
        Assert.That(parts[0].Language, Is.EqualTo("text"));
        Assert.That(parts[0].Content, Is.EqualTo("code"));
    }

    [Test]
    public void MessageContentParser_MixedContent_ReturnsTextAndCodeParts()
    {
        List<MessageContentPart> parts = MessageContentParser.Parse("Intro\n```json\n{\"a\":1}\n```\nOutro");
        Assert.That(parts.Count, Is.EqualTo(3));
        Assert.That(parts[0].Type, Is.EqualTo(ContentPartType.Text));
        Assert.That(parts[0].Content, Is.EqualTo("Intro"));
        Assert.That(parts[1].Type, Is.EqualTo(ContentPartType.CodeBlock));
        Assert.That(parts[1].Language, Is.EqualTo("json"));
        Assert.That(parts[1].Content, Is.EqualTo("{\"a\":1}"));
        Assert.That(parts[2].Type, Is.EqualTo(ContentPartType.Text));
        Assert.That(parts[2].Content, Is.EqualTo("Outro"));
    }
}
