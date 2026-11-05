using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using RPA.Runner;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests.Runner;

/// <summary>
/// `OR-02`, шаги `W3`–`W5` — исполнение прогона раннером и коды возврата.
/// <para>
/// Каждый нештатный исход обязан иметь свой код: падение робота (1), отмена (3), таймаут (4),
/// ошибка загрузки (5), несобранный робот (6). Оркестратор (`OR-05`) и запускатор (`LN-02`)
/// по этому коду решают «повторить / отменить / починить робота», поэтому проверяется именно
/// код, а не текст сообщения.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowRunnerHostTests
{
    /// <summary>Эталон: Sequence → WriteLine → Assign, вход <c>InputText</c>, выход <c>Result</c>.</summary>
    private const string EchoXaml = @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
          xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
          xmlns:s=""clr-namespace:System;assembly=mscorlib""
          xmlns:sco=""clr-namespace:System.Collections.ObjectModel;assembly=mscorlib"">
  <x:Members>
    <x:Property Name=""InputText"" Type=""InArgument(x:String)"" />
    <x:Property Name=""Result"" Type=""OutArgument(x:String)"" />
  </x:Members>
  <TextExpression.NamespacesForImplementation>
    <sco:Collection x:TypeArguments=""x:String"">
      <x:String>System</x:String>
      <x:String>System.Text</x:String>
    </sco:Collection>
  </TextExpression.NamespacesForImplementation>
  <TextExpression.ReferencesForImplementation>
    <sco:Collection x:TypeArguments=""AssemblyReference"">
      <AssemblyReference>mscorlib</AssemblyReference>
      <AssemblyReference>System</AssemblyReference>
      <AssemblyReference>System.Core</AssemblyReference>
    </sco:Collection>
  </TextExpression.ReferencesForImplementation>
  <Sequence DisplayName=""Main sequence"">
    <WriteLine DisplayName=""Log input"" Text=""[InputText]"" />
    <Assign DisplayName=""Build result"">
      <Assign.To>
        <OutArgument x:TypeArguments=""x:String"">[Result]</OutArgument>
      </Assign.To>
      <Assign.Value>
        <InArgument x:TypeArguments=""x:String"">[InputText.ToUpper()]</InArgument>
      </Assign.Value>
    </Assign>
  </Sequence>
</Activity>";

    /// <summary>Сценарий без выхода: прогон обязан остановиться по пределу времени.</summary>
    private const string LongWaitXaml = @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Sequence DisplayName=""Waiting sequence"">
    <Delay DisplayName=""Long wait"" Duration=""00:10:00"" />
    <WriteLine Text=""после ожидания"" />
  </Sequence>
</Activity>";

    /// <summary>
    /// XAML загружается, но проверку WF не проходит: <c>Assign</c> без <c>To</c> и <c>Value</c>
    /// даёт две ошибки валидации. Именно такой случай обязан ловить <c>--validate</c> — если
    /// полагаться на загрузку, ошибка не будет найдена до прогона.
    /// </summary>
    private const string InvalidXaml = @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Sequence DisplayName=""Bad sequence"">
    <Assign DisplayName=""Assign without To and Value"" />
  </Sequence>
</Activity>";

    private string _root;

    [SetUp]
    public void SetUp()
    {
        _root = IOPath.Combine(IOPath.GetTempPath(), "runner_host_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Execute_WorkflowWithArgument_ReturnsZeroAndOutputs()
    {
        string workflow = WriteXaml("Echo.xaml", EchoXaml);

        RunnerOutcome outcome = Execute("--workflow", workflow, "--arg", "InputText=max");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.Success), outcome.Summary);
            Assert.That(outcome.State, Is.EqualTo("Completed"));
            Assert.That(outcome.Outputs["Result"], Is.EqualTo("MAX"));
            Assert.That(outcome.ErrorText, Is.Empty);
            Assert.That(outcome.Status, Is.EqualTo("success"));
        });
    }

    [Test]
    public void Execute_FaultedRobot_ReturnsFaultedOne()
    {
        string workflow = WriteXaml("Echo.xaml", EchoXaml);

        // InputText не передан: [InputText.ToUpper()] падает на null — настоящий сбой робота.
        RunnerOutcome outcome = Execute("--workflow", workflow);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.Faulted), outcome.Summary);
            Assert.That(outcome.ErrorText, Does.Contain("Exception"), "ошибка обязана быть со стеком");
            Assert.That(outcome.Status, Is.EqualTo("failed"));
        });
    }

    [Test]
    public void Execute_BrokenXaml_ReturnsLoadFailedFive()
    {
        string workflow = WriteXaml("Broken.xaml", "<Activity><Sequence DisplayName=\"нет закрывающего тега\">");

        RunnerOutcome outcome = Execute("--workflow", workflow);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.LoadFailed), outcome.Summary);
            Assert.That(outcome.ErrorText, Is.Not.Empty);
            Assert.That(outcome.Status, Is.EqualTo("load-failed"));
        });
    }

    [Test]
    public void Execute_ValidateOnly_ReturnsZeroWithoutRunning()
    {
        string workflow = WriteXaml("Echo.xaml", EchoXaml);

        RunnerOutcome outcome = Execute("--workflow", workflow, "--validate");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.Success), outcome.Summary);
            Assert.That(outcome.State, Is.EqualTo("Validated"), "прогон не должен был запускаться");
            Assert.That(outcome.Outputs, Is.Empty, "у проверки нет выходных аргументов прогона");
        });
    }

    [Test]
    public void Execute_ValidateOnlyWithInvalidWorkflow_ReturnsLoadFailed()
    {
        string workflow = WriteXaml("Invalid.xaml", InvalidXaml);

        RunnerOutcome outcome = Execute("--workflow", workflow, "--validate");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.LoadFailed), outcome.Summary);
            Assert.That(outcome.ErrorText, Does.Contain("проверка workflow"), "ошибки валидации обязаны быть названы");
        });
    }

    [Test]
    public void Execute_NotBuiltRobot_ReturnsRobotNotFoundSix()
    {
        string project = CreateProjectWithoutBuild();

        RunnerOutcome outcome = Execute("--project", project);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.RobotNotFound), outcome.Summary);
            Assert.That(outcome.ErrorText, Does.Contain("не собран"));
            Assert.That(outcome.Status, Is.EqualTo("not-built"));
        });
    }

    [Test]
    public void Execute_Timeout_ReturnsTimedOutFour()
    {
        string workflow = WriteXaml("Wait.xaml", LongWaitXaml);

        RunnerOutcome outcome = Execute("--workflow", workflow, "--timeout", "1");

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.TimedOut), outcome.Summary);
            Assert.That(outcome.TimedOut, Is.True);
            Assert.That(outcome.Status, Is.EqualTo("timeout"));
        });
    }

    [Test]
    public void Execute_Cancelled_ReturnsCancelledThree()
    {
        string workflow = WriteXaml("Wait.xaml", LongWaitXaml);
        RunnerOptions options = Parse("--workflow", workflow, "--timeout", "120");
        var cancellation = new CancellationTokenSource();

        Task<RunnerOutcome> run = Task.Run(() => WorkflowRunnerHost.Execute(options, NullRunnerJournal.Instance, cancellation.Token));

        Thread.Sleep(400);
        cancellation.Cancel();

        Assert.That(run.Wait(TimeSpan.FromSeconds(60)), Is.True, "отменённый прогон обязан завершиться");

        RunnerOutcome outcome = run.Result;

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.Cancelled), outcome.Summary);
            Assert.That(outcome.TimedOut, Is.False, "отмена — не таймаут");
            Assert.That(outcome.Status, Is.EqualTo("cancelled"));
        });
    }

    [Test]
    public void Execute_WritesResultJsonAndJsonLinesLog()
    {
        string workflow = WriteXaml("Echo.xaml", EchoXaml);
        string resultPath = IOPath.Combine(_root, "out", "result.json");
        string logPath = IOPath.Combine(_root, "out", "run.jsonl");
        var console = new StringWriter();

        RunnerOptions options = Parse(
            "--workflow", workflow,
            "--arg", "InputText=max",
            "--run-id", "acceptance-7",
            "--out", resultPath,
            "--log", logPath);

        RunnerOutcome outcome;

        using (RunnerJournal journal = RunnerJournal.Open(options, console))
        {
            outcome = WorkflowRunnerHost.Execute(options, journal);
        }

        Assert.That(outcome.ExitCode, Is.EqualTo(RunnerExitCode.Success), outcome.Summary);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(resultPath), Is.True, "result.json обязан быть записан");

            string result = File.ReadAllText(resultPath);
            Assert.That(result, Does.Contain("\"runId\":\"acceptance-7\""));
            Assert.That(result, Does.Contain("\"exitCode\":0"));
            Assert.That(result, Does.Contain("\"Result\":\"MAX\""));

            Assert.That(File.Exists(logPath), Is.True, "лог обязан быть записан");
            string[] lines = File.ReadAllLines(logPath).Where(l => l.Trim().Length > 0).ToArray();
            Assert.That(lines, Is.Not.Empty, "лог пуст");
            Assert.That(lines.All(l => l.Contains("\"runId\":\"acceptance-7\"")),
                "каждая запись лога обязана нести run-id, иначе прогоны не различить");

            Assert.That(console.ToString(), Does.Contain("[track]"), "консоль обязана получать события трекера");
            Assert.That(console.ToString(), Does.Contain("(код 0)"), "итог прогона обязан быть в консоли");
        });
    }

    private RunnerOutcome Execute(params string[] args)
    {
        RunnerOptions options = Parse(args);

        return WorkflowRunnerHost.Execute(options, NullRunnerJournal.Instance);
    }

    private static RunnerOptions Parse(params string[] args)
    {
        Assert.That(RunnerOptions.TryParse(args, out RunnerOptions options, out string error), Is.True, error);

        return options;
    }

    private string WriteXaml(string name, string content)
    {
        string path = IOPath.Combine(_root, name);
        File.WriteAllText(path, content);

        return path;
    }

    /// <summary>Проект робота без собранной сборки: <c>bin</c> физически отсутствует.</summary>
    private string CreateProjectWithoutBuild()
    {
        string directory = IOPath.Combine(_root, "NotBuiltRobot");
        Directory.CreateDirectory(directory);

        File.WriteAllText(IOPath.Combine(directory, "NotBuiltRobot.csproj"), @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""15.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <AssemblyName>NotBuiltRobot</AssemblyName>
    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
  </PropertyGroup>
</Project>");

        File.WriteAllText(IOPath.Combine(directory, "Main.xaml"), @"<Activity x:Class=""NotBuiltRobot.Main""
  xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities""
  xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <WriteLine Text=""робот, который не собрали"" />
</Activity>");

        return IOPath.Combine(directory, "NotBuiltRobot.csproj");
    }
}
