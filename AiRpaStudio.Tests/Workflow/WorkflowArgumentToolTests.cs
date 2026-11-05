using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.AgentTools;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Agent.Core.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;
using RPA.Chat.Services.Workflow;

namespace AiRpaStudio.Tests.Workflow
{
    /// <summary>
    /// AG-05: инструменты <c>argument_set</c>, <c>variable_add</c>, <c>variable_remove</c> — разбор запроса,
    /// пачка, атомарность и текст ответа.
    /// <para>
    /// Проверяется оркестрация без конструктора: сессия-регистратор запоминает, что именно пришло из
    /// инструмента, и умеет «отказать» на заданном аргументе. Поэтому здесь видно то, что не видно на
    /// живой модели: пустая строка остаётся литералом, число превращается в текст, язык выражения доезжает
    /// до сессии, а отказ второй правки пачки отменяет первую. Реальную модель конструктора (типы,
    /// выражения, переменные) проверяет <c>WorkflowArgumentBridgeTests</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowArgumentToolTests
    {
        private const string OpenPath = @"C:\work\Main.xaml";

        // --- Реестр инструментов ---

        [Test]
        public void Registry_ContainsArgumentTools()
        {
            AgentToolRegistry registry = CreateFactory(new RecordingEditingService()).CreateRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(registry.Find("argument_set"), Is.Not.Null, "нет инструмента argument_set");
                Assert.That(registry.Find("variable_add"), Is.Not.Null, "нет инструмента variable_add");
                Assert.That(registry.Find("variable_remove"), Is.Not.Null, "нет инструмента variable_remove");
            });
        }

        [Test]
        public void ArgumentTools_AreMutating_AndHiddenInPlan()
        {
            AgentToolRegistry registry = CreateFactory(new RecordingEditingService()).CreateRegistry();
            List<string> plan = registry.ForMode(AgentMode.Plan, AgentPermissions.All)
                .Select(tool => tool.Name)
                .ToList();

            foreach (var name in new[] { "argument_set", "variable_add", "variable_remove" })
            {
                Assert.Multiple(() =>
                {
                    Assert.That(registry.Find(name).Mutates, Is.True, name + " обязан быть мутирующим");
                    Assert.That(registry.Find(name).Permission, Is.EqualTo(AgentPermissions.EditFiles),
                        name + " правит модель конструктора — категория EditFiles");
                    Assert.That(plan, Does.Not.Contain(name), name + " не должен быть доступен в Plan");
                });
            }
        }

        // --- argument_set ---

        [Test]
        public void ArgumentSet_LiteralDozensToSessionWithAddress()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["argument"] = "Text",
                ["value"] = "hello"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("применена"), result);
                Assert.That(result, Does.Contain("аргументов задано: 1"), result);
                Assert.That(result, Does.Contain("root/Activities[0]"), result);
                Assert.That(service.Arguments, Has.Count.EqualTo(1));

                WorkflowArgumentSetRequest request = service.Arguments[0];
                Assert.That(request.ElementId, Is.EqualTo("root/Activities[0]"));
                Assert.That(request.Argument, Is.EqualTo("Text"));
                Assert.That(request.Value, Is.EqualTo("hello"));
                Assert.That(request.ValueSpecified, Is.True, "литерал обязан прийти помеченным");
                Assert.That(request.HasExpression, Is.False);
            });
        }

        /// <summary>Пустая строка — это литерал, а не «значение не задано»: иначе текст не обнулить.</summary>
        [Test]
        public void ArgumentSet_EmptyStringLiteralIsKept()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["argument"] = "Text",
                ["value"] = ""
            });

            Assert.Multiple(() =>
            {
                Assert.That(service.Arguments, Has.Count.EqualTo(1));
                Assert.That(service.Arguments[0].ValueSpecified, Is.True, "пустая строка — тоже литерал");
                Assert.That(service.Arguments[0].Value, Is.Empty);
            });
        }

        /// <summary>Число и логическое значение приходят текстом: конвертацию в тип делает фабрика палитры.</summary>
        [Test]
        public void ArgumentSet_NumericAndBooleanValuesBecomeText()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["argument"] = "Text",
                ["value"] = 42
            });

            Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["argument"] = "Text",
                ["value"] = false
            });

            Assert.Multiple(() =>
            {
                Assert.That(service.Arguments[0].Value, Is.EqualTo("42"));
                Assert.That(service.Arguments[1].Value, Is.EqualTo("False").IgnoreCase
                    .Or.EqualTo("false"), "логическое значение приходит текстом");
            });
        }

        [Test]
        public void ArgumentSet_ExpressionAndLanguageReachSession()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["argument"] = "Text",
                ["expression"] = "text.ToUpper()",
                ["language"] = "csharp"
            });

            Assert.Multiple(() =>
            {
                Assert.That(service.Arguments, Has.Count.EqualTo(1));
                Assert.That(service.Arguments[0].HasExpression, Is.True);
                Assert.That(service.Arguments[0].Expression, Is.EqualTo("text.ToUpper()"));
                Assert.That(service.Arguments[0].ValueSpecified, Is.False, "expression не подменяется литералом");
                Assert.That(service.Arguments[0].Language, Is.EqualTo("csharp"));
            });
        }

        [Test]
        public void ArgumentSet_RequiresElementIdAndArgument()
        {
            AgentToolRegistry registry = CreateFactory(new RecordingEditingService()).CreateRegistry();

            string noElement = Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["argument"] = "Text",
                ["value"] = "hello"
            });

            string noArgument = Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["value"] = "hello"
            });

            Assert.Multiple(() =>
            {
                Assert.That(noElement, Does.Contain("Missing \"elementId\""), noElement);
                Assert.That(noArgument, Does.Contain("Missing \"argument\""), noArgument);
            });
        }

        [Test]
        public void ArgumentSet_Batch_AppliesAllInOneEdit()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["arguments"] = new JArray
                {
                    new JObject { ["elementId"] = "root/Activities[0]", ["argument"] = "Text", ["value"] = "first" },
                    new JObject { ["elementId"] = "root/Activities[1]", ["argument"] = "DisplayName", ["value"] = "block" }
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("аргументов задано: 2"), result);
                Assert.That(result, Does.Contain("root/Activities[0], root/Activities[1]"),
                    "ответ обязан перечислить адреса пачки: " + result);
                Assert.That(service.Sessions, Is.EqualTo(1), "пачка — одна сессия правок");
                Assert.That(service.Arguments, Has.Count.EqualTo(2));
            });
        }

        /// <summary>Отказ второй правки отменяет всю пачку: «половины аргументов» в модели не остаётся.</summary>
        [Test]
        public void ArgumentSet_BatchSecondFails_NothingApplied()
        {
            var service = new RecordingEditingService();
            service.FailingArguments.Add("Condition");

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["arguments"] = new JArray
                {
                    new JObject { ["elementId"] = "root/Activities[0]", ["argument"] = "Text", ["value"] = "hello" },
                    new JObject { ["elementId"] = "root/Activities[1]", ["argument"] = "Condition", ["expression"] = "x > 1" }
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("не выполнена"), result);
                Assert.That(result, Does.Contain("пачка отменена целиком"), result);
                Assert.That(service.Reverts, Is.EqualTo(1), "пачка обязана быть отменена");
                Assert.That(service.Arguments, Is.Empty, "ни один аргумент не должен остаться в модели: " + result);
            });
        }

        [Test]
        public void ArgumentSet_ValidateOff_ReportsNotChecked()
        {
            AgentToolRegistry registry = CreateFactory(new RecordingEditingService()).CreateRegistry();

            string result = Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["argument"] = "Text",
                ["value"] = "hello",
                ["validate"] = "off"
            });

            Assert.That(result, Does.Contain("валидация: не проверялась"), result);
        }

        // --- variable_add ---

        [Test]
        public void VariableAdd_PassesNameTypeAndDefault()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "variable_add", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root",
                ["name"] = "counter",
                ["typeName"] = "Int32",
                ["value"] = "0"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("переменных добавлено: 1"), result);
                Assert.That(service.VariablesAdded, Has.Count.EqualTo(1));
                Assert.That(service.VariablesAdded[0].Name, Is.EqualTo("counter"));
                Assert.That(service.VariablesAdded[0].TypeName, Is.EqualTo("Int32"));
                Assert.That(service.VariablesAdded[0].ValueSpecified, Is.True);
                Assert.That(service.VariablesAdded[0].Value, Is.EqualTo("0"));
                Assert.That(service.VariablesAdded[0].ElementId, Is.EqualTo("root"));
            });
        }

        [Test]
        public void VariableAdd_RequiresName()
        {
            AgentToolRegistry registry = CreateFactory(new RecordingEditingService()).CreateRegistry();

            string result = Run(registry, "variable_add", new JObject
            {
                ["path"] = OpenPath,
                ["typeName"] = "String"
            });

            Assert.That(result, Does.Contain("Missing \"name\""), result);
        }

        [Test]
        public void VariableAdd_Batch_DeclaresAll()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "variable_add", new JObject
            {
                ["path"] = OpenPath,
                ["variables"] = new JArray
                {
                    new JObject { ["name"] = "total", ["typeName"] = "Int32" },
                    new JObject { ["name"] = "label", ["typeName"] = "String", ["expression"] = "\"a\" + \"b\"" }
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("переменных добавлено: 2"), result);
                Assert.That(service.VariablesAdded, Has.Count.EqualTo(2));
                Assert.That(service.VariablesAdded[1].HasExpression, Is.True);
            });
        }

        // --- variable_remove ---

        [Test]
        public void VariableRemove_ByName()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "variable_remove", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[1]",
                ["name"] = "counter"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("переменных удалено: 1"), result);
                Assert.That(service.VariablesRemoved, Has.Count.EqualTo(1));
                Assert.That(service.VariablesRemoved[0].Name, Is.EqualTo("counter"));
                Assert.That(service.VariablesRemoved[0].ElementId, Is.EqualTo("root/Activities[1]"));
            });
        }

        [Test]
        public void VariableRemove_AcceptsPlainNameList()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "variable_remove", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root",
                ["variables"] = new JArray { "first", "second" }
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("переменных удалено: 2"), result);
                Assert.That(service.VariablesRemoved.Select(item => item.Name), Is.EqualTo(new[] { "first", "second" }));
                Assert.That(service.VariablesRemoved.All(item => item.ElementId == "root"), Is.True,
                    "адрес владельца берётся из общего аргумента");
            });
        }

        [Test]
        public void VariableRemove_ObjectItemsOverrideElementId()
        {
            var service = new RecordingEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "variable_remove", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root",
                ["variables"] = new JArray
                {
                    new JObject { ["name"] = "inner", ["elementId"] = "root/Activities[1]" }
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(service.VariablesRemoved, Has.Count.EqualTo(1));
                Assert.That(service.VariablesRemoved[0].ElementId, Is.EqualTo("root/Activities[1]"));
            });
        }

        // --- Хост не подключил мост ---

        [Test]
        public void Tools_WithoutEditingService_ExplainWhy()
        {
            AgentToolRegistry registry = CreateFactory(service: null).CreateRegistry();

            string argument = Run(registry, "argument_set", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root",
                ["argument"] = "Text",
                ["value"] = "hello"
            });

            string variable = Run(registry, "variable_add", new JObject
            {
                ["path"] = OpenPath,
                ["name"] = "counter",
                ["typeName"] = "Int32"
            });

            Assert.Multiple(() =>
            {
                Assert.That(argument, Does.Contain("не подключён"), argument);
                Assert.That(argument, Does.Contain("отклонена"), "инструмент отвечает текстом, а не падением: " + argument);
                Assert.That(variable, Does.Contain("не подключён"), variable);
            });
        }

        // --- Инфраструктура ---

        private static string Run(AgentToolRegistry registry, string toolName, JObject arguments)
        {
            IAgentTool tool = registry.Find(toolName);
            Assert.That(tool, Is.Not.Null, "нет инструмента " + toolName);

            return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static AgentToolFactory CreateFactory(IWorkflowEditingService service)
        {
            return new AgentToolFactory(
                new StubAiProvider(),
                () => System.IO.Path.GetTempPath(),
                NullStudioDocumentService.Instance,
                new FakeBuildFeedbackService(),
                (question, options, ct) => Task.FromResult("answer"),
                (systemPrompt, task, ct) => Task.FromResult("sub-result"),
                enableSubagents: false,
                subagentMaxDepth: 2,
                getDocsDirectory: null,
                activityCatalog: null,
                workflowOutline: null,
                workflowEditing: service);
        }

        /// <summary>
        /// Мост-регистратор: запоминает запросы инструмента, «применяет» их только на <c>Complete</c>
        /// и умеет отказать на заданном аргументе — так проверяется атомарность пачки без конструктора.
        /// </summary>
        private sealed class RecordingEditingService : IWorkflowEditingService
        {
            public List<WorkflowArgumentSetRequest> Arguments { get; } = new List<WorkflowArgumentSetRequest>();

            public List<WorkflowVariableAddRequest> VariablesAdded { get; } = new List<WorkflowVariableAddRequest>();

            public List<WorkflowVariableRemoveRequest> VariablesRemoved { get; } = new List<WorkflowVariableRemoveRequest>();

            /// <summary>Имена аргументов, на которых сессия отвечает отказом.</summary>
            public HashSet<string> FailingArguments { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int Sessions { get; private set; }

            public int Completes { get; private set; }

            public int Reverts { get; internal set; }

            public IWorkflowEditSession BeginEdit(string path, string label = null)
            {
                Sessions++;

                if (!string.Equals((path ?? string.Empty).Trim(), OpenPath, StringComparison.OrdinalIgnoreCase))
                {
                    return new UnavailableWorkflowEditSession(
                        path,
                        label,
                        "файл не открыт в конструкторе студии: " + path,
                        "откройте файл вкладкой конструктора");
                }

                return new RecordingSession(this);
            }

            public WorkflowEditResult Undo(string path)
            {
                return WorkflowEditResult.Failure("undo", "отменять нечего").WithState(false);
            }

            public WorkflowEditResult Redo(string path)
            {
                return WorkflowEditResult.Failure("redo", "возвращать нечего").WithState(false);
            }

            public WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true)
            {
                var outline = new WorkflowOutline(OpenPath) { Ok = true };
                outline.Root = new WorkflowOutlineNode("root", string.Empty, 0, "Activities", "Sequence", "Main");
                outline.Root.AddChild(new WorkflowOutlineNode("root/Activities[0]", "root", 0, "Activities", "WriteLine", "Log"));
                outline.Root.AddChild(new WorkflowOutlineNode("root/Activities[1]", "root", 1, "Activities", "Sequence", "block"));
                outline.ActivityCount = 2;
                outline.ShownDepth = 3;

                return outline;
            }

            public bool IsOpenInDesigner(string path)
            {
                return string.Equals((path ?? string.Empty).Trim(), OpenPath, StringComparison.OrdinalIgnoreCase);
            }

            internal void Commit(RecordingSession session)
            {
                Completes++;

                foreach (WorkflowArgumentSetRequest request in session.PendingArguments)
                {
                    Arguments.Add(request);
                }

                foreach (WorkflowVariableAddRequest request in session.PendingVariablesAdded)
                {
                    VariablesAdded.Add(request);
                }

                foreach (WorkflowVariableRemoveRequest request in session.PendingVariablesRemoved)
                {
                    VariablesRemoved.Add(request);
                }
            }
        }

        private sealed class RecordingSession : IWorkflowEditSession
        {
            private readonly RecordingEditingService _owner;

            public RecordingSession(RecordingEditingService owner)
            {
                _owner = owner;
            }

            public List<WorkflowArgumentSetRequest> PendingArguments { get; } = new List<WorkflowArgumentSetRequest>();

            public List<WorkflowVariableAddRequest> PendingVariablesAdded { get; } = new List<WorkflowVariableAddRequest>();

            public List<WorkflowVariableRemoveRequest> PendingVariablesRemoved { get; } = new List<WorkflowVariableRemoveRequest>();

            public string FilePath => OpenPath;

            public string Label => "тест";

            public bool IsActive { get; private set; } = true;

            public WorkflowEditResult Insert(WorkflowInsertRequest request)
            {
                return NotUsed("insert");
            }

            public WorkflowEditResult Delete(string elementId)
            {
                return NotUsed("delete");
            }

            public WorkflowEditResult Move(WorkflowMoveRequest request)
            {
                return NotUsed("move");
            }

            public WorkflowEditResult Wrap(WorkflowWrapRequest request)
            {
                return NotUsed("wrap");
            }

            public WorkflowEditResult SetArgument(WorkflowArgumentSetRequest request)
            {
                if (!IsActive)
                {
                    return WorkflowEditResult.Failure("argument_set", "пачка закрыта").WithState(false);
                }

                if (_owner.FailingArguments.Contains(request.Argument ?? string.Empty))
                {
                    return WorkflowEditResult.Failure(
                            "argument_set",
                            "у активности нет аргумента «" + request.Argument + "»")
                        .WithElements(request.ElementId)
                        .WithState(true);
                }

                PendingArguments.Add(request);

                return WorkflowEditResult.Success("argument_set", "«" + request.Argument + "» задан")
                    .WithElements(request.ElementId)
                    .WithState(true);
            }

            public WorkflowEditResult AddVariable(WorkflowVariableAddRequest request)
            {
                PendingVariablesAdded.Add(request);

                return WorkflowEditResult.Success("variable_add", "переменная «" + request.Name + "» объявлена")
                    .WithElements(request.ElementId)
                    .WithState(true);
            }

            public WorkflowEditResult RemoveVariable(WorkflowVariableRemoveRequest request)
            {
                PendingVariablesRemoved.Add(request);

                return WorkflowEditResult.Success("variable_remove", "переменная «" + request.Name + "» удалена")
                    .WithElements(request.ElementId)
                    .WithState(true);
            }

            public WorkflowEditResult Complete()
            {
                IsActive = false;
                _owner.Commit(this);

                return WorkflowEditResult.Success("complete", "пачка закрыта")
                    .WithState(false, true, false)
                    .WithOutline(_owner.GetLiveOutline(FilePath));
            }

            public WorkflowEditResult Revert()
            {
                IsActive = false;
                _owner.Reverts++;

                PendingArguments.Clear();
                PendingVariablesAdded.Clear();
                PendingVariablesRemoved.Clear();

                return WorkflowEditResult.Success("revert", "пачка отменена")
                    .WithState(false)
                    .WithOutline(_owner.GetLiveOutline(FilePath));
            }

            public WorkflowOutline Outline(int depth = 0, bool includeArguments = true)
            {
                return _owner.GetLiveOutline(FilePath, depth, includeArguments);
            }

            public void Dispose()
            {
                if (IsActive)
                {
                    Revert();
                }
            }

            private static WorkflowEditResult NotUsed(string operation)
            {
                return WorkflowEditResult.Failure(operation, "операция не используется в этом тесте").WithState(false);
            }
        }

        private sealed class FakeBuildFeedbackService : IBuildFeedbackService
        {
            public Task BuildAsync(string configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());

            public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);
        }
    }
}
