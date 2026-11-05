using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using RPA.Core;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-08`, шаг D — headless-движок прогона (<see cref="WorkflowRunEngine"/>).
/// <para>
/// Движок отвечает за то, что делает прогон: загрузка активности (XAML-текст, файл, собранная
/// сборка), <c>WorkflowApplication</c> с трекером и итог с кодом возврата. Проверяется именно
/// контракт результата — успех с выходными аргументами, падение со стеком, таймаут и отмена, —
/// потому что этот же контракт нужен раннеру `OR-02`: иначе «студия умеет, а раннер нет».
/// </para>
/// </summary>
[TestFixture]
public class WorkflowRunEngineTests
{
    /// <summary>Эталон прогона: Sequence -> WriteLine -> Assign, вход <c>InputText</c>, выход <c>Result</c>.</summary>
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
      <x:String>System.Collections.Generic</x:String>
      <x:String>System.Linq</x:String>
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

    /// <summary>Сценарий без выхода: прогон обязан быть остановлен пределом времени.</summary>
    private const string LongWaitXaml = @"<Activity xmlns=""http://schemas.microsoft.com/netfx/2009/xaml/activities"" xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Sequence DisplayName=""Waiting sequence"">
    <Delay DisplayName=""Long wait"" Duration=""00:10:00"" />
    <WriteLine DisplayName=""After wait"" Text=""после ожидания"" />
  </Sequence>
</Activity>";

    // --- Загрузка активности. ---

    [Test]
    public void LoadActivityFromText_ReturnsWorkflowActivity()
    {
        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromText(EchoXaml);

        Assert.That(activity, Is.Not.Null, "движок обязан вернуть загруженную активность");
    }

    [Test]
    public void LoadActivityFromText_EmptyText_Throws()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => WorkflowRunEngine.LoadActivityFromText("   "));

        Assert.That(error.Message, Does.Contain("XAML"), "в тексте ошибки обязано быть сказано, что не так");
    }

    [Test]
    public void LoadActivityFromText_BrokenXaml_ThrowsAndTryLoadReturnsError()
    {
        const string broken = "<Activity><Sequence DisplayName=\"нет закрывающего тега\">";

        Exception error = Assert.Throws(Is.InstanceOf<Exception>(), () => WorkflowRunEngine.LoadActivityFromText(broken));
        Assert.That(error, Is.Not.Null);

        bool loaded = WorkflowRunEngine.TryLoadActivityFromText(broken, out System.Activities.Activity activity, out string text);

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.False, "битый XAML не должен считаться загруженным");
            Assert.That(activity, Is.Null);
            Assert.That(text, Does.Contain("Exception"), "описание ошибки — полный текст исключения со стеком");
        });
    }

    [Test]
    public void LoadActivityFromFile_MissingFile_Throws()
    {
        string missing = IOPath.Combine(IOPath.GetTempPath(), Guid.NewGuid().ToString("N") + ".xaml");

        Assert.That(() => WorkflowRunEngine.LoadActivityFromFile(missing), Throws.TypeOf<FileNotFoundException>());
        Assert.That(WorkflowRunEngine.TryLoadActivityFromFile(missing, out _, out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
    }

    // --- Успешный прогон и трекинг. ---

    [Test]
    public void Run_CompletesWorkflowAndReturnsOutputs()
    {
        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromText(EchoXaml);

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "max" } },
            TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(WorkflowRunState.Completed), result.Description);
            Assert.That(result.IsSuccess, Is.True, result.Description);
            Assert.That(result.ExitCode, Is.EqualTo(0), "успех обязан давать код возврата 0");
            Assert.That(result.Error, Is.Null);
            Assert.That(result.TimedOut, Is.False);
            Assert.That(result.GetOutput("Result"), Is.EqualTo("MAX"), "выходной аргумент Result обязан быть в результате");
        });
    }

    [Test]
    public void Run_SendsTrackingEventsToCustomTrackingParticipant()
    {
        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromText(EchoXaml);
        var live = new List<string>();

        WorkflowRunResult result = WorkflowRunEngine.Run(
            activity,
            new Dictionary<string, object> { { "InputText", "max" } },
            TimeSpan.FromSeconds(60),
            onTracked: args => { lock (live) { live.Add(args.Message ?? string.Empty); } });

        Assert.That(result.IsSuccess, Is.True, result.Description);

        List<string> messages = result.TrackingEvents.Select(e => e.Message ?? string.Empty).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(messages, Is.Not.Empty, "движок обязан отдавать события трекера");
            Assert.That(messages.Any(m => m.Contains("запущен")), Is.True, "нет события запуска процесса");
            Assert.That(messages.Any(m => m.Contains("завершён")), Is.True, "нет события завершения процесса");
            Assert.That(messages, Does.Contain("Запущена активность Main sequence"), "нет состояния Executing у Sequence");
            Assert.That(messages, Does.Contain("Завершена активность Main sequence"), "нет состояния Closed у Sequence");
            Assert.That(live, Has.Count.EqualTo(result.TrackingEvents.Count),
                "обработчик onTracked обязан получить столько же событий, сколько накопило результат");
        });
    }

    // --- Падение. ---

    [Test]
    public void Run_FaultedWorkflow_ReturnsErrorWithStack()
    {
        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromText(EchoXaml);

        // InputText не передан: выражение [InputText.ToUpper()] падает на null — настоящий сбой прогона.
        WorkflowRunResult result = WorkflowRunEngine.Run(activity, null, TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False, "падение не может считаться успехом");
            Assert.That(result.ExitCode, Is.EqualTo(1), "любой нештатный исход обязан давать код возврата 1");
            Assert.That(result.State, Is.AnyOf(WorkflowRunState.Faulted, WorkflowRunState.Cancelled, WorkflowRunState.Aborted),
                result.Description);
            Assert.That(result.Error, Is.Not.Null, result.Description);
            Assert.That(result.ErrorText, Does.Contain("Exception"), "текст ошибки обязан содержать тип исключения");
            Assert.That(result.ErrorText, Does.Contain("   at ").Or.Contain("   в "),
                "без стека ошибку прогона невозможно диагностировать");
        });
    }

    // --- Таймаут и отмена. ---

    [Test]
    public void Run_WorkflowWithoutExit_StopsByTimeout()
    {
        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromText(LongWaitXaml);

        WorkflowRunResult result = WorkflowRunEngine.Run(activity, null, TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(result.TimedOut, Is.True, "зависший сценарий обязан быть остановлен пределом времени");
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Error, Is.InstanceOf<TimeoutException>(), result.Description);
        });
    }

    [Test]
    public void Run_CancellationRequested_ReturnsCancelled()
    {
        System.Activities.Activity activity = WorkflowRunEngine.LoadActivityFromText(LongWaitXaml);
        var cancellation = new CancellationTokenSource();

        Task<WorkflowRunResult> run = Task.Run(() =>
            WorkflowRunEngine.Run(activity, null, TimeSpan.FromMinutes(5), cancellation.Token));

        // Прогон уже в ожидании на Delay — отменяем после короткой паузы.


        Thread.Sleep(300);
        cancellation.Cancel();

        Assert.That(run.Wait(TimeSpan.FromSeconds(60)), Is.True, "отменённый прогон не завершился за 60 секунд");

        WorkflowRunResult result = run.Result;

        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(WorkflowRunState.Cancelled), result.Description);
            Assert.That(result.TimedOut, Is.False, "отмена — не таймаут: иначе вызывающая сторона не отличит причину");
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(1));
        });
    }

    // --- Контракт «один путь прогона». ---

    [Test]
    public void Engine_HasNoUiDependencies()
    {
        string source = ReadSource("AiRpaStudio", "Core", "WorkflowRunEngine.cs");

        Assert.Multiple(() =>
        {
            foreach (string forbidden in new[] { "System.Windows", "MessageBox", "Console.ReadKey", "DispatcherTimer", "CustomWfDesigner" })
            {
                Assert.That(source, Does.Not.Contain(forbidden),
                    $"движок обязан оставаться headless: найден «{forbidden}». Иначе прогон в раннере (OR-02) потребует UI.");
            }
        });
    }

    [Test]
    public void ActivityRunner_UsesSharedEngineForLoadingAndApplication()
    {
        string source = ReadSource("AiRpaStudio", "Core", "ActivityRunner.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("WorkflowRunEngine.LoadActivityFromText"),
                "студия обязана грузить XAML тем же движком, что и раннер");
            Assert.That(source, Does.Contain("WorkflowRunEngine.CreateApplication"),
                "приложение прогона обязано создаваться общим движком");
            Assert.That(source, Does.Not.Contain("new WorkflowApplication("),
                "второй путь создания WorkflowApplication разошёлся бы с прогоном в раннере");
            Assert.That(source, Does.Not.Contain("ActivityXamlServices.Load"),
                "второй путь загрузки XAML дал бы разные настройки (CompileExpressions) в студии и раннере");
        });
    }

    private static string ReadSource(params string[] relativeParts)
    {
        return File.ReadAllText(IOPath.Combine(new[] { FindRepositoryRoot() }.Concat(relativeParts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(IOPath.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");

        return directory.FullName;
    }
}
