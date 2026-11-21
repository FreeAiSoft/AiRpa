using System;
using System.Activities;
using System.Activities.Statements;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.IO;
using System.Linq;
using System.Reflection;
using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Locals;

/// <summary>
/// Этап 8: стек скоупов прогона для панели «Локальные переменные».
/// </summary>
public class LocalsScopeTrackerTests
{
    [SetUp]
    public void SetUp()
    {
        LocalsScopeTracker.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        LocalsScopeTracker.Reset();
    }

    [Test]
    public void TrackingProvider_WithParentAndChild_ReturnsBothScopes()
    {
        var parent = CreateRecord("Parent", "1", "Executing",
            new Dictionary<string, object> { ["parentVar"] = "p" },
            new Dictionary<string, object>());
        var child = CreateRecord("Child", "2", "Executing",
            new Dictionary<string, object> { ["childVar"] = "c" },
            new Dictionary<string, object>());

        LocalsScopeTracker.Observe(parent);
        LocalsScopeTracker.Observe(child);

        LocalsSnapshot snapshot = new TrackingLocalsProvider().GetSnapshot(child);

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ActivityDisplayName, Is.EqualTo("Child"));
            Assert.That(byName["parentVar"].Data, Is.EqualTo("p"));
            Assert.That(byName["parentVar"].Scope, Is.EqualTo("Parent"));
            Assert.That(byName["childVar"].Data, Is.EqualTo("c"));
            Assert.That(byName["childVar"].Scope, Is.EqualTo("Child"));
        });
    }

    [Test]
    public void TrackingProvider_Shadowing_NearestScopeWins()
    {
        var parent = CreateRecord("Parent", "1", "Executing",
            new Dictionary<string, object> { ["value"] = "parent" },
            new Dictionary<string, object>());
        var child = CreateRecord("Child", "2", "Executing",
            new Dictionary<string, object> { ["value"] = "child" },
            new Dictionary<string, object>());

        LocalsScopeTracker.Observe(parent);
        LocalsScopeTracker.Observe(child);

        LocalsSnapshot snapshot = new TrackingLocalsProvider().GetSnapshot(child);

        List<LocalVar> shadowed = snapshot.Locals.Where(l => l.VarName == "value").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(shadowed.Count, Is.EqualTo(1), "одноимённая переменная родителя должна быть затенена");
            Assert.That(shadowed[0].Data, Is.EqualTo("child"));
            Assert.That(shadowed[0].Scope, Is.EqualTo("Child"));
        });
    }

    [Test]
    public void TrackingProvider_ClosedChild_KeepsParentOnly()
    {
        var parent = CreateRecord("Parent", "1", "Executing",
            new Dictionary<string, object> { ["parentVar"] = "p" },
            new Dictionary<string, object>());
        var childExecuting = CreateRecord("Child", "2", "Executing",
            new Dictionary<string, object> { ["childVar"] = "c" },
            new Dictionary<string, object>());
        var childClosed = CreateRecord("Child", "2", "Closed",
            new Dictionary<string, object>(),
            new Dictionary<string, object>());

        LocalsScopeTracker.Observe(parent);
        LocalsScopeTracker.Observe(childExecuting);
        LocalsScopeTracker.Observe(childClosed);

        LocalsSnapshot snapshot = new TrackingLocalsProvider().GetSnapshot(parent);

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("parentVar"), Is.True);
            Assert.That(byName.ContainsKey("childVar"), Is.False, "переменная закрытого ребёнка не должна быть видна");
        });
    }

    [Test]
    public void TrackingProvider_Faulted_RemovesScope()
    {
        var parent = CreateRecord("Parent", "1", "Executing",
            new Dictionary<string, object> { ["parentVar"] = "p" },
            new Dictionary<string, object>());
        var childExecuting = CreateRecord("Child", "2", "Executing",
            new Dictionary<string, object> { ["childVar"] = "c" },
            new Dictionary<string, object>());
        var childFaulted = CreateRecord("Child", "2", "Faulted",
            new Dictionary<string, object>(),
            new Dictionary<string, object>());

        LocalsScopeTracker.Observe(parent);
        LocalsScopeTracker.Observe(childExecuting);
        LocalsScopeTracker.Observe(childFaulted);

        LocalsSnapshot snapshot = new TrackingLocalsProvider().GetSnapshot(parent);

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
        Assert.That(byName.ContainsKey("childVar"), Is.False, "scope Faulted-активности должен сниматься");
    }

    [Test]
    public void TrackingProvider_WithoutObserve_FallsBackToRecord()
    {
        var record = CreateRecord("MyActivity", "99", "Executing",
            new Dictionary<string, object> { ["userName"] = "max" },
            new Dictionary<string, object> { ["InputText"] = "hello" });

        LocalsSnapshot snapshot = new TrackingLocalsProvider().GetSnapshot(record);

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.ActivityDisplayName, Is.EqualTo("MyActivity"));
            Assert.That(byName["userName"].Data, Is.EqualTo("max"));
            Assert.That(byName["userName"].Scope, Is.EqualTo("MyActivity"));
            Assert.That(byName["InputText"].Data, Is.EqualTo("hello"));
        });
    }

    [Test]
    public void Reset_ClearsObservedScopes()
    {
        var parent = CreateRecord("Parent", "1", "Executing",
            new Dictionary<string, object> { ["parentVar"] = "p" },
            new Dictionary<string, object>());
        LocalsScopeTracker.Observe(parent);

        LocalsScopeTracker.Reset();

        var empty = CreateRecord("Other", "2", "Executing",
            new Dictionary<string, object>(),
            new Dictionary<string, object>());

        LocalsSnapshot snapshot = new TrackingLocalsProvider().GetSnapshot(empty);

        Assert.That(snapshot.Locals, Is.Empty, "после Reset() старый стек не должен быть виден");
    }

    [Test]
    public void TrackingProvider_ChildArgument_RefreshesParentVariable()
    {
        var variable = new Variable<string>("text", "initial");
        var parentActivity = new Sequence
        {
            DisplayName = "Parent",
            Variables = { variable }
        };
        var childActivity = new Assign<string>
        {
            To = new OutArgument<string>(variable),
            Value = new InArgument<string>("hello")
        };

        var parent = CreateRecord("Parent", "1", "Executing",
            new Dictionary<string, object> { ["text"] = "initial" },
            new Dictionary<string, object>(),
            parentActivity);
        var childClosed = CreateRecord("Child", "2", "Closed",
            new Dictionary<string, object>(),
            new Dictionary<string, object> { ["To"] = "updated" },
            childActivity);

        LocalsScopeTracker.Observe(parent);
        LocalsScopeTracker.Observe(childClosed);

        LocalsSnapshot snapshot = new TrackingLocalsProvider().GetSnapshot(parent);

        LocalVar text = snapshot.Locals.Single(l => l.VarName == "text");
        Assert.That(text.Data, Is.EqualTo("updated"),
            "выходной аргумент ребёнка должен обновлять переменную родительского скоупа");
    }

    [Test]
    public void CustomTrackingParticipant_ObservesScopeTracker()
    {
        string source = ReadSource(@"ActivitiesTracker\Core\CustomTrackingParticipant.cs");

        Assert.That(source, Does.Contain("LocalsScopeTracker.Observe(activityStateRecord)"),
            "трекер обязан вести стек скоупов до формирования TrackingEventArgs");
    }

    [Test]
    public void ActivityRunner_ResetsScopeTracker_AtRunBoundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MethodBody("private void Start(string pathToXaml)"), Does.Contain("LocalsScopeTracker.Reset"));
            Assert.That(MethodBody("void OnCompleted("), Does.Contain("LocalsScopeTracker.Reset"));
            Assert.That(MethodBody("void OnAborted("), Does.Contain("LocalsScopeTracker.Reset"));
            Assert.That(MethodBody("public void WorkflowStop()"), Does.Contain("LocalsScopeTracker.Reset"));
        });
    }

    [Test]
    public void ActivityRunner_Resume_DoesNotResetScopeTracker()
    {
        string method = MethodBody("internal void Run(");

        int clear = method.IndexOf("OnUi(ClearLocalsPanel)", StringComparison.Ordinal);
        int wake = method.IndexOf("_resumeRuntimeFromHost?.Set()", StringComparison.Ordinal);

        Assert.That(clear, Is.GreaterThanOrEqualTo(0), "ветка resume обязана очищать панель Locals");
        Assert.That(wake, Is.GreaterThanOrEqualTo(0), "ветка resume обязана будить фоновый поток");

        string resumeBranch = method.Substring(clear, wake - clear);
        Assert.That(resumeBranch, Does.Not.Contain("LocalsScopeTracker.Reset"),
            "при продолжении прогона стек скоупов должен сохраняться");
    }

    [Test]
    public void TrackingProvider_ImplementationChild_ExcludesParentVariable()
    {
        var output = new Variable<string>("output", "main-value");

        Activity workflow = new Sequence
        {
            DisplayName = "Main",
            Variables = { output },
            Activities =
            {
                new ImplementationBodyActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    })
                {
                    DisplayName = "SubProcess1"
                }
            }
        };

        List<ActivityStateRecord> records = RunAndCollect(workflow, "Main", "SubProcess1", "inner");
        foreach (ActivityStateRecord record in records)
        {
            LocalsScopeTracker.Observe(record);
        }

        LocalsSnapshot snapshot = SnapshotFor(records, "inner");

        Assert.That(snapshot.Locals.Select(l => l.VarName), Does.Not.Contain("output"),
            "публичная переменная Main.xaml не должна протекать внутрь implementation-тела");
    }

    [Test]
    public void TrackingProvider_PublicChild_StillSeesParentVariable()
    {
        var output = new Variable<string>("output", "main-value");

        Activity workflow = new Sequence
        {
            DisplayName = "Main",
            Variables = { output },
            Activities =
            {
                new Sequence
                {
                    DisplayName = "publicChild",
                    Activities =
                    {
                        new Delay
                        {
                            DisplayName = "inner",
                            Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                        }
                    }
                }
            }
        };

        List<ActivityStateRecord> records = RunAndCollect(workflow, "Main", "publicChild", "inner");
        foreach (ActivityStateRecord record in records)
        {
            LocalsScopeTracker.Observe(record);
        }

        LocalsSnapshot snapshot = SnapshotFor(records, "inner");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
        Assert.That(byName.ContainsKey("output"), Is.True,
            "публичная переменная родителя должна оставаться видимой публичному потомку");
        Assert.That(byName["output"].Data, Is.EqualTo("main-value"));
    }

    [Test]
    public void TrackingProvider_ImplementationChild_IncludesOwnerArguments()
    {
        Activity workflow = new Sequence
        {
            DisplayName = "Main",
            Activities =
            {
                new ArgumentCarrierActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    })
                {
                    DisplayName = "SubProcess1",
                    InputText = new InArgument<string>("hello-arg")
                }
            }
        };

        List<ActivityStateRecord> records = RunAndCollect(workflow, "Main", "SubProcess1", "inner");
        foreach (ActivityStateRecord record in records)
        {
            LocalsScopeTracker.Observe(record);
        }

        LocalsSnapshot snapshot = SnapshotFor(records, "inner");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);
        Assert.That(byName.ContainsKey("InputText"), Is.True,
            "аргументы вложенной активности должны быть видны её implementation-телу");
        Assert.That(byName["InputText"].Data, Is.EqualTo("hello-arg"));
    }

    private static LocalsSnapshot SnapshotFor(List<ActivityStateRecord> records, string activityName)
    {
        ActivityStateRecord record = records.Single(r => r.Activity?.Name == activityName);
        return new TrackingLocalsProvider().GetSnapshot(record);
    }

    private static List<ActivityStateRecord> RunAndCollect(Activity workflow, params string[] requiredNames)
    {
        var participant = new RecordCollectingParticipant(requiredNames);
        var application = new WorkflowApplication(workflow);
        application.Extensions.Add(participant);

        Exception failure = null;
        application.OnUnhandledException = args =>
        {
            failure = args.UnhandledException;
            return UnhandledExceptionAction.Terminate;
        };

        var thread = new Thread(() =>
        {
            try
            {
                application.Run();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "LocalsScopeTrackerTests.Run"
        };
        thread.Start();

        bool captured = participant.WaitCaptured(TimeSpan.FromSeconds(30));
        try
        {
            application.Cancel(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Прогон мог завершиться/остановиться раньше.
        }

        thread.Join(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Null, "прогон упал: " + failure);
            Assert.That(captured, Is.True,
                "не дождались Executing-записей: " + string.Join(", ", requiredNames));
        });

        return participant.Records.ToList();
    }

    private sealed class RecordCollectingParticipant : TrackingParticipant
    {
        private readonly string[] requiredNames;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<ActivityStateRecord> records = new List<ActivityStateRecord>();
        private readonly ManualResetEventSlim captured = new ManualResetEventSlim(false);

        public RecordCollectingParticipant(params string[] requiredNames)
        {
            this.requiredNames = requiredNames;
            TrackingProfile = new TrackingProfile
            {
                Name = "LocalsScopeTrackerTestsProfile",
                ImplementationVisibility = ImplementationVisibility.All,
                Queries =
                {
                    new ActivityStateQuery
                    {
                        ActivityName = "*",
                        States = { "*" },
                        Arguments = { "*" },
                        Variables = { "*" }
                    }
                }
            };
        }

        public IReadOnlyList<ActivityStateRecord> Records => records;

        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            if (!(record is ActivityStateRecord stateRecord) || stateRecord.State != "Executing")
            {
                return;
            }

            lock (records)
            {
                records.Add(stateRecord);
            }

            string name = stateRecord.Activity?.Name;
            if (name != null)
            {
                seen.Add(name);
            }

            if (requiredNames.All(n => seen.Contains(n)))
            {
                captured.Set();
            }
        }

        public bool WaitCaptured(TimeSpan timeout)
        {
            return captured.Wait(timeout);
        }
    }

    private sealed class ImplementationBodyActivity : NativeActivity
    {
        private readonly Activity body;

        public ImplementationBodyActivity(Activity body)
        {
            this.body = body;
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var children = new Collection<Activity> { body };
            metadata.SetImplementationChildrenCollection(children);
        }

        protected override void Execute(NativeActivityContext context)
        {
            context.ScheduleActivity(body);
        }
    }

    private sealed class ArgumentCarrierActivity : NativeActivity
    {
        private readonly Activity body;

        public ArgumentCarrierActivity(Activity body)
        {
            this.body = body;
        }

        public InArgument<string> InputText { get; set; }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            RuntimeArgument inputArgument = new RuntimeArgument("InputText", typeof(string), ArgumentDirection.In);
            metadata.Bind(InputText, inputArgument);
            metadata.AddArgument(inputArgument);

            var children = new Collection<Activity> { body };
            metadata.SetImplementationChildrenCollection(children);
        }

        protected override void Execute(NativeActivityContext context)
        {
            context.ScheduleActivity(body);
        }
    }

    private static ActivityStateRecord CreateRecord(
        string name,
        string instanceId,
        string state,
        IDictionary<string, object> variables,
        IDictionary<string, object> arguments,
        Activity activity = null)
    {
        var info = new ActivityInfo(name, "1", instanceId, name);
        if (activity != null)
        {
            SetActivity(info, activity);
        }

        var record = new ActivityStateRecord(Guid.NewGuid(), 0, info, state);
        SetField(record, "variables", variables);
        SetField(record, "arguments", arguments);
        return record;
    }

    private static void SetActivity(ActivityInfo info, Activity activity)
    {
        PropertyInfo property = typeof(ActivityInfo).GetProperty(
            "Activity",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null, "внутреннее свойство ActivityInfo.Activity не найдено");
        property.SetValue(info, activity, null);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = typeof(ActivityStateRecord).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "внутреннее поле ActivityStateRecord '{0}' не найдено", name);
        field.SetValue(target, value);
    }

    private static string MethodBody(string signature)
    {
        string source = ReadSource(@"AiRpaStudio\Core\ActivityRunner.cs");
        int index = source.LastIndexOf(signature, StringComparison.Ordinal);
        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"signature not found: {signature}");
        return source.Substring(index, Math.Min(3200, source.Length - index));
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(FindRepositoryRoot(), relativePath);
        Assert.That(File.Exists(path), Is.True, $"file not found: {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "repository root not found (AiRpa.sln)");
        return directory.FullName;
    }
}
