using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Core.AgentTools;
using Agent.Core.Contracts;
using Agent.Core.Models;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RPA.Chat.Services;
using RPA.Chat.Services.AgentTools;
using RPA.Chat.Services.Spec;
using RPA.Chat.Services.Workflow;

namespace AiRpaStudio.Tests.Spec
{
    /// <summary>
    /// Волна 8 через реальный реестр инструментов: <c>spec_import</c> → <c>spec_extract</c> → <c>spec_decompose</c> →
    /// <c>spec_link</c>/<c>spec_coverage</c> → <c>spec_generate</c> → <c>spec_testcases</c>/<c>spec_run_tests</c>,
    /// плюс журнал неоднозначностей <c>spec_ask</c>/<c>spec_answer</c>. Хранилище, структура процесса,
    /// мост правки и прогонщик — подменённые: тест проверяет инструменты, а не WF.
    /// </summary>
    [TestFixture]
    public class SpecToolTests
    {
        private string _root;
        private FakeEditingService _editing;
        private FakeOutlineService _outline;
        private FakeRunService _run;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "spec-tools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            _editing = new FakeEditingService();
            _outline = new FakeOutlineService(CreateOutline());
            _run = new FakeRunService();

            ChatEnvironment.SpecStore = new SpecificationStore(() => _root);
            ChatEnvironment.WorkflowEditing = _editing;
            ChatEnvironment.WorkflowOutline = _outline;
            ChatEnvironment.WorkflowRun = _run;
            ChatEnvironment.ActivityCatalog = NullActivityCatalog.Instance;
        }

