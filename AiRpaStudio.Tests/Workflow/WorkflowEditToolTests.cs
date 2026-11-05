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
    /// AG-04 + AG-09: инструменты <c>activity_insert</c>/<c>activity_delete</c>/<c>activity_move</c>/
    /// <c>activity_wrap</c> и атомарность с валидацией.
    /// <para>
    /// Проверяется оркестрация, а не конструктор: фейковая сессия правок повторяет контракт (адреса,
    /// пачка, откат) и «валидацию» с заданными ошибками. Поэтому здесь видно то, что нельзя увидеть на
    /// живой модели: «вторая правка пачки не применилась — модель не изменилась», «strict откатывает
    /// правку из-за новой ошибки», «report оставляет правку и показывает адрес ошибки».
    /// Живой конструктор проверяют <c>WorkflowEditBridgeTests</c> и <c>WorkflowWrapBridgeTests</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WorkflowEditToolTests
    {
        private const string OpenPath = @"C:\work\Main.xaml";

        // --- Реестр инструментов ---

        [Test]
        public void Registry_ContainsEditingTools()
        {
            AgentToolRegistry registry = CreateFactory(new FakeEditingService()).CreateRegistry();

            Assert.Multiple(() =>
            {
                Assert.That(registry.Find("activity_insert"), Is.Not.Null, "нет инструмента activity_insert");
                Assert.That(registry.Find("activity_delete"), Is.Not.Null, "нет инструмента activity_delete");
                Assert.That(registry.Find("activity_move"), Is.Not.Null, "нет инструмента activity_move");
                Assert.That(registry.Find("activity_wrap"), Is.Not.Null, "нет инструмента activity_wrap");
                Assert.That(registry.Find("activity_undo"), Is.Not.Null, "нет инструмента activity_undo");
                Assert.That(registry.Find("activity_redo"), Is.Not.Null, "нет инструмента activity_redo");
            });
        }

        [Test]
        public void EditingTools_AreMutating_AndHiddenInPlan()
        {
            AgentToolRegistry registry = CreateFactory(new FakeEditingService()).CreateRegistry();
            List<string> plan = registry.ForMode(AgentMode.Plan, AgentPermissions.All)
                .Select(tool => tool.Name)
                .ToList();

            foreach (var name in new[] { "activity_insert", "activity_delete", "activity_move", "activity_wrap", "activity_undo", "activity_redo" })
            {
                Assert.Multiple(() =>
                {
                    Assert.That(registry.Find(name).Mutates, Is.True, name + " обязан быть мутирующим");
                    Assert.That(registry.Find(name).Permission, Is.EqualTo(AgentPermissions.EditFiles),
                        name + " правит процесс — категория EditFiles");
                    Assert.That(plan, Does.Not.Contain(name), name + " не должен быть доступен в Plan");
                });
            }
        }

        // --- Вставка ---

        [Test]
        public void Insert_ReturnsAddressesAndValidationSummary()
        {
            var service = new FakeEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "WriteLine",
                ["displayName"] = "Log first step"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("применена"), result);
                Assert.That(result, Does.Contain("вставлено активностей: 1"), result);
                Assert.That(result, Does.Contain("root/Activities[0]"), "адрес новой активности обязан быть в ответе: " + result);
                Assert.That(result, Does.Contain("сводка:"), result);
                Assert.That(service.ActivityCount, Is.EqualTo(1), "активность обязана оказаться в модели");
                Assert.That(service.Root.Children[0].DisplayName, Is.EqualTo("Log first step"), "подпись задаётся одной пачкой");
            });
        }

        [Test]
        public void Insert_Multiple_AppliesWholeBatchAndListsAllAddresses()
        {
            var service = new FakeEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activities"] = new JArray
                {
                    new JObject { ["activityType"] = "WriteLine" },
                    new JObject { ["activityType"] = "WriteLine" },
                    new JObject { ["activityType"] = "Sequence" }
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("вставлено активностей: 3"), result);
                Assert.That(result, Does.Contain("root/Activities[0], root/Activities[1], root/Activities[2]"),
                    "ответ обязан перечислить все адреса пачки: " + result);
                Assert.That(service.ActivityCount, Is.EqualTo(3));
            });
        }

        /// <summary>Атомарность: вторая правка пачки не применилась — модель не изменилась вовсе.</summary>
        [Test]
        public void Insert_BatchSecondFails_NothingApplied()
        {
            var service = new FakeEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activities"] = new JArray
                {
                    new JObject { ["activityType"] = "WriteLine" },
                    new JObject { ["activityType"] = "WriteLine", ["parent"] = "root/Activities[7]" }
                }
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("не выполнена"), result);
                Assert.That(result, Does.Contain("элемент «root/Activities[7]» не найден"), result);
                Assert.That(result, Does.Contain("пачка отменена целиком"), "агент обязан знать, что ничего не применилось: " + result);
                Assert.That(service.ActivityCount, Is.EqualTo(0),
                    "первая правка пачки тоже отменяется: " + result);
                Assert.That(service.UndoDepth, Is.EqualTo(0), "история отмены не должна расти после откката пачки");
            });
        }

        // --- Валидация (AG-09) ---

        [Test]
        public void Strict_RevertsWhenNewValidationErrorAppears()
        {
            var service = new FakeEditingService();
            service.InvalidTypes.Add("If");

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "If",
                ["validate"] = "strict"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("не выполнена"), result);
                Assert.That(result, Does.Contain("новых ошибок валидации 1"), result);
                Assert.That(result, Does.Contain("root/Activities[0]"), "ошибка приходит с адресом узла: " + result);
                Assert.That(service.ActivityCount, Is.EqualTo(0), "strict откатывает правку: " + result);
            });
        }

        [Test]
        public void Report_KeepsEditAndListsOnlyNewErrors()
        {
            var service = new FakeEditingService();
            service.InvalidTypes.Add("If");

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "If"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("применена"), result);
                Assert.That(result, Does.Contain("новых 1"), result);
                Assert.That(result, Does.Contain("обязательный аргумент"), "текст ошибки валидации обязан быть в ответе: " + result);
                Assert.That(service.ActivityCount, Is.EqualTo(1), "report оставляет правку: " + result);
            });
        }

        [Test]
        public void Report_ExistingErrorIsNotCountedAsNew()
        {
            var service = new FakeEditingService();
            service.InvalidTypes.Add("If");
            service.Root.Children.Add(new FakeNode("If", "старый If"));

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "WriteLine"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("применена"), result);
                Assert.That(result, Does.Contain("ошибок 1"), "старая ошибка видна в общем счёте: " + result);
                Assert.That(result, Does.Contain("новых 0"), "чужая ошибка не считается новой: " + result);
            });
        }

        [Test]
        public void Validation_Off_SkipsValidation()
        {
            var service = new FakeEditingService();
            service.InvalidTypes.Add("If");

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "If",
                ["validate"] = "off"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("применена"), result);
                Assert.That(result, Does.Contain("валидация: не проверялась"), result);
                Assert.That(service.ActivityCount, Is.EqualTo(1));
            });
        }

        // --- Удаление, перенос, обёртка ---

        [Test]
        public void Delete_RemovesByOutlineAddress()
        {
            var service = new FakeEditingService();
            service.Root.Children.Add(new FakeNode("WriteLine", "первый"));
            service.Root.Children.Add(new FakeNode("WriteLine", "второй"));

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_delete", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("удалено: 1"), result);
                Assert.That(service.ActivityCount, Is.EqualTo(1), result);
                Assert.That(service.Root.Children[0].DisplayName, Is.EqualTo("второй"), "удалён именно первый элемент");
            });
        }

        [Test]
        public void Delete_UnknownAddress_StructuredFailure()
        {
            var service = new FakeEditingService();
            service.Root.Children.Add(new FakeNode("WriteLine", "первый"));

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_delete", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[9]"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("не выполнена"), result);
                Assert.That(result, Does.Contain("не найден"), result);
                Assert.That(result, Does.Contain("подсказ") .Or.Contain("повторите"), "ответ обязан подсказывать, что делать: " + result);
                Assert.That(service.ActivityCount, Is.EqualTo(1), "модель не изменилась");
            });
        }

        [Test]
        public void Move_ReturnsNewAddress()
        {
            var service = new FakeEditingService();
            service.Root.Children.Add(new FakeNode("Sequence", "block"));
            service.Root.Children.Add(new FakeNode("WriteLine", "step"));

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_move", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[1]",
                ["parent"] = "root/Activities[0]"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("перенесено: 1"), result);
                Assert.That(result, Does.Contain("root/Activities[0]/Activities[0]"), "адрес перенесённого элемента: " + result);
                Assert.That(service.Root.Children.Count, Is.EqualTo(1));
                Assert.That(service.Root.Children[0].Children[0].DisplayName, Is.EqualTo("step"));
            });
        }

        [Test]
        public void Wrap_NestsActivity_AndReportsBothAddresses()
        {
            var service = new FakeEditingService();
            service.Root.Children.Add(new FakeNode("WriteLine", "step"));

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_wrap", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]",
                ["wrapperType"] = "Sequence",
                ["displayName"] = "guarded"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("обёрнуто: 1"), result);
                Assert.That(result, Does.Contain("root/Activities[0]"), "адрес обёртки: " + result);
                Assert.That(result, Does.Contain("root/Activities[0]/Activities[0]"), "новый адрес обёрнутой активности: " + result);
                Assert.That(service.Root.Children[0].TypeName, Is.EqualTo("Sequence"), "обёртка заняла место элемента");
                Assert.That(service.Root.Children[0].Children[0].DisplayName, Is.EqualTo("step"), "элемент стал первым ребёнком обёртки");
            });
        }

        [Test]
        public void Wrap_MissingWrapperType_IsRefused()
        {
            var service = new FakeEditingService();
            service.Root.Children.Add(new FakeNode("WriteLine", "step"));

            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_wrap", new JObject
            {
                ["path"] = OpenPath,
                ["elementId"] = "root/Activities[0]"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("wrapperType"), result);
                Assert.That(service.Root.Children[0].TypeName, Is.EqualTo("WriteLine"), "модель не изменилась");
            });
        }

        // --- Отказы на границах ---

        [Test]
        public void NotOpenFile_ReportsReasonWithHint()
        {
            var service = new FakeEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = @"C:\work\NotOpened.xaml",
                ["activityType"] = "WriteLine"
            });

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("не выполнена"), result);
                Assert.That(result, Does.Contain("не открыт"), "агент обязан увидеть причину: " + result);
                Assert.That(service.ActivityCount, Is.EqualTo(0));
            });
        }

        [Test]
        public void MissingService_ExplainsUnavailableInsteadOfThrowing()
        {
            // Хост не подключил мост: чат обязан объяснить это текстом, а не упасть.
            AgentToolRegistry registry = CreateFactory(NullWorkflowEditingService.Instance).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "WriteLine"
            });

            Assert.That(result, Does.Contain("не подключён"), "ответ обязан объяснять отсутствие моста: " + result);
        }

        [Test]
        public void MissingPath_IsRefused()
        {
            AgentToolRegistry registry = CreateFactory(new FakeEditingService()).CreateRegistry();

            string result = Run(registry, "activity_delete", new JObject { ["elementId"] = "root/Activities[0]" });

            Assert.That(result, Does.Contain("Missing or empty \"path\""), result);
        }

        [Test]
        public void MissingActivityType_IsRefused()
        {
            AgentToolRegistry registry = CreateFactory(new FakeEditingService()).CreateRegistry();

            string result = Run(registry, "activity_insert", new JObject { ["path"] = OpenPath });

            Assert.That(result, Does.Contain("activityType"), result);
        }

        [Test]
        public void InvalidIndexAndValidate_AreRefused()
        {
            AgentToolRegistry registry = CreateFactory(new FakeEditingService()).CreateRegistry();

            string badIndex = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "WriteLine",
                ["index"] = "первый"
            });

            string badValidate = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "WriteLine",
                ["validate"] = "maybe"
            });

            Assert.Multiple(() =>
            {
                Assert.That(badIndex, Does.Contain("Invalid \"index\""), badIndex);
                Assert.That(badValidate, Does.Contain("Invalid \"validate\""), badValidate);
            });
        }

        [Test]
        public void IncludeOutline_False_HidesStructure()
        {
            var service = new FakeEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            string withOutline = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "WriteLine"
            });

            string withoutOutline = Run(registry, "activity_insert", new JObject
            {
                ["path"] = OpenPath,
                ["activityType"] = "WriteLine",
                ["include_outline"] = false
            });

            Assert.Multiple(() =>
            {
                Assert.That(withOutline, Does.Contain("Структура процесса"), "по умолчанию структура приходит сразу: " + withOutline);
                Assert.That(withoutOutline, Does.Not.Contain("Структура процесса"), "include_outline=false экономит контекст: " + withoutOutline);
            });
        }

        // --- Отмена и возврат ---

        [Test]
        public void UndoRedo_Tools_RestoreModel()
        {
            var service = new FakeEditingService();
            AgentToolRegistry registry = CreateFactory(service).CreateRegistry();

            Run(registry, "activity_insert", new JObject { ["path"] = OpenPath, ["activityType"] = "WriteLine" });
            string undo = Run(registry, "activity_undo", new JObject { ["path"] = OpenPath });
            int afterUndo = service.ActivityCount;
            string redo = Run(registry, "activity_redo", new JObject { ["path"] = OpenPath });

            Assert.Multiple(() =>
            {
                Assert.That(undo, Does.Contain("undo"), undo);
                Assert.That(afterUndo, Is.EqualTo(0), "Ctrl+Z снимает одну правку");
                Assert.That(redo, Does.Contain("redo"), redo);
                Assert.That(service.ActivityCount, Is.EqualTo(1), "Ctrl+Y возвращает правку");
            });
        }

        // --- Сравнение валидации ---

        [Test]
        public void ValidationDelta_ReportsNewAndFixedErrors()
        {
            WorkflowOutline before = OutlineWithErrors("первая");
            WorkflowOutline after = OutlineWithErrors("первая", "вторая");

            WorkflowValidationDelta delta = WorkflowValidationDelta.Compare(before, after);

            Assert.Multiple(() =>
            {
                Assert.That(delta.Available, Is.True);
                Assert.That(delta.BaselineErrors, Is.EqualTo(1));
                Assert.That(delta.Errors, Is.EqualTo(2));
                Assert.That(delta.NewErrors, Has.Count.EqualTo(1), "новая только вторая ошибка");
                Assert.That(delta.NewErrors[0].Message, Is.EqualTo("вторая"));
                Assert.That(delta.HasNewErrors, Is.True);
                Assert.That(delta.Summary, Does.Contain("новых 1"));
            });
        }

        [Test]
        public void ValidationDelta_FixedErrorsAreNotNew()
        {
            WorkflowOutline before = OutlineWithErrors("первая", "вторая");
            WorkflowOutline after = OutlineWithErrors("вторая");

            WorkflowValidationDelta delta = WorkflowValidationDelta.Compare(before, after);

            Assert.Multiple(() =>
            {
                Assert.That(delta.NewErrors, Is.Empty);
                Assert.That(delta.FixedErrors, Has.Count.EqualTo(1), "исчезнувшая ошибка — исправленная, а не новая");
                Assert.That(delta.Summary, Does.Contain("исправлено 1"));
            });
        }

        [Test]
        public void ValidationDelta_UnavailableWhenOutlineIsMissing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(WorkflowValidationDelta.Compare(null, OutlineWithErrors("x")).Available, Is.False);
                Assert.That(WorkflowValidationDelta.Compare(OutlineWithErrors("x"), null).Available, Is.False);
                Assert.That(WorkflowValidationDelta.Unavailable.Summary, Does.Contain("недоступна"));
            });
        }

        /// <summary>Адреса ошибок могут сместиться — сравнение идёт по тексту, а не по <c>Id</c>.</summary>
        [Test]
        public void ValidationDelta_MatchesErrorsByTextNotByAddress()
        {
            WorkflowOutline before = OutlineWithErrorsAt("root/Activities[2]", "одинаковая");
            WorkflowOutline after = OutlineWithErrorsAt("root/Activities[3]", "одинаковая");

            WorkflowValidationDelta delta = WorkflowValidationDelta.Compare(before, after);

            Assert.That(delta.NewErrors, Is.Empty, "сдвиг индексов не создаёт «новых» ошибок: " + delta.Summary);
        }

        // --- Инфраструктура теста ---

        private static WorkflowOutline OutlineWithErrors(params string[] messages)
        {
            var outline = new WorkflowOutline("C:\\work\\Main.xaml") { Ok = true };

            for (int index = 0; index < messages.Length; index++)
            {
                outline.AddError("root/Activities[" + index + "]", messages[index]);
            }

            return outline;
        }

        private static WorkflowOutline OutlineWithErrorsAt(string elementId, string message)
        {
            var outline = new WorkflowOutline("C:\\work\\Main.xaml") { Ok = true };
            outline.AddError(elementId, message);
            return outline;
        }

        private static string Run(AgentToolRegistry registry, string toolName, JObject arguments)
        {
            IAgentTool tool = registry.Find(toolName);
            Assert.That(tool, Is.Not.Null, "нет инструмента " + toolName);

            return tool.ExecuteAsync(arguments, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static AgentToolFactory CreateFactory(IWorkflowEditingService editing)
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
                workflowEditing: editing);
        }

        /// <summary>Узел «модели» фейка: тип, подпись и дети.</summary>
        private sealed class FakeNode
        {
            public FakeNode(string typeName, string displayName = null)
            {
                TypeName = typeName ?? string.Empty;
                DisplayName = displayName;
            }

            public string TypeName { get; }

            public string DisplayName { get; }

            public List<FakeNode> Children { get; } = new List<FakeNode>();

            /// <summary>Заданные аргументы: имя → текст значения (литерал или выражение).</summary>
            public Dictionary<string, string> Arguments { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Объявленные переменные: имя → тип.</summary>
            public Dictionary<string, string> Variables { get; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public FakeNode Clone()
            {
                var copy = new FakeNode(TypeName, DisplayName);

                foreach (FakeNode child in Children)
                {
                    copy.Children.Add(child.Clone());
                }

                foreach (KeyValuePair<string, string> argument in Arguments)
                {
                    copy.Arguments[argument.Key] = argument.Value;
                }

                foreach (KeyValuePair<string, string> variable in Variables)
                {
                    copy.Variables[variable.Key] = variable.Value;
                }

                return copy;
            }
        }

        /// <summary>
        /// Фейковый мост правок: держит дерево в памяти, отдаёт адреса в формате outline, умеет откат
        /// пачки и историю отмены. «Валидация» — узлы типов из <see cref="InvalidTypes"/> дают ошибку.
        /// </summary>
        private sealed class FakeEditingService : IWorkflowEditingService
        {
            private readonly List<FakeNode> _undo = new List<FakeNode>();
            private readonly List<FakeNode> _redo = new List<FakeNode>();

            public FakeEditingService(string openPath = null)
            {
                OpenPath = string.IsNullOrWhiteSpace(openPath) ? WorkflowEditToolTests.OpenPath : openPath;
                Root = new FakeNode("Sequence", "Main");
            }

            public string OpenPath { get; }

            public FakeNode Root { get; private set; }

            /// <summary>Типы, узлы которых «не проходят валидацию».</summary>
            public HashSet<string> InvalidTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public int ActivityCount => Count(Root) - 1;

            public int UndoDepth => _undo.Count;

            public IWorkflowEditSession BeginEdit(string path, string label = null)
            {
                if (string.IsNullOrWhiteSpace(path) || !string.Equals(path.Trim(), OpenPath, StringComparison.OrdinalIgnoreCase))
                {
                    return new UnavailableWorkflowEditSession(
                        path,
                        label,
                        "файл не открыт в конструкторе студии: " + path,
                        "откройте файл вкладкой конструктора");
                }

                return new FakeEditSession(this, label);
            }

            public WorkflowEditResult Undo(string path)
            {
                if (_undo.Count == 0)
                {
                    return WorkflowEditResult.Failure("undo", "отменять нечего").WithState(false);
                }

                _redo.Add(Root.Clone());
                Root = Pop(_undo);

                return WorkflowEditResult.Success("undo", "правка отменена")
                    .WithState(false, _undo.Count > 0, _redo.Count > 0)
                    .WithOutline(BuildOutline());
            }

            public WorkflowEditResult Redo(string path)
            {
                if (_redo.Count == 0)
                {
                    return WorkflowEditResult.Failure("redo", "возвращать нечего").WithState(false);
                }

                _undo.Add(Root.Clone());
                Root = Pop(_redo);

                return WorkflowEditResult.Success("redo", "правка возвращена")
                    .WithState(false, _undo.Count > 0, _redo.Count > 0)
                    .WithOutline(BuildOutline());
            }

            public WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true)
            {
                return BuildOutline();
            }

            public bool IsOpenInDesigner(string path)
            {
                return string.Equals((path ?? string.Empty).Trim(), OpenPath, StringComparison.OrdinalIgnoreCase);
            }

            internal WorkflowOutline BuildOutline()
            {
                var outline = new WorkflowOutline(OpenPath) { Ok = true };
                outline.Root = BuildNode(Root, "root", string.Empty, 0, outline);
                outline.ActivityCount = ActivityCount;
                outline.ShownDepth = 3;

                return outline;
            }

            internal WorkflowOutlineNode BuildNode(FakeNode node, string id, string parentId, int index, WorkflowOutline outline)
            {
                var item = new WorkflowOutlineNode(id, parentId, index, "Activities", node.TypeName, node.DisplayName);

                if (InvalidTypes.Contains(node.TypeName))
                {
                    outline.AddError(id, "узел «" + node.TypeName + "»: обязательный аргумент не задан", "validation");
                }

                for (int position = 0; position < node.Children.Count; position++)
                {
                    item.AddChild(BuildNode(
                        node.Children[position],
                        id + "/Activities[" + position + "]",
                        id,
                        position,
                        outline));
                }

                item.SetChildCount(node.Children.Count);
                return item;
            }

            internal void Save()
            {
                _undo.Add(Root.Clone());
                _redo.Clear();
            }

            internal void Rollback(int edits)
            {
                for (int index = 0; index < edits && _undo.Count > 0; index++)
                {
                    Root = Pop(_undo);
                }
            }

            internal FakeNode Find(string elementId)
            {
                string id = (elementId ?? string.Empty).Trim();

                if (id.Length == 0 || string.Equals(id, WorkflowEditing.RootElementId, StringComparison.OrdinalIgnoreCase))
                {
                    return Root;
                }

                return TryLocate(id, out FakeNode parent, out int index) ? parent.Children[index] : null;
            }

            internal bool TryLocate(string elementId, out FakeNode parent, out int index)
            {
                parent = null;
                index = -1;

                string id = (elementId ?? string.Empty).Trim();
                int slash = id.LastIndexOf('/');

                if (slash <= 0)
                {
                    return false;
                }

                string parentId = id.Substring(0, slash);
                string segment = id.Substring(slash + 1);
                int bracket = segment.IndexOf('[');

                if (bracket <= 0 || !segment.EndsWith("]", StringComparison.Ordinal))
                {
                    return false;
                }

                if (!string.Equals(segment.Substring(0, bracket), "Activities", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!int.TryParse(segment.Substring(bracket + 1, segment.Length - bracket - 2), out index))
                {
                    return false;
                }

                parent = Find(parentId);
                return parent != null && index >= 0 && index < parent.Children.Count;
            }

            internal static string AddressOf(string parentId, int index)
            {
                string parent = string.IsNullOrWhiteSpace(parentId) ? WorkflowEditing.RootElementId : parentId.Trim();
                return parent + "/Activities[" + index + "]";
            }

            private static FakeNode Pop(List<FakeNode> stack)
            {
                FakeNode node = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                return node;
            }

            private static int Count(FakeNode node)
            {
                int count = 1;

                foreach (FakeNode child in node.Children)
                {
                    count += Count(child);
                }

                return count;
            }
        }

        private sealed class FakeEditSession : IWorkflowEditSession
        {
            private readonly FakeEditingService _owner;
            private int _edits;

            public FakeEditSession(FakeEditingService owner, string label)
            {
                _owner = owner;
                FilePath = owner.OpenPath;
                Label = string.IsNullOrWhiteSpace(label) ? "тест" : label;
                IsActive = true;
            }

            public string FilePath { get; }

            public string Label { get; }

            public bool IsActive { get; private set; }

            public WorkflowEditResult Insert(WorkflowInsertRequest request)
            {
                return Apply("insert", () =>
                {
                    if (string.IsNullOrWhiteSpace(request?.ActivityType))
                    {
                        return WorkflowEditResult.Failure("insert", "не задан тип активности");
                    }

                    FakeNode parent = _owner.Find(request.ParentId);
                    if (parent == null)
                    {
                        return WorkflowEditResult.Failure(
                                "insert",
                                "элемент «" + request.ParentId + "» не найден в модели конструктора")
                            .WithElements(request.ParentId);
                    }

                    int index = request.Index ?? parent.Children.Count;
                    if (index < 0 || index > parent.Children.Count)
                    {
                        index = parent.Children.Count;
                    }

                    parent.Children.Insert(index, new FakeNode(request.ActivityType, request.DisplayName));

                    return WorkflowEditResult.Success("insert", "вставлено в «" + parent.TypeName + ".Activities»")
                        .WithElements(request.ParentId, FakeEditingService.AddressOf(request.ParentId, index));
                });
            }

            public WorkflowEditResult Delete(string elementId)
            {
                return Apply("delete", () =>
                {
                    if (!_owner.TryLocate(elementId, out FakeNode parent, out int index))
                    {
                        return WorkflowEditResult.Failure(
                                "delete",
                                "элемент «" + elementId + "» не найден в модели конструктора",
                                "адреса берутся из workflow_outline")
                            .WithElements(elementId);
                    }

                    parent.Children.RemoveAt(index);
                    return WorkflowEditResult.Success("delete", "удалено: " + elementId).WithElements(elementId);
                });
            }

            public WorkflowEditResult Move(WorkflowMoveRequest request)
            {
                return Apply("move", () =>
                {
                    if (!_owner.TryLocate(request.ElementId, out FakeNode source, out int sourceIndex))
                    {
                        return WorkflowEditResult.Failure(
                                "move",
                                "элемент «" + request.ElementId + "» не найден в модели конструктора")
                            .WithElements(request.ElementId);
                    }

                    FakeNode target = _owner.Find(request.ParentId);
                    if (target == null)
                    {
                        return WorkflowEditResult.Failure(
                                "move",
                                "элемент «" + request.ParentId + "» не найден в модели конструктора")
                            .WithElements(request.ElementId);
                    }

                    FakeNode moved = source.Children[sourceIndex];
                    source.Children.RemoveAt(sourceIndex);

                    int index = request.Index ?? target.Children.Count;
                    if (index < 0 || index > target.Children.Count)
                    {
                        index = target.Children.Count;
                    }

                    target.Children.Insert(index, moved);

                    return WorkflowEditResult.Success("move", "перенесено")
                        .WithElements(request.ElementId, FakeEditingService.AddressOf(request.ParentId, index));
                });
            }

            public WorkflowEditResult Wrap(WorkflowWrapRequest request)
            {
                return Apply("wrap", () =>
                {
                    if (string.IsNullOrWhiteSpace(request?.WrapperType))
                    {
                        return WorkflowEditResult.Failure("wrap", "не задан тип обёртки (wrapperType)").WithElements(request?.ElementId);
                    }

                    if (!_owner.TryLocate(request.ElementId, out FakeNode parent, out int index))
                    {
                        return WorkflowEditResult.Failure(
                                "wrap",
                                "элемент «" + request.ElementId + "» не найден в модели конструктора")
                            .WithElements(request.ElementId);
                    }

                    FakeNode element = parent.Children[index];
                    var wrapper = new FakeNode(request.WrapperType, request.DisplayName);
                    wrapper.Children.Add(element);
                    parent.Children[index] = wrapper;

                    // Обёртка занимает место элемента, поэтому её адрес — прежний адрес элемента.
                    string wrapperAddress = request.ElementId.Trim();

                    return WorkflowEditResult.Success("wrap", "обёрнуто")
                        .WithElements(request.ElementId, wrapperAddress)
                        .WithApplied(1, new[] { wrapperAddress, wrapperAddress + "/Activities[0]" });
                });
            }

            /// <summary>Известные аргументы фейковых типов — как в каталоге активностей.</summary>
            private static readonly Dictionary<string, string[]> KnownArguments =
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    { "WriteLine", new[] { "Text" } },
                    { "If", new[] { "Condition" } },
                    { "Assign", new[] { "To", "Value" } },
                    { "InvokeMethod", new[] { "MethodName" } },
                    { "Sequence", new string[0] }
                };

            public WorkflowEditResult SetArgument(WorkflowArgumentSetRequest request)
            {
                return Apply("argument_set", () =>
                {
                    FakeNode element = _owner.Find(request?.ElementId);
                    if (element == null)
                    {
                        return WorkflowEditResult.Failure(
                                "argument_set",
                                "элемент «" + request?.ElementId + "» не найден в модели конструктора")
                            .WithElements(request?.ElementId);
                    }

                    string argument = KnownArgumentOf(element.TypeName, request.Argument);
                    if (argument == null)
                    {
                        return WorkflowEditResult.Failure(
                                "argument_set",
                                "у активности «" + element.TypeName + "» нет аргумента «" + request.Argument + "»")
                            .WithElements(request.ElementId);
                    }

                    if (request.HasExpression && request.ValueSpecified)
                    {
                        return WorkflowEditResult.Failure(
                                "argument_set",
                                "заданы и value, и expression: нужно ровно одно")
                            .WithElements(request.ElementId);
                    }

                    if (!request.HasExpression && !request.ValueSpecified)
                    {
                        return WorkflowEditResult.Failure(
                                "argument_set",
                                "не задано значение аргумента «" + argument + "»")
                            .WithElements(request.ElementId);
                    }

                    if (request.HasExpression)
                    {
                        string problems = WorkflowExpressionPolicy.Describe(
                            WorkflowExpressionPolicy.Check(request.Expression, WorkflowExpressionLanguage.CSharp));

                        if (problems.Length > 0)
                        {
                            return WorkflowEditResult.Failure("argument_set", "выражение не принято: " + problems)
                                .WithElements(request.ElementId);
                        }

                        element.Arguments[argument] = "= " + request.Expression.Trim();
                    }
                    else
                    {
                        element.Arguments[argument] = request.Value;
                    }

                    return WorkflowEditResult.Success(
                            "argument_set",
                            "«" + element.TypeName + "." + argument + "» задан")
                        .WithElements(request.ElementId);
                });
            }

            public WorkflowEditResult AddVariable(WorkflowVariableAddRequest request)
            {
                return Apply("variable_add", () =>
                {
                    FakeNode element = _owner.Find(request?.ElementId);
                    if (element == null)
                    {
                        return WorkflowEditResult.Failure(
                                "variable_add",
                                "элемент «" + request?.ElementId + "» не найден в модели конструктора")
                            .WithElements(request?.ElementId);
                    }

                    if (!OwnsVariables(element.TypeName))
                    {
                        return WorkflowEditResult.Failure(
                                "variable_add",
                                "у элемента «" + request.ElementId + "» нет коллекции переменных (Variables)")
                            .WithElements(request.ElementId);
                    }

                    if (element.Variables.ContainsKey(request.Name))
                    {
                        return WorkflowEditResult.Failure(
                                "variable_add",
                                "переменная «" + request.Name + "» уже объявлена у «" + request.ElementId + "»")
                            .WithElements(request.ElementId);
                    }

                    string typeName = string.IsNullOrWhiteSpace(request.TypeName) ? "String" : request.TypeName.Trim();
                    element.Variables[request.Name] = typeName;

                    return WorkflowEditResult.Success(
                            "variable_add",
                            "переменная «" + request.Name + "»: " + typeName + " объявлена в «" + request.ElementId + "»")
                        .WithElements(request.ElementId);
                });
            }

            public WorkflowEditResult RemoveVariable(WorkflowVariableRemoveRequest request)
            {
                return Apply("variable_remove", () =>
                {
                    FakeNode element = _owner.Find(request?.ElementId);
                    if (element == null)
                    {
                        return WorkflowEditResult.Failure(
                                "variable_remove",
                                "элемент «" + request?.ElementId + "» не найден в модели конструктора")
                            .WithElements(request?.ElementId);
                    }

                    if (!OwnsVariables(element.TypeName))
                    {
                        return WorkflowEditResult.Failure(
                                "variable_remove",
                                "у элемента «" + request.ElementId + "» нет коллекции переменных (Variables)")
                            .WithElements(request.ElementId);
                    }

                    if (!element.Variables.Remove(request.Name))
                    {
                        return WorkflowEditResult.Failure(
                                "variable_remove",
                                "переменной «" + request.Name + "» у «" + request.ElementId + "» нет")
                            .WithElements(request.ElementId);
                    }

                    return WorkflowEditResult.Success(
                            "variable_remove",
                            "переменная «" + request.Name + "» удалена из «" + request.ElementId + "»")
                        .WithElements(request.ElementId);
                });
            }

            private static string KnownArgumentOf(string typeName, string argument)
            {
                if (string.IsNullOrWhiteSpace(argument) || !KnownArguments.TryGetValue(typeName ?? string.Empty, out string[] names))
                {
                    return null;
                }

                foreach (string name in names)
                {
                    if (string.Equals(name, argument.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }

                return null;
            }

            /// <summary>Переменные объявляются там, где есть коллекция <c>Variables</c>.</summary>
            private static bool OwnsVariables(string typeName)
            {
                return string.Equals(typeName, "Sequence", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(typeName, "Flowchart", StringComparison.OrdinalIgnoreCase);
            }

            public WorkflowEditResult Complete()
            {
                if (!IsActive)
                {
                    return WorkflowEditResult.Failure("complete", "пачка уже закрыта").WithState(false);
                }

                IsActive = false;

                return WorkflowEditResult.Success("complete", "пачка закрыта: правок " + _edits)
                    .WithState(false, true, false)
                    .WithOutline(_owner.BuildOutline());
            }

            public WorkflowEditResult Revert()
            {
                int edits = _edits;
                _edits = 0;
                IsActive = false;
                _owner.Rollback(edits);

                return WorkflowEditResult.Success("revert", "пачка отменена: правок " + edits)
                    .WithState(false)
                    .WithOutline(_owner.BuildOutline());
            }

            public WorkflowOutline Outline(int depth = 0, bool includeArguments = true)
            {
                return _owner.BuildOutline();
            }

            public void Dispose()
            {
                if (IsActive)
                {
                    Revert();
                }
            }

            private WorkflowEditResult Apply(string operation, Func<WorkflowEditResult> action)
            {
                if (!IsActive)
                {
                    return WorkflowEditResult.Failure(operation, "пачка закрыта").WithState(false);
                }

                _owner.Save();
                WorkflowEditResult result = action();

                if (!result.Ok)
                {
                    _owner.Rollback(1);
                    return result.WithState(true);
                }

                _edits++;
                return result.WithState(true);
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
