using System;
using System.Activities;
using System.Activities.Statements;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading;
using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Locals;

/// <summary>
/// Этап 5 (вариант B): провайдер живых значений из <see cref="System.Activities.ActivityInstance"/>.
/// </summary>
public class ActivityInstanceLocalsProviderTests
{
    [Test]
    public void GetSnapshot_NullRecord_ReturnsEmpty()
    {
        var provider = new ActivityInstanceLocalsProvider();

        LocalsSnapshot snapshot = provider.GetSnapshot(null);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Locals, Is.Empty);
            Assert.That(snapshot.ActivityDisplayName, Is.Null);
        });
    }

    [Test]
    public void GetSnapshot_RecordWithoutActivityInstance_ReturnsEmpty()
    {
        var provider = new ActivityInstanceLocalsProvider();
        var record = new ActivityStateRecord(
            Guid.NewGuid(),
            0,
            new ActivityInfo("MyActivity", "1", "1", "MyActivity"),
            "Closed");

        LocalsSnapshot snapshot = provider.GetSnapshot(record);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Locals, Is.Empty);
            Assert.That(snapshot.ActivityDisplayName, Is.EqualTo("MyActivity"));
        });
    }

    [Test]
    public void GetSnapshot_ReadsLiveLocals_FromRunningWorkflow()
    {
        CaptureResult result = RunAndCapture(CreateSampleWorkflow(), "pause");

        Dictionary<string, LocalVar> byName = result.ActivityInstanceSnapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(result.ActivityInstanceSnapshot.ActivityDisplayName, Is.EqualTo("pause"));
            Assert.That(byName["text"].Data, Is.EqualTo("hello"));
            Assert.That(byName["text"].TypeName, Is.EqualTo("String"));
            Assert.That(byName["number"].Data, Is.EqualTo(42));
            Assert.That(byName["number"].TypeName, Is.EqualTo("Int32"));
        });
    }

    [Test]
    public void GetSnapshot_IncludesThisPseudoVariable()
    {
        CaptureResult result = RunAndCapture(CreateSampleWorkflow(), "pause");

        Dictionary<string, LocalVar> byName = result.ActivityInstanceSnapshot.Locals.ToDictionary(l => l.VarName);

        Assert.That(byName.ContainsKey("this"), Is.True, "вариант B обязан отдавать псевдопеременную this");
        Assert.That(byName["this"].Data, Is.InstanceOf<ActivityInstance>());
        Assert.That(byName["this"].TypeName, Is.EqualTo("System.Activities.ActivityInstance"));
    }

    [Test]
    public void GetSnapshot_InsideNestedActivity_ExcludesParentVariable_AndIncludesNestedArguments()
    {
        var output = new Variable<string>("output", "main-value");

        Activity workflow = new Sequence
        {
            DisplayName = "Main",
            Variables = { output },
            Activities =
            {
                new ArgumentCarrierActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    })
                {
                    InputText = new InArgument<string>("hello-arg")
                }
            }
        };

        CaptureResult result = RunAndCapture(workflow, "inner");

        Dictionary<string, LocalVar> byName = result.ActivityInstanceSnapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.Keys, Does.Not.Contain("output"),
                "публичная переменная Main.xaml не должна протекать внутрь вложенной XAML-активности");
            Assert.That(byName.ContainsKey("InputText"), Is.True,
                "аргументы вложенной активности должны быть видны её телу");
            Assert.That(byName["InputText"].Data, Is.EqualTo("hello-arg"));
        });
    }

    [Test]
    public void GetSnapshot_MatchesTrackingScope_AndAddsThis()
    {
        CaptureResult result = RunAndCapture(CreateSampleWorkflow(), "pause");

        Dictionary<string, LocalVar> tracking = result.TrackingSnapshot.Locals.ToDictionary(l => l.VarName);
        Dictionary<string, LocalVar> live = result.ActivityInstanceSnapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(tracking.Keys, Does.Not.Contain("this"), "трекинг-провайдер не отдаёт this");
            Assert.That(live.Keys, Does.Contain("this"), "ActivityInstance-провайдер обязан отдавать this");

            foreach (string name in new[] { "text", "number", "payload", "items", "table" })
            {
                Assert.That(live.ContainsKey(name), Is.True, $"live-скоп не содержит переменную {name}");
                Assert.That(tracking.ContainsKey(name), Is.True, $"tracking-скоп не содержит переменную {name}");
            }

            Assert.That(live["text"].Data, Is.EqualTo(tracking["text"].Data));
            Assert.That(live["number"].Data, Is.EqualTo(tracking["number"].Data));

            // Ссылочные типы читаются из той же живой Location — это один и тот же объект.
            Assert.That(live["payload"].Data, Is.SameAs(tracking["payload"].Data));
            Assert.That(live["items"].Data, Is.SameAs(tracking["items"].Data));
            Assert.That(live["table"].Data, Is.SameAs(tracking["table"].Data));
        });
    }

    private static CaptureResult RunAndCapture(Activity workflow, string captureActivityName)
    {
        var participant = new CapturingParticipant(captureActivityName);
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
            Name = "ActivityInstanceLocalsProviderTests.Run"
        };
        thread.Start();

        bool captured = participant.WaitCaptured(TimeSpan.FromSeconds(30));
        try
        {
            application.Cancel(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Прогон мог завершиться/остановиться раньше, чем мы успели отменить.
        }

        thread.Join(TimeSpan.FromSeconds(10));

        Assert.That(failure, Is.Null, "прогон упал: " + failure);
        Assert.That(captured, Is.True, $"не дождались трекинг-записи активности {captureActivityName}");
        Assert.That(participant.Result.Error, Is.Null, "провайдер упал: " + participant.Result.Error);

        return participant.Result;
    }

    private static Activity CreateSampleWorkflow()
    {
        var textVar = new Variable<string>("text", "initial");
        var numberVar = new Variable<int>("number", 0);
        var payloadVar = new Variable<object>("payload");
        var itemsVar = new Variable<List<string>>("items");
        var tableVar = new Variable<DataTable>("table");

        return new Sequence
        {
            DisplayName = "root",
            Variables = { textVar, numberVar, payloadVar, itemsVar, tableVar },
            Activities =
            {
                new Assign<string>
                {
                    To = new OutArgument<string>(textVar),
                    Value = new InArgument<string>("hello")
                },
                new Assign<int>
                {
                    To = new OutArgument<int>(numberVar),
                    Value = new InArgument<int>(42)
                },
                new Assign<List<string>>
                {
                    To = new OutArgument<List<string>>(itemsVar),
                    Value = new InArgument<List<string>>(ctx => BuildItems())
                },
                new Assign<DataTable>
                {
                    To = new OutArgument<DataTable>(tableVar),
                    Value = new InArgument<DataTable>(ctx => BuildTable())
                },
                new Assign<object>
                {
                    To = new OutArgument<object>(payloadVar),
                    Value = new InArgument<object>(ctx => BuildPayload())
                },
                new Delay
                {
                    DisplayName = "pause",
                    Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                }
            }
        };
    }

    private static List<string> BuildItems()
    {
        return new List<string> { "a", "b" };
    }

    private static DataTable BuildTable()
    {
        var table = new DataTable("People");
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Age", typeof(int));
        table.Rows.Add("max", 33);
        return table;
    }

    private static object BuildPayload()
    {
        return new SamplePayload { Name = "obj", Count = 7 };
    }

    private sealed class SamplePayload
    {
        public string Name { get; set; }

        public int Count { get; set; }
    }

    /// <summary>
    /// Активность с входным аргументом и implementation-телом: моделирует вложенную XAML-активность.
    /// </summary>
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

    private sealed class CaptureResult
    {
        public ActivityStateRecord Record { get; set; }

        public LocalsSnapshot TrackingSnapshot { get; set; }

        public LocalsSnapshot ActivityInstanceSnapshot { get; set; }

        public Exception Error { get; set; }
    }

    private sealed class CapturingParticipant : TrackingParticipant
    {
        private readonly string captureActivityName;
        private readonly TrackingLocalsProvider tracking = new TrackingLocalsProvider();
        private readonly ActivityInstanceLocalsProvider live = new ActivityInstanceLocalsProvider();
        private readonly ManualResetEventSlim captured = new ManualResetEventSlim(false);

        public CapturingParticipant(string captureActivityName)
        {
            this.captureActivityName = captureActivityName;
            TrackingProfile = new TrackingProfile
            {
                Name = "ActivityInstanceLocalsProviderTestsProfile",
                ImplementationVisibility = ImplementationVisibility.All,
                Queries =
                {
                    new ActivityStateQuery
                    {
                        ActivityName = "*",
                        States = { "*" },
                        Arguments = { "*" },
                        Variables = { "text", "number", "payload", "items", "table" }
                    }
                }
            };
        }

        public CaptureResult Result { get; } = new CaptureResult();

        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            if (!(record is ActivityStateRecord stateRecord)
                || stateRecord.Activity?.Name != captureActivityName
                || stateRecord.State != "Executing"
                || Result.Record != null)
            {
                return;
            }

            try
            {
                Result.Record = stateRecord;
                Result.TrackingSnapshot = tracking.GetSnapshot(stateRecord);
                Result.ActivityInstanceSnapshot = live.GetSnapshot(stateRecord);
            }
            catch (Exception ex)
            {
                Result.Error = ex;
            }
            finally
            {
                captured.Set();
            }
        }

        public bool WaitCaptured(TimeSpan timeout)
        {
            return captured.Wait(timeout);
        }
    }
}
