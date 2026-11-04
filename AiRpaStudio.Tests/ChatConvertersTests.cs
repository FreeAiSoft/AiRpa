using System.Globalization;
using System.Threading;
using System.Windows.Controls;
using RPA.Chat.Converters;
using Agent.Core.Services;

namespace AiRpaStudio.Tests;

public class ChatConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Test]
    public void OptionEqualsAnswerConverter_Match_ReturnsTrue()
    {
        var c = new OptionEqualsAnswerConverter();
        Assert.That(c.Convert(new object[] { "Yes", "Yes" }, typeof(bool), null, Culture), Is.True);
    }

    [Test]
    public void OptionEqualsAnswerConverter_Mismatch_ReturnsFalse()
    {
        var c = new OptionEqualsAnswerConverter();
        Assert.That(c.Convert(new object[] { "Yes", "No" }, typeof(bool), null, Culture), Is.False);
    }

    [Test]
    public void OptionEqualsAnswerConverter_EmptyAnswer_ReturnsFalse()
    {
        var c = new OptionEqualsAnswerConverter();
        Assert.That(c.Convert(new object[] { "Yes", "" }, typeof(bool), null, Culture), Is.False);
    }

    [Test]
    public void OptionEqualsAnswerConverter_NullValues_ReturnsFalse()
    {
        var c = new OptionEqualsAnswerConverter();
        Assert.That(c.Convert(null, typeof(bool), null, Culture), Is.False);
        Assert.That(c.Convert(new object[] { "Yes" }, typeof(bool), null, Culture), Is.False);
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CodeTokensToTextBlockConverter_ReturnsTextBlockWithInlines()
    {
        var c = new CodeTokensToTextBlockConverter();
        var tokens = CSharpHighlighter.Highlight("static void Main()");
        var result = c.Convert(tokens, typeof(TextBlock), null, Culture);
        Assert.That(result, Is.InstanceOf<TextBlock>());
        Assert.That(((TextBlock)result).Inlines.Count, Is.GreaterThan(0));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void CodeTokensToTextBlockConverter_NonTokenValue_ReturnsEmptyTextBlock()
    {
        var c = new CodeTokensToTextBlockConverter();
        var result = c.Convert("not tokens", typeof(TextBlock), null, Culture);
        Assert.That(result, Is.InstanceOf<TextBlock>());
        Assert.That(((TextBlock)result).Inlines.Count, Is.EqualTo(0));
    }
}
