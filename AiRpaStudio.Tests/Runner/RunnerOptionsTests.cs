using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using RPA.Runner;

namespace AiRpaStudio.Tests.Runner;

/// <summary>
/// `OR-02`, шаг `W2` — разбор командной строки `RpaRunner.exe`.
/// <para>
/// Проверяется не «красивый API», а контракт исполнения: источник указывается один раз,
/// <c>--args</c> обязан быть JSON-объектом (иначе это ошибка запускающего, а не робота),
/// а таблица кодов возврата не может разойтись со справкой.
/// </para>
/// </summary>
[TestFixture]
public class RunnerOptionsTests
{
    [Test]
    public void Parse_ProjectWithArguments_ReadsEveryValue()
    {
        RunnerOptions options = Parse(
            "--project", @"C:\robot\Robot.csproj",
            "--args", "{\"InputText\":\"max\"}",
            "--run-id", "nightly-42",
            "--out", @"C:\out\result.json",
            "--log", @"C:\out\run.jsonl",
            "--timeout", "30");

        Assert.Multiple(() =>
        {
            Assert.That(options.ProjectPath, Is.EqualTo(@"C:\robot\Robot.csproj"));
            Assert.That(options.RunId, Is.EqualTo("nightly-42"));
            Assert.That(options.ResultPath, Is.EqualTo(@"C:\out\result.json"));
            Assert.That(options.LogPath, Is.EqualTo(@"C:\out\run.jsonl"));
            Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That(options.ValidateOnly, Is.False);
            Assert.That(options.Arguments["InputText"], Is.EqualTo("max"));
        });
    }

    [Test]
    public void Parse_JsonArguments_ConvertsScalarsToClrTypes()
    {
        RunnerOptions options = Parse(
            "--workflow", @"C:\robot\Main.xaml",
            "--args", "{\"text\":\"max\",\"count\":5,\"huge\":9000000000,\"ratio\":1.5,\"flag\":true}");

        Assert.Multiple(() =>
        {
            Assert.That(options.Arguments["text"], Is.TypeOf<string>());
            Assert.That(options.Arguments["count"], Is.EqualTo(5).And.TypeOf<int>());
            Assert.That(options.Arguments["huge"], Is.EqualTo(9000000000L).And.TypeOf<long>());
            Assert.That(options.Arguments["ratio"], Is.EqualTo(1.5).And.TypeOf<double>());
            Assert.That(options.Arguments["flag"], Is.EqualTo(true).And.TypeOf<bool>());
        });
    }

    [Test]
    public void Parse_NamedArgument_WorksWithoutQuotes()
    {
        RunnerOptions options = Parse(
            "--workflow", @"C:\robot\Main.xaml",
            "--arg", "InputText=max",
            "--arg", "Count=3",
            "--arg", "Flag=true");

        Assert.Multiple(() =>
        {
            Assert.That(options.Arguments["InputText"], Is.EqualTo("max"));
            Assert.That(options.Arguments["Count"], Is.EqualTo(3));
            Assert.That(options.Arguments["Flag"], Is.EqualTo(true));
        });
    }

    [Test]
    public void Parse_NamedArgumentWithoutEquals_IsError()
    {
        AssertRejected(new[] { "--workflow", @"C:\robot\Main.xaml", "--arg", "InputText" }, "имя=значение");
    }

    [Test]
    public void Parse_NoSource_IsError()
    {
        AssertRejected(Array.Empty<string>(), "источник прогона");
        AssertRejected(new[] { "--run-id", "x" }, "источник прогона");
    }

    [Test]
    public void Parse_TwoSources_IsError()
    {
        AssertRejected(
            new[] { "--project", @"C:\robot\Robot.csproj", "--workflow", @"C:\robot\Main.xaml" },
            "один раз");
    }

    [Test]
    public void Parse_AssemblyWithoutType_IsError()
    {
        AssertRejected(new[] { "--assembly", @"C:\robot\Robot.dll" }, "--type");
    }

    [Test]
    public void Parse_UnknownOption_IsError()
    {
        AssertRejected(new[] { "--workflow", @"C:\robot\Main.xaml", "--build" }, "неизвестный параметр");
    }

