using System;
using System.Activities;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;

using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;
using Agent.Core.Services;
using NUnit.Framework;
using RPA.VsProject;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// `CP-08` — 4.7.2-робот на 4.8-хосте.
/// <para>
/// Паспорт совместимости и трекинг: эталонный робот `TestProcess\Net472Robot` (legacy WF,
/// `v4.7.2`) загружается в AppDomain тестового процесса (`net48`), исполняется и отдаёт
/// события в <see cref="CustomTrackingParticipant"/> — ту самую сборку `ActivitiesTracker`
/// (`v4.7.2`), которой пользуется студия. Это и есть проверяемый факт карточки: одна сборка
/// на 4.7.2 работает в 4.8-процессе, а не «теоретически совместима».
/// </para>
/// </summary>
[TestFixture]
public class Net472OnNet48HostTests
{
    private const string Net472FrameworkName = ".NETFramework,Version=v4.7.2";
    private const string Net48FrameworkName = ".NETFramework,Version=v4.8";

    private readonly List<string> shadowDirectories = new List<string>();

    [OneTimeTearDown]
    public void CleanupShadowCopies()
    {
        foreach (string directory in shadowDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
                // Загруженная в процесс сборка держит файл — каталог останется во временной папке.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Test]
    public void Host_RunsOnNet48_WhileRobotAndTrackerAreNet472()
    {
        BuildLegacyRobot();

        Assembly robot = Assembly.LoadFrom(ShadowCopyRobot());

        Assert.Multiple(() =>
        {
            Assert.That(AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName, Is.EqualTo(Net48FrameworkName),
                "тестовый процесс — хост на 4.8");

            Assert.That(TargetFrameworkOf(robot), Is.EqualTo(Net472FrameworkName),
                "робот собран на 4.7.2");

            Assert.That(TargetFrameworkOf(typeof(CustomTrackingParticipant).Assembly), Is.EqualTo(Net472FrameworkName),
                "`ActivitiesTracker`, которым пользуется студия, тоже на 4.7.2");

            // У mscorlib нет TargetFrameworkAttribute (это рантайм, а не библиотека), поэтому
            // 4.8-хост подтверждается именем платформы домена и версией CLR.
            Assert.That(Environment.Version.Major, Is.EqualTo(4), "рантайм .NET Framework 4.x — сборки 4.7.2 исполняются на нём");

            Assert.That(robot.ImageRuntimeVersion, Is.EqualTo(typeof(object).Assembly.ImageRuntimeVersion),
                "одна и та же версия CLR — иначе загрузка в домен невозможна");
        });
    }

    [Test]
    public void Net472Robot_TypeIsWorkflowActivityInThisDomain()
    {
        BuildLegacyRobot();

        object handle = Activator.CreateInstanceFrom(ShadowCopyRobot(), "Net472Robot.Main").Unwrap();

        Assert.That(handle, Is.InstanceOf<Activity>(), "тип Net472Robot.Main — WF-активность, загруженная в домен 4.8-процесса");
        Assert.That(handle.GetType().Assembly.GetName().Name, Is.EqualTo("Net472Robot"));
    }

    [Test]
    public void Net472Robot_RunIsTrackedByCustomTrackingParticipant()
    {
        BuildLegacyRobot();

        var participant = new CustomTrackingParticipant();
        var events = new List<TrackingEventArgs>();
        var completed = new ManualResetEventSlim(false);
        var outputs = new Dictionary<string, object>();
        Exception failure = null;

        participant.TrackingEventArgsRecieved += (sender, args) =>
        {
            lock (events)
            {
                events.Add(args);
            }
        };

        Activity robot = CreateActivity(ShadowCopyRobot());

        var application = new WorkflowApplication(robot, new Dictionary<string, object> { { "InputText", "max" } });
        application.Extensions.Add(participant);
        application.OnUnhandledException = args =>
        {
            failure = args.UnhandledException;
            return UnhandledExceptionAction.Terminate;
        };
        application.Completed = args =>
        {
            if (args.CompletionState == ActivityInstanceState.Closed)
            {
                foreach (KeyValuePair<string, object> output in args.Outputs)
                {
                    outputs[output.Key] = output.Value;
                }
            }
            else
            {
                failure ??= args.TerminationException;
            }

            completed.Set();
        };

        application.Run();

        Assert.That(completed.Wait(TimeSpan.FromSeconds(60)), Is.True, "робот не завершился за 60 секунд");
        Assert.That(failure, Is.Null, "робот упал: " + failure);
        Assert.That(outputs.ContainsKey("Result"), Is.True, "сценарий обязан вернуть выходной аргумент Result");
        Assert.That(outputs["Result"], Is.EqualTo("MAX"));

        List<TrackingEventArgs> snapshot;
        lock (events)
        {
            snapshot = events.ToList();
        }

        Assert.That(snapshot, Is.Not.Empty, "трекер обязан получить события от 4.7.2-робота");

        List<string> activities = TrackedActivityNames(snapshot);

        Assert.Multiple(() =>
        {
            // Процесс: трекер отдаёт сообщения «Процесс <id> запущен/завершён» без Record — проверяем текст.
            Assert.That(snapshot.Any(e => Message(e).Contains("запущен")), Is.True, "нет события запуска процесса");
            Assert.That(snapshot.Any(e => Message(e).Contains("завершён")), Is.True, "нет события завершения процесса");

            // WF отдаёт в трекинг отображаемые имена активностей эталона:
            // «Main sequence» (Sequence) -> «Has input» (If) -> «Guarded write» (TryCatch) -> «Log input» (WriteLine).
            Assert.That(activities, Does.Contain("Main sequence"), "нет трекинга Sequence");
            Assert.That(activities, Does.Contain("Has input"), "нет трекинга ветвления If");
            Assert.That(activities, Does.Contain("Guarded write"), "нет трекинга TryCatch");
            Assert.That(activities, Does.Contain("Log input"), "нет трекинга WriteLine");
            Assert.That(activities, Does.Contain("Build result"), "нет трекинга Assign");

            Assert.That(snapshot.Any(e => Message(e) == "Запущена активность Main sequence"), Is.True, "нет состояния Executing у Sequence");
            Assert.That(snapshot.Any(e => Message(e) == "Завершена активность Main sequence"), Is.True, "нет состояния Closed у Sequence");
        });

        Assert.That(
            snapshot.Any(e => e.ActivityParameters != null
                              && e.ActivityParameters.TryGetValue("InputText", out object value)
                              && Equals(value, "max")),
            Is.True,
            "трекер обязан показать значения аргументов робота (InputText = max) — иначе окно Locals пустое");
    }

    // --- Инфраструктура. ---

    private static string Message(TrackingEventArgs args)
    {
        return args.Message ?? string.Empty;
    }

    private static List<string> TrackedActivityNames(List<TrackingEventArgs> events)
    {
        return events
            .Where(e => e.Record is System.Activities.Tracking.ActivityStateRecord)
            .Select(e => e.ActivityInfo?.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();
    }

    private static string TargetFrameworkOf(Assembly assembly)
    {
        TargetFrameworkAttribute attribute = assembly
            .GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
            .OfType<TargetFrameworkAttribute>()
            .FirstOrDefault();

        return attribute?.FrameworkName;
    }

    private static Activity CreateActivity(string assemblyPath)
    {
        object handle = Activator.CreateInstanceFrom(assemblyPath, "Net472Robot.Main").Unwrap();

        return handle as Activity ?? throw new InvalidOperationException("тип Net472Robot.Main не является Activity");
    }

    private static void BuildLegacyRobot()
    {
        MsBuildLocation location = VsProjectBuilder.FindMSBuild(MsBuildToolchain.Auto);

        if (!location.Found)
        {
            Assert.Ignore("MSBuild не найден на машине — сборка эталона пропущена");
        }

        var log = new List<string>();
        VsBuildResult result = VsProjectBuilder.BuildWithToolchain(LegacyRobotProject, "Debug", MsBuildToolchain.Auto, log.Add, restore: true);

        Assert.That(result.Success, Is.True,
            "сборка эталона не прошла: " + Environment.NewLine
            + string.Join(Environment.NewLine, result.Errors.Take(10)) + Environment.NewLine + log.FirstOrDefault());
    }

    /// <summary>
    /// Копирует выход сборки во временный каталог: загруженная сборка блокирует bin, и следующая
    /// сборка эталона упала бы на MSB3027.
    /// </summary>
    private string ShadowCopyRobot()
    {
        string source = IOPath.GetDirectoryName(LegacyRobotDll);
        string target = IOPath.Combine(IOPath.GetTempPath(), "net472_host_shadow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, IOPath.Combine(target, IOPath.GetFileName(file)), true);
        }

        shadowDirectories.Add(target);

        return IOPath.Combine(target, "Net472Robot.dll");
    }

    private static string RepositoryRoot
    {
        get
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

    private static string LegacyRobotProject
    {
        get { return IOPath.Combine(RepositoryRoot, "AiRpaStudio", "TestProcess", "Net472Robot", "Net472Robot.csproj"); }
    }

    private static string LegacyRobotDll
    {
        get { return IOPath.Combine(RepositoryRoot, "AiRpaStudio", "TestProcess", "Net472Robot", "bin", "Debug", "Net472Robot.dll"); }
    }
}
