using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.AgentTools;
using Agent.Core.Context;
using Agent.Core.Models;
using NUnit.Framework;

namespace AiRpaStudio.Tests
{
    /// <summary>
    /// AG-11: маркап, напечатанный моделью в Native-режиме, исполняется как вызов инструмента,
    /// а не показывается пользователю текстом.
    /// <para>
    /// В JSON-протоколе <see cref="AgentResponseParser"/> нормализует DSML/XML-маркап с самого начала,
    /// а ветка <see cref="ToolCallingMode.Native"/> проверяла только <c>response.ToolCalls</c>: если модель
    /// вместо нативного вызова печатала <c>&lt;invoke&gt;</c>, пользователь получал разметку в чат, работа
    /// не выполнялась, и агент «зависал» без объяснения.
    /// </para>
    /// </summary>
    [TestFixture]
    public class NativeMarkupRecoveryTests
    {
        [Test]
        public async Task RunAsync_NativeMarkupInsteadOfCall_ExecutesRecoveredCall()
        {
            var executed = new List<string>();
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("workflow_outline", "structure", "{\"path\":\"string\"}", mutates: false,
                (arguments, cancellationToken) =>
                {
                    executed.Add(arguments?.Value<string>("path") ?? "");
                    return Task.FromResult("root: Sequence");
                },
                permission: AgentPermissions.ReadFiles));

            var ai = new FakeAiProvider(
                new ChatResponse
                {
                    Answer = "<invoke name=\"tool_call\">" +
                             "<parameter name=\"tool\" string=\"true\">workflow_outline</parameter>" +
                             "<parameter name=\"arguments\" string=\"false\">{\"path\":\"Main.xaml\"}</parameter>" +
                             "</invoke>",
                    FinishReason = "stop"
                },
                new ChatResponse { Answer = "Готово: структура прочитана.", FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYS",
                new ContextBudget { MaxIterations = 4 }, "COMPACT", ToolCallingMode.Native);

            AgentRunResult result = await runner.RunAsync("прочитай структуру", AgentMode.Act, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(executed, Is.EqualTo(new[] { "Main.xaml" }),
                    "вызов из маркапа обязан выполниться с разобранными аргументами");
                Assert.That(result.FinalText, Is.EqualTo("Готово: структура прочитана."));
                Assert.That(ai.CallCount, Is.EqualTo(2), "после выполнения вызова агент обязан сделать второй запрос");
            });
        }

        [Test]
        public async Task RunAsync_NativeMarkupRecovery_IsLimitedAndThenShownAsText()
        {
            const string markup =
                "<invoke name=\"tool_call\">" +
                "<parameter name=\"tool\" string=\"true\">workflow_outline</parameter>" +
                "<parameter name=\"arguments\" string=\"false\">{}</parameter>" +
                "</invoke>";

            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("workflow_outline", "structure", "{}", mutates: false,
                (arguments, cancellationToken) => Task.FromResult("root: Sequence"),
                permission: AgentPermissions.ReadFiles));

