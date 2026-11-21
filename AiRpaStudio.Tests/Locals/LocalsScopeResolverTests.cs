using System;
using System.Activities;
using System.Activities.Statements;
using System.Activities.Tracking;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using ActivitiesTracker.Core;
using ActivitiesTracker.Entities;
using NUnit.Framework;

namespace AiRpaStudio.Tests.Locals;

/// <summary>
/// Этап 1 плана <c>Locals_Scope_Correctness_Plan.md</c>: эталонные code-first workflow фиксируют
/// правильную модель scope, которую должен реализовать <see cref="LocalsScopeResolver"/>.
/// </summary>
public class LocalsScopeResolverTests
{
    private static readonly PropertyInfo ActivityInfoInstanceProperty =
        typeof(ActivityInfo).GetProperty("Instance", BindingFlags.Instance | BindingFlags.NonPublic);

    [Test]
    public void Resolve_PublicVariable_VisibleToPublicChild()
    {
        var publicVar = new Variable<string>("publicVar", "visible");

        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Variables = { publicVar },
            Activities =
            {
                new Delay
                {
                    DisplayName = "leaf",
                    Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                }
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "leaf");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("publicVar"), Is.True,
                "публичная переменная должна быть видна публичному потомку");
            Assert.That(byName["publicVar"].Data, Is.EqualTo("visible"));
        });
    }

    [Test]
    public void Resolve_PublicVariable_NotVisibleToImplementationChild()
    {
        var publicVar = new Variable<string>("publicVar", "hidden");

        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Variables = { publicVar },
            Activities =
            {
                new ImplementationBodyActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    })
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "inner");

        Assert.That(snapshot.Locals.Select(l => l.VarName), Does.Not.Contain("publicVar"),
            "публичная переменная предка не должна протекать через implementation-границу");
    }

    [Test]
    public void Resolve_NestedActivityArguments_VisibleToImplementationChild()
    {
        Activity workflow = new Sequence
        {
            DisplayName = "Root",
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

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "inner");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("InputText"), Is.True,
                "аргументы вложенной активности должны быть видны её implementation-потомку");
            Assert.That(byName["InputText"].Data, Is.EqualTo("hello-arg"));
        });
    }

    [Test]
    public void Resolve_IncludesThisPseudoVariable()
    {
        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Activities =
            {
                new Delay
                {
                    DisplayName = "leaf",
                    Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                }
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "leaf");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("this"), Is.True,
                "resolver обязан отдавать псевдопеременную this");
            Assert.That(byName["this"].Data, Is.InstanceOf<ActivityInstance>());
            Assert.That(byName["this"].TypeName, Is.EqualTo("System.Activities.ActivityInstance"));
        });
    }

    [Test]
    public void Resolve_ShadowsDuplicateNames_AndHidesUnderscoreVariables()
    {
        var outerX = new Variable<string>("x", "outer");
        var innerX = new Variable<string>("x", "inner");
        var hidden = new Variable<string>("_secret", "hidden");

        Activity workflow = new Sequence
        {
            DisplayName = "Outer",
            Variables = { outerX, hidden },
            Activities =
            {
                new Sequence
                {
                    DisplayName = "Inner",
                    Variables = { innerX },
                    Activities =
                    {
                        new Delay
                        {
                            DisplayName = "leaf",
                            Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                        }
                    }
                }
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "leaf");

        LocalVar[] xLocals = snapshot.Locals.Where(l => l.VarName == "x").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(xLocals.Length, Is.EqualTo(1),
                "затенение имён: ближайший scope должен побеждать");
            Assert.That(xLocals[0].Data, Is.EqualTo("inner"));
            Assert.That(snapshot.Locals.Select(l => l.VarName), Does.Not.Contain("_secret"),
                "переменные с именем, начинающимся на '_', должны быть скрыты");
        });
    }

    [Test]
    public void Resolve_ImplementationVariable_VisibleToImplementationChild()
    {
        var implVar = new Variable<string>("implVar", "impl-value");

        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Activities =
            {
                new ImplementationVariablesCarrierActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    },
                    implVar)
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "inner");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("implVar"), Is.True,
                "implementation-переменная вложенной активности должна быть видна её implementation-потомку");
            Assert.That(byName["implVar"].Data, Is.EqualTo("impl-value"));
        });
    }

    [Test]
    public void Resolve_DelegateArgument_VisibleToDelegateHandler()
    {
        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Activities =
            {
                new InvokeAction<string>
                {
                    DisplayName = "Invoke",
                    Argument = new InArgument<string>("delegate-value"),
                    Action = new ActivityAction<string>
                    {
                        Argument = new DelegateInArgument<string>("Argument"),
                        Handler = new Delay
                        {
                            DisplayName = "handler",
                            Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                        }
                    }
                }
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "handler");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("Argument"), Is.True,
                "аргументы делегата должны быть видны внутри обработчика делегата");
            Assert.That(byName["Argument"].Data, Is.EqualTo("delegate-value"));
        });
    }

    [Test]
    public void Resolve_OutArgument_OnExecuting_IsUnassigned()
    {
        var outputVar = new Variable<string>("outValue");

        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Variables = { outputVar },
            Activities =
            {
                new OutArgumentCarrierActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    })
                {
                    OutputText = new OutArgument<string>(outputVar)
                }
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "inner", "Executing");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("OutputText"), Is.True,
                "Out-аргумент должен быть виден implementation-потомку");
            LocalVar output = byName["OutputText"];
            Assert.That(output.Kind, Is.EqualTo(LocalVarKind.Argument));
            Assert.That(output.Direction, Is.EqualTo(ArgumentDirection.Out));
            Assert.That(output.ActivityState, Is.EqualTo("Executing"));
            Assert.That(output.IsUnassigned, Is.True,
                "Out-аргумент на Executing должен быть помечен «не присвоен»");
            Assert.That(output.Data, Is.Null);
        });
    }

    [Test]
    public void Resolve_OutArgument_OnClosed_ShowsValue()
    {
        var outputVar = new Variable<string>("outValue");

        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Variables = { outputVar },
            Activities =
            {
                new OutArgumentCarrierActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    })
                {
                    DisplayName = "OutCarrier",
                    OutputText = new OutArgument<string>(outputVar)
                }
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "inner", "Executing", "Closed");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("OutputText"), Is.True);
            LocalVar output = byName["OutputText"];
            Assert.That(output.Kind, Is.EqualTo(LocalVarKind.Argument));
            Assert.That(output.Direction, Is.EqualTo(ArgumentDirection.Out));
            Assert.That(output.ActivityState, Is.EqualTo("Closed"));
            Assert.That(output.IsUnassigned, Is.False);
            Assert.That(output.Data, Is.EqualTo("done"),
                "Out-аргумент на Closed должен показывать итоговое значение");
        });
    }

    [Test]
    public void Resolve_InArgument_OnClosed_DoesNotDisappear()
    {
        Activity workflow = new Sequence
        {
            DisplayName = "Root",
            Activities =
            {
                new ArgumentCarrierActivity(
                    new Delay
                    {
                        DisplayName = "inner",
                        Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                    })
                {
                    DisplayName = "InCarrier",
                    InputText = new InArgument<string>("hello-arg")
                }
            }
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "inner", "Executing", "Closed");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("InputText"), Is.True,
                "In-аргумент не должен пропадать на Closed");
            LocalVar input = byName["InputText"];
            Assert.That(input.Kind, Is.EqualTo(LocalVarKind.Argument));
            Assert.That(input.Direction, Is.EqualTo(ArgumentDirection.In));
            Assert.That(input.ActivityState, Is.EqualTo("Closed"));
            Assert.That(input.IsUnassigned, Is.False);
            Assert.That(input.Data, Is.EqualTo("hello-arg"));
        });
    }

    [Test]
    public void Resolve_AncestorArguments_DoNotLeakThroughImplementationBoundary()
    {
        Activity workflow = new NamedArgumentCarrierActivity(
            new NamedArgumentCarrierActivity(
                new Delay
                {
                    DisplayName = "inner",
                    Duration = new InArgument<TimeSpan>(TimeSpan.FromSeconds(30))
                },
                "ParentArg")
            {
                Value = new InArgument<string>("parent-arg")
            },
            "RootArg")
        {
            DisplayName = "RootCarrier",
            Value = new InArgument<string>("root-arg")
        };

        LocalsSnapshot snapshot = CaptureAndResolve(workflow, "inner", "Executing");

        Dictionary<string, LocalVar> byName = snapshot.Locals.ToDictionary(l => l.VarName);

        Assert.Multiple(() =>
        {
            Assert.That(byName.ContainsKey("ParentArg"), Is.True,
                "аргумент ближайшей implementation-активности должен быть виден");
            Assert.That(byName["ParentArg"].Data, Is.EqualTo("parent-arg"));
            Assert.That(byName.Keys, Does.Not.Contain("RootArg"),
                "аргумент предка не должен протекать через implementation-границу");
        });
    }


    private static LocalsSnapshot CaptureAndResolve(Activity workflow, string captureActivityName)
    {
        return CaptureAndResolve(workflow, captureActivityName, "Executing", "Executing");
    }
    private static LocalsSnapshot CaptureAndResolve(Activity workflow, string captureActivityName, string state)
    {
        return CaptureAndResolve(workflow, captureActivityName, state, state);
    }



    private static LocalsSnapshot CaptureAndResolve(
        Activity workflow,
        string captureActivityName,
        string captureState,
        string resolveState)
    {
        var participant = new InstanceCapturingParticipant(captureActivityName, captureState);
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
            Name = "LocalsScopeResolverTests.Run"
        };
        thread.Start();

        bool captured = participant.WaitCaptured(TimeSpan.FromSeconds(30));

        LocalsSnapshot snapshot = null;
        Exception resolveError = null;
        if (captured && participant.Instance != null)
        {
            try
            {
                snapshot = new LocalsScopeResolver().Resolve(participant.Instance, resolveState);
            }
            catch (Exception ex)
            {
                resolveError = ex;
            }
        }

        try
        {
            application.Cancel(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Прогон мог завершиться или остановиться раньше, чем мы успели отменить.
        }

        thread.Join(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Null, "прогон упал: " + failure);
            Assert.That(captured, Is.True, $"не дождались записи активности {captureActivityName}");
            Assert.That(participant.Instance, Is.Not.Null,
                "не удалось извлечь ActivityInstance из ActivityInfo");
            Assert.That(resolveError, Is.Null, "resolver упал: " + resolveError);
        });

        return snapshot;
    }

    private static ActivityInstance GetActivityInstance(ActivityInfo activityInfo)
    {
        if (activityInfo == null || ActivityInfoInstanceProperty == null)
        {
            return null;
        }

        return ActivityInfoInstanceProperty.GetValue(activityInfo) as ActivityInstance;
    }

    /// <summary>
    /// Активность, тело которой является implementation-ребёнком (граница implementation scope).
    /// </summary>
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

    /// <summary>
    /// Активность с входным аргументом и implementation-телом: проверяет видимость аргументов
    /// внутри вложенного implementation-потомка.
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

    /// <summary>
    /// Активность с implementation-переменной и implementation-телом: проверяет, что
    /// implementation-переменная видна вложенному implementation-потомку.
    /// </summary>
    private sealed class ImplementationVariablesCarrierActivity : NativeActivity
    {
        private readonly Activity body;
        private readonly Variable<string> implVar;

        public ImplementationVariablesCarrierActivity(Activity body, Variable<string> implVar)
        {
            this.body = body;
            this.implVar = implVar;
        }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            var children = new Collection<Activity> { body };
            metadata.SetImplementationChildrenCollection(children);
            metadata.AddImplementationVariable(implVar);
        }

        protected override void Execute(NativeActivityContext context)
        {
            context.ScheduleActivity(body);
        }
    }

    /// <summary>
    /// Активность с Out-аргументом и implementation-телом: проверяет семантику Out-аргумента
    /// на Executing (не присвоен) и Closed (итоговое значение).
    /// </summary>
    private sealed class OutArgumentCarrierActivity : NativeActivity
    {
        private readonly Activity body;

        public OutArgumentCarrierActivity(Activity body)
        {
            this.body = body;
        }

        public OutArgument<string> OutputText { get; set; }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            RuntimeArgument outputArgument = new RuntimeArgument("OutputText", typeof(string), ArgumentDirection.Out);
            metadata.Bind(OutputText, outputArgument);
            metadata.AddArgument(outputArgument);

            var children = new Collection<Activity> { body };
            metadata.SetImplementationChildrenCollection(children);
        }

        protected override void Execute(NativeActivityContext context)
        {
            OutputText.Set(context, "done");
            context.ScheduleActivity(body);
        }

        private void OnBodyCompleted(NativeActivityContext context, ActivityInstance completedInstance)
        {
            OutputText.Set(context, "done");
        }
    }

    /// <summary>
    /// Активность с In-аргументом с произвольным именем и implementation-телом: позволяет
    /// проверить, что аргументы предков не протекают через implementation-границу.
    /// </summary>
    private sealed class NamedArgumentCarrierActivity : NativeActivity
    {
        private readonly Activity body;
        private readonly string argumentName;

        public NamedArgumentCarrierActivity(Activity body, string argumentName)
        {
            this.body = body;
            this.argumentName = argumentName;
        }

        public InArgument<string> Value { get; set; }

        protected override void CacheMetadata(NativeActivityMetadata metadata)
        {
            RuntimeArgument argument = new RuntimeArgument(argumentName, typeof(string), ArgumentDirection.In);
            metadata.Bind(Value, argument);
            metadata.AddArgument(argument);

            var children = new Collection<Activity> { body };
            metadata.SetImplementationChildrenCollection(children);
        }

        protected override void Execute(NativeActivityContext context)
        {
            context.ScheduleActivity(body);
        }
    }


    private sealed class InstanceCapturingParticipant : TrackingParticipant
    {
        private readonly string captureActivityName;
        private readonly string captureState;
        private readonly ManualResetEventSlim captured = new ManualResetEventSlim(false);

        public InstanceCapturingParticipant(string captureActivityName, string captureState)
        {
            this.captureActivityName = captureActivityName;
            this.captureState = captureState;
            TrackingProfile = new TrackingProfile
            {
                Name = "LocalsScopeResolverTestsProfile",
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

        public ActivityInstance Instance { get; private set; }

        protected override void Track(TrackingRecord record, TimeSpan timeout)
        {
            if (!(record is ActivityStateRecord stateRecord)
                || stateRecord.Activity?.Name != captureActivityName
                || stateRecord.State != captureState
                || Instance != null)
            {
                return;
            }

            Instance = GetActivityInstance(stateRecord.Activity);
            captured.Set();
        }

        public bool WaitCaptured(TimeSpan timeout)
        {
            return captured.Wait(timeout);
        }
    }
}
