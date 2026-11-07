using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CSharp.Activities;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 4 конвейера генерации XAML: палитра активностей
/// (<c>PaletteWorkflowService</c>) — единственный конвейер инструментов <c>xaml.*</c>.
/// <para>
/// Проверяется контракт п. 1.3 и 4 плана: создание процесса пишется и открывается в конструкторе,
/// вставка активности идёт через живую модель вкладки, каждый вызов завершается гейтом,
/// а при провале гейта правка откатывается и файл процесса остаётся валидным.
/// </para>
/// <para>
/// Среда конструктора и модель подменяются: предмет теста — порядок действий и ответы сервиса,
/// живой дизайнер проверяет <c>PaletteDesignerTests</c> на стенде конструктора.
/// </para>
/// </summary>
[TestFixture]
public class PaletteWorkflowServiceTests
{
    private readonly List<string> _temporaryDirectories = new List<string>();

    [TearDown]
    public void Cleanup()
    {
        foreach (string directory in _temporaryDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (Exception)
            {
                // Каталог мог остаться заблокированным — на результат теста это не влияет.
            }
        }

        _temporaryDirectories.Clear();
    }

    // --- Создание процесса. ---

    [Test]
    public void CreateWorkflow_SavesValidWorkflowAndOpensTab()
    {
        string path = NewPath();
        var host = new FakeDesignerHost();

        var request = new PaletteCreateWorkflowRequest { FilePath = path, ProjectName = WorkflowTestSamples.ProjectName };
        request.Activities.Add(new PaletteActivitySpec("WriteLine")
            .WithArgument("Text", PaletteValue.FromExpression("\"привет\"")));

        PaletteResult result = CreateService(host).CreateWorkflow(request);

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.True, "файл процесса обязан появиться на диске");
            Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Main"), "x:Class выводится из имени папки и файла");
            Assert.That(result.Xaml, Does.Contain("ExpressionActivityEditor=\"C#\""));
            Assert.That(result.Xaml, Does.Contain("CSharpValue"));
            Assert.That(host.OpenCount, Is.EqualTo(1), "после создания вкладка конструктора обязана открыться");
            Assert.That(host.ActiveTab, Is.Not.Null);
        });
    }

    [Test]
    public void CreateWorkflow_UnknownActivity_DoesNotWriteFile()
    {
        string path = NewPath();

        var request = new PaletteCreateWorkflowRequest { FilePath = path, ProjectName = WorkflowTestSamples.ProjectName };
        request.Activities.Add(new PaletteActivitySpec("НетТакойАктивности"));

        PaletteResult result = CreateService(new FakeDesignerHost()).CreateWorkflow(request);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("не найдена в палитре"));
            Assert.That(File.Exists(path), Is.False, "при ошибке сборки файл процесса не создаётся");
        });
    }

    [Test]
    public void CreateWorkflow_WithoutSteps_CreatesEmptySequenceWithHint()
    {
        string path = NewPath();

        PaletteResult result = CreateService(new FakeDesignerHost()).CreateWorkflow(
            new PaletteCreateWorkflowRequest { FilePath = path, ProjectName = WorkflowTestSamples.ProjectName });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(result.Hints, Has.Some.Contains("xaml.add_activity"),
                "агент должен узнать, чем наполнять пустой процесс");
        });
    }

    // --- Вставка активности. ---

    [Test]
    public void AddActivity_InsertsThroughModelAndReportsNewActivityId()
    {
        string path = WriteSample(3);
        var host = new FakeDesignerHost();
        var session = new FakeModelSession
        {
            XamlToWriteOnSave = XamlWithWriteLines(4),
            ActivityIdToReturn = null
        };

        var request = new PaletteAddActivityRequest
        {
            FilePath = path,
            Activity = new PaletteActivitySpec("WriteLine").WithArgument("Text", PaletteValue.FromExpression("\"новый\"")),
            Index = 3
        };

        PaletteResult result = CreateService(host, session).AddActivity(request);

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.Multiple(() =>
        {
            Assert.That(session.InsertCount, Is.EqualTo(1), "вставка идёт через модель вкладки, а не текстом XAML");
            Assert.That(session.LastActivity?.ActivityType, Is.EqualTo("WriteLine"));
            Assert.That(session.LastIndex, Is.EqualTo(3), "позиция вставки передаётся в модель");
            Assert.That(session.SaveCount, Is.EqualTo(1));
            Assert.That(result.ActivityId, Is.EqualTo("root/Activities[3]"),
                "новый идентификатор вычисляется по снимку процесса после правки");
            Assert.That(host.OpenCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void AddActivity_FailedGate_RollsBackAndKeepsWorkflowFileValid()
    {
        string path = WriteSample(3);
        string original = File.ReadAllText(path);
        var session = new FakeModelSession
        {
            // Assign без обязательных аргументов: WF считает такой процесс невалидным.
            XamlToWriteOnSave = InvalidXaml(),
            XamlToWriteOnRollback = original
        };

        PaletteResult result = CreateService(new FakeDesignerHost(), session).AddActivity(new PaletteAddActivityRequest
        {
            FilePath = path,
            Activity = new PaletteActivitySpec("Assign")
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, result.Describe());
            Assert.That(session.RollbackCount, Is.EqualTo(1), "провал гейта обязан откатывать правку модели");
            Assert.That(File.ReadAllText(path), Is.EqualTo(original), "файл процесса остаётся валидным");
            Assert.That(result.Hints, Has.Some.Contains("правка отменена"));
        });
    }

    [Test]
    public void AddActivity_UnknownParent_ReturnsErrorBeforeEditing()
    {
        string path = WriteSample(1);
        var session = new FakeModelSession();

        PaletteResult result = CreateService(new FakeDesignerHost(), session).AddActivity(new PaletteAddActivityRequest
        {
            FilePath = path,
            Activity = new PaletteActivitySpec("WriteLine"),
            ParentActivityId = "root/Activities[99]",
            ContainerName = "Activities"
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("не найдена"));
            Assert.That(session.InsertCount, Is.Zero, "до проверки родителя модель не трогается");
        });
    }

    [Test]
    public void AddActivity_ContainerNotOwnedByParent_ReturnsError()
    {
        string path = WriteSample(1);

        PaletteResult result = CreateService(new FakeDesignerHost(), new FakeModelSession()).AddActivity(
            new PaletteAddActivityRequest
            {
                FilePath = path,
                Activity = new PaletteActivitySpec("WriteLine"),
                ContainerName = "Body"
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("нет контейнера «Body»"));
        });
    }

    [Test]
    public void AddActivity_NoLiveDesigner_ReturnsErrorWithHint()
    {
        string path = WriteSample(1);

        PaletteResult result = CreateService(new FakeDesignerHost(), sessionFactory: _ => null)
            .AddActivity(new PaletteAddActivityRequest
            {
                FilePath = path,
                Activity = new PaletteActivitySpec("WriteLine")
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("живого конструктора"));
            Assert.That(result.Hints, Is.Not.Empty);
        });
    }

    // --- Установка аргумента. ---

    [Test]
    public void SetArgument_ValidActivityAndArgument_PassesValueToModel()
    {
        string path = WriteSample(2);
        var session = new FakeModelSession { XamlToWriteOnSave = WriteSampleXaml(2) };

        PaletteResult result = CreateService(new FakeDesignerHost(), session).SetArgument(new PaletteSetArgumentRequest
        {
            FilePath = path,
            ActivityId = "root/Activities[0]",
            ArgumentName = "Text",
            Value = PaletteValue.FromExpression("\"новый текст\"")
        });

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.Multiple(() =>
        {
            Assert.That(session.SetArgumentCount, Is.EqualTo(1));
            Assert.That(session.LastActivityId, Is.EqualTo("root/Activities[0]"));
            Assert.That(session.LastArgumentName, Is.EqualTo("Text"));
            Assert.That(session.LastValue?.Expression, Is.EqualTo("\"новый текст\""));
            Assert.That(session.SaveCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void SetArgument_UnknownArgument_ReturnsErrorWithoutOpeningTab()
    {
        string path = WriteSample(2);
        var host = new FakeDesignerHost();

        PaletteResult result = CreateService(host, new FakeModelSession()).SetArgument(new PaletteSetArgumentRequest
        {
            FilePath = path,
            ActivityId = "root/Activities[0]",
            ArgumentName = "НетТакого",
            Value = PaletteValue.FromExpression("\"x\"")
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("нет аргумента"));
            Assert.That(result.Errors.Single(), Does.Contain("Text"), "ошибка перечисляет реальные аргументы");
            Assert.That(host.OpenCount, Is.Zero);
        });
    }

    [Test]
    public void SetArgument_UnknownActivity_ReturnsError()
    {
        string path = WriteSample(2);

        PaletteResult result = CreateService(new FakeDesignerHost(), new FakeModelSession()).SetArgument(
            new PaletteSetArgumentRequest
            {
                FilePath = path,
                ActivityId = "root/Activities[42]",
                ArgumentName = "Text",
                Value = PaletteValue.FromExpression("\"x\"")
            });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("не найдена в процессе"));
        });
    }

    // --- Чтение процесса. ---

    [Test]
    public void GetWorkflow_ReturnsTreeAndOptionalXaml()
    {
        string path = WriteSample(2);

        PaletteResult result = CreateService(new FakeDesignerHost()).GetWorkflow(
            new PaletteGetWorkflowRequest { FilePath = path, IncludeXaml = true });

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.Multiple(() =>
        {
            Assert.That(result.Snapshot?.Root?.TypeName, Is.EqualTo("Sequence"));
            Assert.That(result.Snapshot.ActivityCount, Is.EqualTo(3));
            Assert.That(result.Xaml, Does.Contain("<Sequence"));
            Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Main"));
        });
    }

    [Test]
    public void GetWorkflow_MissingFile_ReturnsErrorWithHint()
    {
        PaletteResult result = CreateService(new FakeDesignerHost()).GetWorkflow(
            new PaletteGetWorkflowRequest { FilePath = NewPath() });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Hints, Is.Not.Empty);
        });
    }

    [Test]
    public void CallsWithoutDesignerEnvironment_ReportUnavailableInsteadOfThrowing()
    {
        string path = WriteSample(1);
        var service = new PaletteWorkflowService(
            registry: ActivityPaletteRegistry.Default,
            host: null,
            uiThread: null,
            sessionFactory: null,
            gate: null);

        PaletteResult result = service.AddActivity(new PaletteAddActivityRequest
        {
            FilePath = path,
            Activity = new PaletteActivitySpec("WriteLine")
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("среда конструктора недоступна"));
        });
    }

    // --- Инфраструктура тестов. ---

    private PaletteWorkflowService CreateService(
        FakeDesignerHost host,
        FakeModelSession session = null,
        Func<DesignerTab, IWorkflowModelSession> sessionFactory = null)
    {
        FakeModelSession target = session ?? new FakeModelSession();
        Func<DesignerTab, IWorkflowModelSession> factory = sessionFactory ?? (_ => target);

        return new PaletteWorkflowService(
            ActivityPaletteRegistry.Default,
            host,
            new ImmediateUiThread(),
            factory);
    }

    private string NewPath()
    {
        string directory = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(directory);

        return Path.Combine(directory, "Main.xaml");
    }

    private string WriteSample(int writeLineCount)
    {
        string path = NewPath();
        File.WriteAllText(path, XamlWithWriteLines(writeLineCount), new UTF8Encoding(false));

        return path;
    }

    private static string WriteSampleXaml(int writeLineCount)
    {
        return XamlWithWriteLines(writeLineCount);
    }

    /// <summary>Валидный процесс из последовательности <c>WriteLine</c> — «3–5 шагов RPA».</summary>
    private static string XamlWithWriteLines(int count)
    {
        var sequence = new Sequence();

        for (int index = 0; index < count; index++)
        {
            sequence.Activities.Add(new WriteLine
            {
                DisplayName = "Шаг " + (index + 1),
                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"шаг\"" })
            });
        }

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Build(sequence);
        if (!result.Ok)
        {
            throw new InvalidOperationException("Образец процесса не прошёл гейт: " + result.Describe());
        }

        return result.Xaml;
    }

    /// <summary>Невалидный процесс: <c>Assign</c> без обязательных аргументов (гейт обязан отказать).</summary>
    private static string InvalidXaml()
    {
        var sequence = new Sequence();
        sequence.Activities.Add(new Assign { DisplayName = "Пустой Assign" });

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Build(sequence);
        if (result.Ok || string.IsNullOrWhiteSpace(result.Xaml))
        {
            throw new InvalidOperationException("Ожидался отказ гейта для Assign без аргументов");
        }

        return result.Xaml;
    }

    /// <summary>Среда конструктора без WPF: вкладки, открытие и активация.</summary>
    private sealed class FakeDesignerHost : IWorkflowDesignerHost
    {
        private readonly List<DesignerTab> _tabs = new List<DesignerTab>();

        internal int OpenCount { get; private set; }

        public DesignerTab ActiveTab { get; private set; }

        public IReadOnlyList<DesignerTab> GetOpenTabs()
        {
            return _tabs.ToArray();
        }

        public DesignerTab Open(string filePath)
        {
            OpenCount++;

            var tab = new DesignerTab(filePath, isEdited: false);
            _tabs.Add(tab);

            return tab;
        }

        public void Activate(DesignerTab tab)
        {
            ActiveTab = tab;
        }

        public void Close(DesignerTab tab)
        {
            _tabs.Remove(tab);

            if (ReferenceEquals(ActiveTab, tab))
            {
                ActiveTab = null;
            }
        }

        public void MarkClean(DesignerTab tab)
        {
            tab.IsEdited = false;
        }
    }

    /// <summary>UI-поток стенда: вызов выполняется сразу в текущем потоке.</summary>
    private sealed class ImmediateUiThread : IDesignerUiThread
    {
        public bool IsUiThread => true;

        public T Invoke<T>(Func<T> func)
        {
            return func();
        }
    }

    /// <summary>Поддельная модель вкладки: запоминает правки и пишет заданный XAML при сохранении.</summary>
    private sealed class FakeModelSession : IWorkflowModelSession
    {
        internal string XamlToWriteOnSave { get; set; }

        internal string XamlToWriteOnRollback { get; set; }

        internal string ActivityIdToReturn { get; set; } = "root/Activities[0]";

        internal int InsertCount { get; private set; }

        internal int SetArgumentCount { get; private set; }

        internal int SaveCount { get; private set; }

        internal int RollbackCount { get; private set; }

        internal PaletteActivitySpec LastActivity { get; private set; }

        internal string LastActivityId { get; private set; }

        internal string LastArgumentName { get; private set; }

        internal PaletteValue LastValue { get; private set; }

        internal string LastParentId { get; private set; }

        internal string LastContainer { get; private set; }

        internal int? LastIndex { get; private set; }

        public WorkflowEditResult InsertActivity(
            PaletteActivitySpec spec,
            string parentActivityId,
            string containerName,
            int? index)
        {
            InsertCount++;
            LastActivity = spec;
            LastParentId = parentActivityId;
            LastContainer = containerName;
            LastIndex = index;

            return WorkflowEditResult.Success(ActivityIdToReturn);
        }

        public WorkflowEditResult SetArgument(string activityId, string argumentName, PaletteValue value)
        {
            SetArgumentCount++;
            LastActivityId = activityId;
            LastArgumentName = argumentName;
            LastValue = value;

            return WorkflowEditResult.Success(activityId);
        }

        public WorkflowEditResult Save(string filePath)
        {
            SaveCount++;

            if (!string.IsNullOrEmpty(XamlToWriteOnSave))
            {
                File.WriteAllText(filePath, XamlToWriteOnSave, new UTF8Encoding(false));
            }

            return WorkflowEditResult.Success();
        }

        public WorkflowEditResult Rollback(string filePath)
        {
            RollbackCount++;

            if (!string.IsNullOrEmpty(XamlToWriteOnRollback))
            {
                File.WriteAllText(filePath, XamlToWriteOnRollback, new UTF8Encoding(false));
            }

            return WorkflowEditResult.Success();
        }
    }
}