            var ai = new FakeAiProvider(
                new ChatResponse { Answer = markup, FinishReason = "stop" },
                new ChatResponse { Answer = markup, FinishReason = "stop" },
                new ChatResponse { Answer = markup, FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYS",
                new ContextBudget { MaxIterations = 6 }, "COMPACT", ToolCallingMode.Native);

            AgentRunResult result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(ai.CallCount, Is.EqualTo(3),
                    "восстановление ограничено двумя попытками, третья обязана вернуть ответ, а не крутиться вечно");
                Assert.That(result.FinalText, Does.Contain("<invoke"),
                    "непоправимый маркап отдаётся пользователю как есть — терять текст ответа нельзя");
            });
        }

        [Test]
        public async Task RunAsync_NativePlainAnswer_IsReturnedAsFinalWithoutRecovery()
        {
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("workflow_outline", "structure", "{}", mutates: false,
                (arguments, cancellationToken) => Task.FromResult("root: Sequence"),
                permission: AgentPermissions.ReadFiles));

            var ai = new FakeAiProvider(new ChatResponse { Answer = "Ошибок нет.", FinishReason = "stop" });
            var runner = new AgentRunner(ai, registry, mode => "SYS",
                new ContextBudget { MaxIterations = 4 }, "COMPACT", ToolCallingMode.Native);

            AgentRunResult result = await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(result.FinalText, Is.EqualTo("Ошибок нет."));
                Assert.That(ai.CallCount, Is.EqualTo(1), "обычный ответ не должен вызывать повторный запрос");
            });
        }

        [Test]
        public async Task RunAsync_NativeMarkupForMutatingToolInPlanMode_IsBlocked()
        {
            var executed = 0;
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("activity_insert", "insert", "{}", mutates: true,
                (arguments, cancellationToken) =>
                {
                    executed++;
                    return Task.FromResult("inserted");
                },
                permission: AgentPermissions.EditFiles));

            var ai = new FakeAiProvider(
                new ChatResponse
                {
                    Answer = "<invoke name=\"tool_call\">" +
                             "<parameter name=\"tool\" string=\"true\">activity_insert</parameter>" +
                             "<parameter name=\"arguments\" string=\"false\">{}</parameter>" +
                             "</invoke>",
                    FinishReason = "stop"
                },
                new ChatResponse { Answer = "В Plan правки запрещены.", FinishReason = "stop" });

            var runner = new AgentRunner(ai, registry, mode => "SYS",
                new ContextBudget { MaxIterations = 4 }, "COMPACT", ToolCallingMode.Native);

            AgentRunResult result = await runner.RunAsync("вставь WriteLine", AgentMode.Plan, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(executed, Is.Zero, "мутирующий вызов из маркапа обязан пройти ту же проверку режима");
                Assert.That(result.FinalText, Is.EqualTo("В Plan правки запрещены."));
            });
        }
    }

    /// <summary>
    /// AG-11: поведение контекста на нагрузке WF-инструментами. Структура процесса — самый большой ответ
    /// агента (десятки килобайт на реальном роботе), поэтому проверяется две вещи: усечение объясняет,
    /// как запросить меньше, а конденсация не выбрасывает свежий <c>workflow_outline</c> из окна.
    /// </summary>
    [TestFixture]
    public class WorkflowContextLoadTests
    {
        [Test]
        public async Task RunAsync_LongWorkflowOutline_TruncationCarriesNarrowingHint()
        {
            string bigOutline = new string('o', 20000);
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("workflow_outline", "structure", "{}", mutates: false,
                (arguments, cancellationToken) => Task.FromResult(bigOutline),
                permission: AgentPermissions.ReadFiles));

            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "{\"action\":\"tool_call\",\"tool\":\"workflow_outline\",\"arguments\":{}}", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var budget = new ContextBudget { MaxIterations = 4, ToolResultMaxChars = 12000 };
            var runner = new AgentRunner(ai, registry, mode => "SYS", budget, "COMPACT");

            await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            string toolMessage = ai.Calls[1].Last(message => message.Role == "tool" || message.Role == "user").Content;

            Assert.Multiple(() =>
            {
                Assert.That(toolMessage.Length, Is.LessThan(bigOutline.Length), "ответ обязан усекаться по бюджету");
                Assert.That(toolMessage, Does.Contain("…(truncated)"), "усечение обязано быть явным");
                Assert.That(toolMessage, Does.Contain("depth"),
                    "усечённая структура обязана подсказывать, как запросить меньше (иначе агент теряет дерево)");
                Assert.That(toolMessage, Does.Contain("include_arguments"));
            });
        }

        [Test]
        public async Task RunAsync_LongToolResult_EventCarriesFullResultForClipboard()
        {
            string big = new string('x', 20000);
            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("activity_search", "search", "{}", mutates: false,
                (arguments, cancellationToken) => Task.FromResult(big),
                permission: AgentPermissions.ReadFiles));

            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "{\"action\":\"tool_call\",\"tool\":\"activity_search\",\"arguments\":{}}", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"done\"}", FinishReason = "stop" });

            var budget = new ContextBudget { MaxIterations = 4, ToolResultMaxChars = 12000 };
            var runner = new AgentRunner(ai, registry, mode => "SYS", budget, "COMPACT");

            AgentToolCall executed = null;
            runner.ToolExecuted += (sender, args) => executed = args;

            await runner.RunAsync("go", AgentMode.Act, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(executed, Is.Not.Null);
                Assert.That(executed.Result, Does.Contain("…(truncated)"), "плашка/контекст остаются усечёнными");
                Assert.That(executed.Result.Length, Is.LessThan(big.Length));
                Assert.That(executed.FullResult, Is.EqualTo(big), "в буфер обмена попадает полный результат");
            });
        }

        [Test]
        public async Task RunAsync_BigOutlineHistory_CondensesAndKeepsRecentOutlineIntact()
        {
            string oldOutline = new string('a', 12000);
            string recentOutline = new string('b', 12000);

            var registry = new AgentToolRegistry();
            registry.Register(new AgentTool("workflow_validate", "validate", "{}", mutates: false,
                (arguments, cancellationToken) => Task.FromResult("no errors"),
                permission: AgentPermissions.ReadFiles));

            var ai = new FakeAiProvider(
                new ChatResponse { Answer = "SUMMARY", FinishReason = "stop" },
                new ChatResponse { Answer = "{\"action\":\"final\",\"text\":\"ok\"}", FinishReason = "stop" });

            var budget = new ContextBudget
            {
                ContextWindowSize = 2000,
                ReserveFraction = 0.1,
                CompactThresholdFraction = 0.5,
                KeepRecentMessages = 2,
                MaxIterations = 2
            };

            var runner = new AgentRunner(ai, registry, mode => "SYS", budget, "COMPACT");
            var compacted = false;
            runner.ContextCompacted += (sender, args) => compacted = true;

            var history = new List<ChatMessageData>
            {
                new ChatMessageData { Role = "tool", Content = "Tool \"workflow_outline\" returned:\n" + oldOutline },
                new ChatMessageData { Role = "tool", Content = "Tool \"workflow_outline\" returned:\n" + recentOutline }
            };

            AgentRunResult result = await runner.RunAsync(history, "проверь", AgentMode.Act, budget, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(compacted, Is.True, "большая история обязана вызвать конденсацию и событие ContextCompacted");
                Assert.That(result.FinalText, Is.EqualTo("ok"));
                Assert.That(ai.Calls[1].Any(message => message.Content == "Tool \"workflow_outline\" returned:\n" + recentOutline),
                    Is.True, "свежий workflow_outline обязан остаться в окне целиком, иначе править не по чему");
                Assert.That(ai.Calls[1].Any(message => message.Content.Contains(oldOutline)), Is.False,
                    "старая структура уходит в сводку, а не остаётся второй копией");
            });
        }
    }
}
