using System.IO;

using NUnit.Framework;

using IOPath = System.IO.Path;

namespace AiRpaStudio.Tests;

/// <summary>
/// Переработка студийного запуска WF (вариант A): <c>ActivityRunner</c> обязан запускать
/// <c>WorkflowApplication.Run()</c> на фоновом потоке и не поллить состояние по
/// <c>DispatcherTimer</c> — иначе длинная активность замораживает UI, а автомат состояний
/// молча «зависает» на <c>AwaitDesignerReady</c>, если в маппинге нет Sequence с <c>Id == "1.1"</c>.
/// </summary>
[TestFixture]
public class ActivityRunnerLaunchTests
{
    [Test]
    public void ActivityRunner_DoesNotPollWorkflowOnDispatcherTimer()
    {
        string source = ReadSource("AiRpaStudio", "Core", "ActivityRunner.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("DispatcherTimer"),
                "запуск обязан быть событийным (Idle/Completed/Aborted), а не поллингом по таймеру на UI-потоке");
            Assert.That(source, Does.Not.Contain("RunSubWorkflow"),
                "поллинг-машина состояний должна уйти: субворкфлоу запускается по трекинг-событию, а не опрашивается по таймеру");
        });
    }

    [Test]
    public void ActivityRunner_RunsWorkflowApplicationOnBackgroundThread()
    {
        string source = ReadSource("AiRpaStudio", "Core", "ActivityRunner.cs");

        Assert.That(source, Does.Contain("IsBackground = true"),
            "WorkflowApplication.Run() обязан исполняться на фоновом потоке, а не на UI: иначе длинные активности замораживают студию");
    }

    private static string ReadSource(params string[] relativeParts)
    {
        string root = FindRepositoryRoot();

        string[] parts = new string[relativeParts.Length + 1];
        parts[0] = root;

        for (int index = 0; index < relativeParts.Length; index++)
        {
            parts[index + 1] = relativeParts[index];
        }

        return File.ReadAllText(IOPath.Combine(parts));
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
