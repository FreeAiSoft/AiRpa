using Agent.Core.AgentTools;
using NUnit.Framework;

namespace AiRpaStudio.Tests
{
    /// <summary>
    /// AG-11: терпимость нормализатора маркапа. Модель «печатает» вызов инструмента текстом
    /// (<c>&lt;invoke&gt;</c>/<c>&lt;parameter&gt;</c>, в том числе с юникод-разделителями DSML),
    /// и такой ответ обязан превращаться в обычный tool-call, а не уходить пользователю как текст.
    /// <para>
    /// Проверяется через публичный <see cref="AgentResponseParser"/>: тесты не лезут во внутренний
    /// <c>DsmlNormalizer</c>, поэтому нормализатор можно менять, не переписывая тесты.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DsmlToleranceTests
    {
        [Test]
        public void Parse_MarkupWithEmptyArguments_ReturnsCallWithEmptyArguments()
        {
            var content =
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">workflow_outline</parameter>" +
                "<parameter name=\"arguments\" string=\"false\"></parameter>" +
                "</invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.False);
                Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
                Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("workflow_outline"));
                Assert.That(result.ToolCalls[0].Arguments.HasValues, Is.False, "пустые аргументы — это {}, а не null");
            });
        }

        [Test]
        public void Parse_MarkupWithGarbageBeforeAndAfter_RecoversEveryCall()
        {
            var content =
                "Let me check the workflow first.\n" +
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">workflow_outline</parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{\"path\":\"Main.xaml\"}</parameter>" +
                "</invoke>\n" +
                "and one more:\n" +
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">workflow_validate</parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{\"path\":\"Main.xaml\"}</parameter>" +
                "</invoke>\n" +
                "Done - waiting for the results.";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.False);
                Assert.That(result.ToolCalls.Count, Is.EqualTo(2));
                Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("workflow_outline"));
                Assert.That(result.ToolCalls[1].ToolName, Is.EqualTo("workflow_validate"));
            });
        }

        [Test]
        public void Parse_MarkupWithQuotesInsideJsonArguments_KeepsValues()
        {
            var content =
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">argument_set</parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{\"argument\":\"Text\",\"expression\":\"\\\"Started: \\\" + Now\"}</parameter>" +
                "</invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.False);
                Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
                Assert.That(result.ToolCalls[0].Arguments.Value<string>("argument"), Is.EqualTo("Text"));
                Assert.That(result.ToolCalls[0].Arguments.Value<string>("expression"), Is.EqualTo("\"Started: \" + Now"));
            });
        }

        [Test]
        public void Parse_MarkupWithBrokenJsonArguments_StillReturnsCall()
        {
            var content =
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">workflow_outline</parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{\"path\": }</parameter>" +
                "</invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.False, "битые аргументы не должны прятать сам вызов");
                Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
                Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("workflow_outline"));
                Assert.That(result.ToolCalls[0].Arguments.HasValues, Is.False);
            });
        }

        [Test]
        public void Parse_MarkupWithoutToolParameter_ReturnsFinalWithOriginalText()
        {
            var content = "<invoke name=\"tool_call\"><parameter name=\"path\">Main.xaml</parameter></invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.True, "без имени инструмента вызов не восстанавливается");
                Assert.That(result.Text, Is.EqualTo(content), "непонятый маркап отдаётся как есть, а не теряется");
            });
        }

        [Test]
        public void Parse_MarkupWithGreaterThanInsideValue_KeepsWholeValue()
        {
            var content =
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">argument_set</parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{\"value\":\"a > b\"}</parameter>" +
                "</invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.False);
                Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
                Assert.That(result.ToolCalls[0].Arguments.Value<string>("value"), Is.EqualTo("a > b"));
            });
        }

        [Test]
        public void Parse_MarkupWithSpacesAroundToolName_IsTrimmed()
        {
            var content =
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">  workflow_validate  </parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{}</parameter>" +
                "</invoke>";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.False);
                Assert.That(result.ToolCalls[0].ToolName, Is.EqualTo("workflow_validate"));
            });
        }

        [Test]
        public void Parse_PlainTextWithoutMarkup_PassesThroughAsFinal()
        {
            const string content = "Готово: процесс проверен, ошибок нет.";

            var result = AgentResponseParser.Parse(content);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsFinal, Is.True);
                Assert.That(result.Text, Is.EqualTo(content));
            });
        }
    }
}