    [Test]
    public void Parse_MissingValue_IsError()
    {
        AssertRejected(new[] { "--workflow" }, "нет значения");
    }

    [Test]
    public void Parse_NotJsonArguments_IsError()
    {
        // Именно так приходит `--args` из PowerShell: оболочка съедает кавычки.
        AssertRejected(new[] { "--workflow", @"C:\robot\Main.xaml", "--args", "{InputText:max}" }, "JSON");
    }

    [Test]
    public void Parse_BadTimeout_IsError()
    {
        AssertRejected(new[] { "--workflow", @"C:\robot\Main.xaml", "--timeout", "0" }, "секунд");
        AssertRejected(new[] { "--workflow", @"C:\robot\Main.xaml", "--timeout", "abc" }, "секунд");
    }

    [Test]
    public void Parse_Help_DoesNotRequireSource()
    {
        Assert.That(RunnerOptions.TryParse(new[] { "--help" }, out RunnerOptions options, out string error), Is.True, error);
        Assert.That(options.ShowHelp, Is.True);
    }

    [Test]
    public void ResolveRunId_Empty_GeneratesIdentifiableValue()
    {
        RunnerOptions options = Parse("--workflow", @"C:\robot\Main.xaml");

        string runId = options.ResolveRunId();

        Assert.That(runId, Is.Not.Empty);
        Assert.That(runId, Does.Match(@"^\d{8}-\d{6}-[0-9a-f]{8}$"), "сгенерированный run-id обязан быть сортируемым и различимым");
    }

    [Test]
    public void ExitCodes_HaveFixedNumbersAndDescriptions()
    {
        var expected = new Dictionary<RunnerExitCode, int>
        {
            { RunnerExitCode.Success, 0 },
            { RunnerExitCode.Faulted, 1 },
            { RunnerExitCode.BadArguments, 2 },
            { RunnerExitCode.Cancelled, 3 },
            { RunnerExitCode.TimedOut, 4 },
            { RunnerExitCode.LoadFailed, 5 },
            { RunnerExitCode.RobotNotFound, 6 }
        };

        Assert.Multiple(() =>
        {
            foreach (KeyValuePair<RunnerExitCode, int> pair in expected)
            {
                Assert.That((int)pair.Key, Is.EqualTo(pair.Value),
                    $"код «{pair.Key}» изменился: оркестратор (OR-05) рассчитывает на таблицу кодов");
                Assert.That(RunnerExitCodes.Describe(pair.Key), Is.Not.Empty);
            }

            Assert.That(RunnerExitCodes.All.Count, Is.EqualTo(expected.Count), "все коды обязаны быть в списке");
        });
    }

    [Test]
    public void Help_ListsEveryExitCode()
    {
        string help = RunnerUsage.Text;

        Assert.Multiple(() =>
        {
            foreach (RunnerExitCode code in RunnerExitCodes.All)
            {
                Assert.That(help, Does.Contain("  " + (int)code + " — " + RunnerExitCodes.Describe(code)),
                    $"код {(int)code} не описан в справке: справка и таблица кодов обязаны совпадать");
            }

            foreach (string option in new[] { "--project", "--workflow", "--assembly", "--type", "--bin", "--args", "--arg", "--run-id", "--out", "--log", "--timeout", "--validate" })
            {
                Assert.That(help, Does.Contain(option), $"параметр «{option}» не описан в справке");
            }
        });
    }

    private static RunnerOptions Parse(params string[] args)
    {
        bool parsed = RunnerOptions.TryParse(args, out RunnerOptions options, out string error);

        Assert.That(parsed, Is.True, "аргументы обязаны разбираться: " + error);

        return options;
    }

    private static void AssertRejected(IList<string> args, string expectedInError)
    {
        bool parsed = RunnerOptions.TryParse(args, out _, out string error);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False, "аргументы обязаны быть отклонены");
            Assert.That(error, Does.Contain(expectedInError), "текст ошибки обязан объяснять причину");
        });
    }
}