        [TearDown]
        public void TearDown()
        {
            ChatEnvironment.ResetSpecStore();
            ChatEnvironment.WorkflowEditing = NullWorkflowEditingService.Instance;
            ChatEnvironment.WorkflowOutline = NullWorkflowOutlineService.Instance;
            ChatEnvironment.WorkflowRun = NullWorkflowRunService.Instance;
            ChatEnvironment.ActivityCatalog = NullActivityCatalog.Instance;

            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------
        // Реестр
        // ------------------------------------------------------------------

        [Test]
        public void Registry_RegistersAllSpecificationTools()
        {
            var registry = CreateRegistry();
            var names = registry.ForMode(Agent.Core.Models.AgentMode.Act, Agent.Core.Models.AgentPermissions.All)
                .Where(tool => tool.Name.StartsWith("spec_", StringComparison.OrdinalIgnoreCase))
                .Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "spec_import", "spec_show", "spec_extract", "spec_update", "spec_decompose", "spec_link",
                "spec_coverage", "spec_generate", "spec_ask", "spec_answer", "spec_testcases", "spec_run_tests"
            }));
        }

        [Test]
        public void Registry_PlanModeSeesOnlyReadOnlySpecTools()
        {
            var registry = CreateRegistry();
            var names = registry.ForMode(Agent.Core.Models.AgentMode.Plan, Agent.Core.Models.AgentPermissions.All)
                .Where(tool => tool.Name.StartsWith("spec_", StringComparison.OrdinalIgnoreCase))
                .Select(tool => tool.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.That(names, Is.EquivalentTo(new[] { "spec_coverage", "spec_show" }));
        }

        // ------------------------------------------------------------------
        // TZ-01 / TZ-03 / TZ-04
        // ------------------------------------------------------------------

        [Test]
        public void Import_Extract_Show_FullCycle()
        {
            string import = Invoke("spec_import", "{\"text\":\"# ТЗ\\n\\nЦель: собрать отчёт по складу.\",\"name\":\"Отчёт.md\"}");
            Assert.That(import, Does.Contain("версия 1"), import);

            string extract = Invoke("spec_extract",
                "{\"slug\":\"отчёт\",\"title\":\"Отчёт по складу\",\"model\":" + ModelJson() + "}");
            Assert.That(extract, Does.Contain("модель ТЗ сохранена"), extract);

            string show = Invoke("spec_show", "{\"slug\":\"отчёт\",\"section\":\"steps\"}");
            Assert.Multiple(() =>
            {
                Assert.That(show, Does.Contain("S1"));
                Assert.That(show, Does.Contain("Открыть файл склада"));
                Assert.That(show, Does.Contain("проверка модели"));
            });
        }

        [Test]
        public void Extract_WithoutGoal_IsNotSavedAndIssuesAreNamed()
        {
            string extract = Invoke("spec_extract",
                "{\"slug\":\"tz\",\"title\":\"Отчёт\",\"model\":{\"title\":\"Отчёт\",\"goal\":\"\",\"steps\":[{\"id\":\"S1\",\"text\":\"шаг\",\"expected\":\"есть\"}]}}");

            Assert.Multiple(() =>
            {
                Assert.That(extract, Does.Contain("модель не сохранена"));
                Assert.That(extract, Does.Contain("goal"));
                Assert.That(ChatEnvironment.SpecStore.TryLoad("tz", out _, out _), Is.False,
                    "невалидная модель не должна сохраняться");
            });
        }

        [Test]
        public void Extract_BrokenJson_IsExplained()
        {
            string extract = Invoke("spec_extract", "{\"slug\":\"tz\",\"json\":\"{ это не json\"}");

            Assert.That(extract, Does.Contain("JSON модели не разобран"));
        }

        [Test]
        public void Update_PatchesOnlyProvidedFields()
        {
            Invoke("spec_import", "{\"text\":\"исходный текст\",\"name\":\"tz.md\"}");
            Invoke("spec_extract", "{\"slug\":\"tz\",\"title\":\"Отчёт\",\"model\":" + ModelJson() + "}");

            string update = Invoke("spec_update", "{\"slug\":\"tz\",\"model\":{\"goal\":\"Новая цель\"}}");

            Assert.That(update, Does.Contain("правка применена"), update);
            Assert.That(ChatEnvironment.SpecStore.TryLoad("tz", out var specification, out _), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(specification.Goal, Is.EqualTo("Новая цель"));
                Assert.That(specification.Steps.Count, Is.EqualTo(1), "патч не должен терять шаги");
            });
        }

        // ------------------------------------------------------------------
        // TZ-05
        // ------------------------------------------------------------------

        [Test]
        public void Decompose_UncoveredStep_IsRejectedWithAddress()
        {
            Invoke("spec_import", "{\"text\":\"текст\",\"name\":\"tz.md\"}");
            Invoke("spec_extract", "{\"slug\":\"tz\",\"title\":\"Отчёт\",\"model\":" + ModelJson() + "}");

            string decompose = Invoke("spec_decompose",
                "{\"slug\":\"tz\",\"tasks\":[{\"id\":\"T1\",\"title\":\"только критерий\",\"specRefs\":[\"A1\"]," +
                "\"steps\":[{\"activityType\":\"Sequence\"}]}]}");

            Assert.Multiple(() =>
            {
                Assert.That(decompose, Does.Contain("декомпозиция не сохранена"));
                Assert.That(decompose, Does.Contain("steps[S1]"));
            });
        }

        [Test]
        public void Decompose_Covered_SavesTasks()
        {
            Invoke("spec_import", "{\"text\":\"текст\",\"name\":\"tz.md\"}");
            Invoke("spec_extract", "{\"slug\":\"tz\",\"title\":\"Отчёт\",\"model\":" + ModelJson() + "}");

            string decompose = Invoke("spec_decompose", "{\"slug\":\"tz\",\"tasks\":" + TasksJson() + "}");

            Assert.That(decompose, Does.Contain("декомпозиция сохранена"), decompose);
            Assert.That(ChatEnvironment.SpecStore.TryLoad("tz", out var specification, out _), Is.True);
            Assert.That(specification.Tasks.Count, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------
        // TZ-06
        // ------------------------------------------------------------------

        [Test]
        public void Link_ThenCoverage_ShowsCoveredAndUncoveredItems()
        {
            PrepareSpecified();

            string link = Invoke("spec_link",
                "{\"slug\":\"tz\",\"itemId\":\"S1\",\"elementId\":\"root/Activities[0]\",\"path\":\"Main.xaml\"}");
            Assert.That(link, Does.Contain("покрыто 1 из 2"), link);

            string coverage = Invoke("spec_coverage", "{\"slug\":\"tz\",\"path\":\"Main.xaml\"}");
            Assert.Multiple(() =>
            {
                Assert.That(coverage, Does.Contain("покрыто 1 из 2"));
                Assert.That(coverage, Does.Contain("A1"));
            });
        }

        [Test]
        public void Link_ToMissingElement_IsRejected()
        {
            PrepareSpecified();

            string link = Invoke("spec_link",
                "{\"slug\":\"tz\",\"itemId\":\"S1\",\"elementId\":\"root/Activities[9]\",\"path\":\"Main.xaml\"}");

            Assert.That(link, Does.Contain("нет в структуре процесса"), link);
        }

        [Test]
        public void Link_ToMissingItem_IsRejectedWithKnownIds()
        {
            PrepareSpecified();

            string link = Invoke("spec_link",
                "{\"slug\":\"tz\",\"itemId\":\"S99\",\"elementId\":\"root/Activities[0]\",\"path\":\"Main.xaml\"}");

            Assert.Multiple(() =>
            {
                Assert.That(link, Does.Contain("нет в ТЗ"));
                Assert.That(link, Does.Contain("S1"));
            });
        }

        [Test]
        public void Coverage_WithoutProcess_IsNotPerformedState()
        {
            PrepareSpecified();

            string coverage = Invoke("spec_coverage", "{\"slug\":\"tz\"}");

            Assert.That(coverage, Does.Contain("трассировка не выполнена"), coverage);
        }

        // ------------------------------------------------------------------
        // TZ-07
        // ------------------------------------------------------------------

        [Test]
        public void Generate_WithoutApply_OnlyBuildsPlan()
        {
            PrepareSpecifiedWithTasks();

            string plan = Invoke("spec_generate", "{\"slug\":\"tz\",\"path\":\"Main.xaml\"}");

            Assert.Multiple(() =>
            {
                Assert.That(plan, Does.Contain("план построен"));
                Assert.That(plan, Does.Contain("правки не применялись"));
                Assert.That(plan, Does.Contain("Sequence"));
                Assert.That(_editing.Inserts, Is.Empty, "без apply=true правки не применяются");
            });
        }

        [Test]
        public void Generate_Apply_CreatesElementsAndStoresAddresses()
        {
            PrepareSpecifiedWithTasks();

            string apply = Invoke("spec_generate", "{\"slug\":\"tz\",\"path\":\"Main.xaml\",\"apply\":true}");

            Assert.Multiple(() =>
            {
                Assert.That(apply, Does.Contain("создано элементов"), apply);
                Assert.That(_editing.Inserts, Is.EqualTo(new[] { "Sequence" }));
                Assert.That(_editing.Arguments, Is.Not.Empty, "аргументы шага должны задаваться");
            });

            Assert.That(ChatEnvironment.SpecStore.TryLoad("tz", out var specification, out _), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(specification.Tasks[0].ElementIds, Is.Not.Empty);
                Assert.That(specification.Tasks[0].Status, Is.EqualTo(SpecTaskStatus.Done));
                Assert.That(specification.Links, Is.Empty,
                    "у задачи два пункта ТЗ: метка не угадывается, её ставит spec_link");
            });

            // Повторный прогон не дублирует: задача уже сгенерирована.
            _outline.Current = CreateOutline();
            string second = Invoke("spec_generate", "{\"slug\":\"tz\",\"path\":\"Main.xaml\",\"apply\":true}");
            Assert.That(second, Does.Contain("пропущено"), second);
            Assert.That(_editing.Inserts.Count, Is.EqualTo(1), "второй прогон не должен вставлять повторно");
        }

        [Test]
        public void Generate_Apply_WithoutDesigner_IsHonestRefusal()
        {
            PrepareSpecifiedWithTasks();
            _editing.Open = false;

            string apply = Invoke("spec_generate", "{\"slug\":\"tz\",\"path\":\"Main.xaml\",\"apply\":true}");

            Assert.Multiple(() =>
            {
                Assert.That(apply, Does.Contain("генерация не выполнена"));
                Assert.That(apply, Does.Contain("не открыт в конструкторе"));
            });
        }

        // ------------------------------------------------------------------
        // TZ-09
        // ------------------------------------------------------------------

        [Test]
        public void Ask_ThenAnswer_ClosesQuestionAndCanCreateVersion()
        {
            PrepareSpecified();

            string ask = Invoke("spec_ask",
                "{\"slug\":\"tz\",\"question\":\"Какой файл открывать?\",\"itemId\":\"S1\",\"options\":[\"A\",\"B\"]}");
            Assert.Multiple(() =>
            {
                Assert.That(ask, Does.Contain("записан в журнал"));
                Assert.That(ask, Does.Contain("открыт"));
            });

            string answer = Invoke("spec_answer",
                "{\"slug\":\"tz\",\"id\":\"Q1\",\"answer\":\"Файл склада\",\"text\":\"исправленный текст ТЗ\"}");

            Assert.Multiple(() =>
            {
                Assert.That(answer, Does.Contain("вопрос Q1 закрыт"));
                Assert.That(answer, Does.Contain("решён"));
                Assert.That(answer, Does.Contain("новая версия ТЗ: 2"));
            });
        }

        [Test]
        public void Answer_UnknownQuestion_IsExplained()
        {
            PrepareSpecified();

            string answer = Invoke("spec_answer", "{\"slug\":\"tz\",\"id\":\"Q9\",\"answer\":\"да\"}");

            Assert.That(answer, Does.Contain("нет в журнале"), answer);
        }

        // ------------------------------------------------------------------
        // TZ-08
        // ------------------------------------------------------------------

        [Test]
        public void TestCases_ThenRun_ProducesChecklist()
        {
            PrepareSpecified();
            Invoke("spec_testcases", "{\"slug\":\"tz\",\"testCases\":[" +
                                     "{\"id\":\"C1\",\"criterionId\":\"A1\",\"inputs\":{\"InputText\":\"max\"},\"expectedOutputs\":{\"Result\":\"MAX\"}}," +
                                     "{\"id\":\"C2\",\"criterionId\":\"A1\",\"inputs\":{\"InputText\":\"max\"},\"expectedOutputs\":{\"Result\":\"нет\"}}]}");

            string run = Invoke("spec_run_tests", "{\"slug\":\"tz\",\"path\":\"Main.xaml\"}");

            Assert.Multiple(() =>
            {
                Assert.That(run, Does.Contain("пройдено 1 из 2"), run);
                Assert.That(run, Does.Contain("провалено 1"));
                Assert.That(run, Does.Contain("Result"));
            });

            string runsDirectory = Path.Combine(_root, "docs", "spec", "tz", "runs");
            Assert.That(Directory.Exists(runsDirectory), Is.True, "отчёт о прогоне не записан");
            Assert.That(Directory.GetFiles(runsDirectory, "*.json"), Is.Not.Empty);
        }

        [Test]
        public void RunTests_WithoutEngine_ReportsNotRun()
        {
            PrepareSpecified();
            Invoke("spec_testcases", "{\"slug\":\"tz\",\"testCases\":[{\"id\":\"C1\",\"criterionId\":\"A1\"," +
                                     "\"inputs\":{},\"expectedOutputs\":{\"Result\":\"MAX\"}}]}");
            ChatEnvironment.WorkflowRun = NullWorkflowRunService.Instance;

            string run = Invoke("spec_run_tests", "{\"slug\":\"tz\",\"path\":\"Main.xaml\"}");

            Assert.Multiple(() =>
            {
                Assert.That(run, Does.Contain("не запускалось"));
                Assert.That(run, Does.Contain("прогон не выполнен"));
                Assert.That(run, Does.Not.Contain("пройдено 1"));
            });
        }

        // ------------------------------------------------------------------
        // Помощники
        // ------------------------------------------------------------------

        [Test]
        public void ReadFiles_ExtractsDocxText()
        {
            string path = Path.Combine(_root, "tz.docx");
            using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                       path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var main = document.AddMainDocumentPart();
                main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
                    new DocumentFormat.OpenXml.Wordprocessing.Body(
                        new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                            new DocumentFormat.OpenXml.Wordprocessing.Run(
                                new DocumentFormat.OpenXml.Wordprocessing.Text("Открыть файл склада")))));
                main.Document.Save();
            }

            string result = Invoke("read_files", "{\"paths\":[\"" + path.Replace("\\", "\\\\") + "\"]}");

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("формат: docx"), result);
                Assert.That(result, Does.Contain("Открыть файл склада"), result);
            });
        }

        private string Invoke(string tool, string json)
        {
            var registry = CreateRegistry();
            var instance = registry.Find(tool);
            Assert.That(instance, Is.Not.Null, "инструмент не зарегистрирован: " + tool);
            return instance.ExecuteAsync(JObject.Parse(json), CancellationToken.None).GetAwaiter().GetResult();
        }

        private AgentToolRegistry CreateRegistry()
        {
            var factory = new AgentToolFactory(
                new FakeAiProvider(),
                () => _root,
                NullStudioDocumentService.Instance,
                new FakeBuildFeedbackService(),
                (question, options, cancellationToken) => Task.FromResult("answer"),
                (systemPrompt, task, cancellationToken) => Task.FromResult("sub-result"),
                enableSubagents: false,
                subagentMaxDepth: 2);

            return factory.CreateRegistry();
        }

        private void PrepareSpecified()
        {
            Invoke("spec_import", "{\"text\":\"# ТЗ\\n\\nЦель: собрать отчёт.\",\"name\":\"tz.md\"}");
            Invoke("spec_extract", "{\"slug\":\"tz\",\"title\":\"Отчёт\",\"model\":" + ModelJson() + "}");
        }

        private void PrepareSpecifiedWithTasks()
        {
            PrepareSpecified();
            Invoke("spec_decompose", "{\"slug\":\"tz\",\"tasks\":" + TasksJson() + "}");
        }

        private static string ModelJson()
        {
            return "{\"title\":\"Отчёт\",\"goal\":\"Собрать отчёт по складу\"," +
                   "\"steps\":[{\"id\":\"S1\",\"text\":\"Открыть файл склада\",\"expected\":\"Файл открыт\",\"dataRefs\":[]}]," +
                   "\"data\":[]," +
                   "\"acceptanceCriteria\":[{\"id\":\"A1\",\"text\":\"Отчёт создан\",\"check\":\"файл существует\"}]}";
        }

        private static string TasksJson()
        {
            return "[{\"id\":\"T1\",\"title\":\"Открыть файл\",\"specRefs\":[\"S1\",\"A1\"]," +
                   "\"steps\":[{\"activityType\":\"Sequence\",\"parent\":\"root\",\"container\":\"Activities\"," +
                   "\"argumentName\":\"DisplayName\",\"argumentValue\":\"Открыть\"}]}]";
        }

        private static WorkflowOutline CreateOutline()
        {
            var outline = new WorkflowOutline(Path.Combine(Path.GetTempPath(), "Main.xaml"));
            var root = new WorkflowOutlineNode("root", string.Empty, 0, string.Empty,
                "System.Activities.Statements.Sequence", "Main");
            root.AddChild(new WorkflowOutlineNode("root/Activities[0]", "root", 0, "Activities",
                "System.Activities.Statements.WriteLine", "Log"));
            outline.Root = root;
            return outline;
        }

        // ------------------------------------------------------------------
        // Подмены
        // ------------------------------------------------------------------

        private sealed class FakeOutlineService : IWorkflowOutlineService
        {
            public FakeOutlineService(WorkflowOutline outline)
            {
                Current = outline;
            }

            public WorkflowOutline Current { get; set; }

            public WorkflowOutline GetOutline(string path, int depth = 0, bool includeArguments = true)
            {
                return string.IsNullOrWhiteSpace(path) ? null : Current;
            }
        }

        private sealed class FakeRunService : IWorkflowRunService
        {
            public bool Available => true;

            public string UnavailableReason => string.Empty;

            public SpecRunOutcome Run(SpecRunRequest request)
            {
                string input = request?.Inputs != null && request.Inputs.TryGetValue("InputText", out string value)
                    ? value
                    : string.Empty;

                var outcome = new SpecRunOutcome
                {
                    Ok = true,
                    State = "Completed",
                    ExitCode = 0
                };
                outcome.Outputs["Result"] = input.ToUpperInvariant();
                return outcome;
            }
        }

        private sealed class FakeEditingService : IWorkflowEditingService
        {
            public List<string> Inserts { get; } = new List<string>();

            public List<string> Arguments { get; } = new List<string>();

            public bool Open { get; set; } = true;

            public IWorkflowEditSession BeginEdit(string path, string label = null)
            {
                return Open
                    ? (IWorkflowEditSession)new Session(this)
                    : new UnavailableWorkflowEditSession(path, label, "файл не открыт в конструкторе");
            }

            public WorkflowEditResult Undo(string path) => new WorkflowEditResult("undo", true);

            public WorkflowEditResult Redo(string path) => new WorkflowEditResult("redo", true);

            public WorkflowOutline GetLiveOutline(string path, int depth = 0, bool includeArguments = true)
                => Open ? CreateOutline() : null;

            public bool IsOpenInDesigner(string path) => Open;

            private sealed class Session : IWorkflowEditSession
            {
                private readonly FakeEditingService _owner;

                public Session(FakeEditingService owner)
                {
                    _owner = owner;
                }

                public string FilePath => Path.Combine(Path.GetTempPath(), "Main.xaml");

                public string Label => "test";

                public bool IsActive => true;

                public WorkflowEditResult Insert(WorkflowInsertRequest request)
                {
                    _owner.Inserts.Add(request.ActivityType);
                    string address = "root/Activities[" + (_owner.Inserts.Count - 1) + "]";
                    return new WorkflowEditResult("insert", true).WithElements(null, address);
                }

                public WorkflowEditResult Delete(string elementId) => new WorkflowEditResult("delete", true);

                public WorkflowEditResult Move(WorkflowMoveRequest request) => new WorkflowEditResult("move", true);

                public WorkflowEditResult Wrap(WorkflowWrapRequest request) => new WorkflowEditResult("wrap", true);

                public WorkflowEditResult SetArgument(WorkflowArgumentSetRequest request)
                {
                    _owner.Arguments.Add(request.ElementId + "." + request.Argument);
                    return new WorkflowEditResult("argument_set", true).WithElements(request.ElementId);
                }

                public WorkflowEditResult AddVariable(WorkflowVariableAddRequest request)
                    => new WorkflowEditResult("variable_add", true);

                public WorkflowEditResult RemoveVariable(WorkflowVariableRemoveRequest request)
                    => new WorkflowEditResult("variable_remove", true);

                public WorkflowEditResult Complete() => new WorkflowEditResult("complete", true);

                public WorkflowEditResult Revert() => new WorkflowEditResult("revert", true);

                public WorkflowOutline Outline(int depth = 0, bool includeArguments = true) => CreateOutline();

                public void Dispose()
                {
                }
            }
        }

        private sealed class FakeBuildFeedbackService : IBuildFeedbackService
        {
            public Task BuildAsync(string configuration, CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task<bool> HasSolutionConfigurationAsync(string configuration, CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public Task<IReadOnlyList<BuildError>> GetCompilationErrorsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<BuildError>>(new List<BuildError>());

            public Task<string> GetOutputWindowTextAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(string.Empty);
        }
    }
}
