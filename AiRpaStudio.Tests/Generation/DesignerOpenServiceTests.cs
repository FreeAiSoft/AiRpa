using System.Text;
using AiRpaStudio.Tests.Design;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 3 конвейера генерации XAML: открытие процесса в конструкторе
/// (<c>DesignerOpenService</c>).
/// <para>
/// Проверяется контракт п. 1.3 плана: файл открывается и его вкладка активна, повторный вызов
/// активирует ту же вкладку (второй конструктор на тот же файл не создаётся), работа уходит на
/// UI-поток, а перезапись файла, открытого в конструкторе, даёт понятную ошибку — не падение
/// и не «тихую» потерю правок.
/// </para>
/// <para>
/// Среда конструктора подменяется фальшивым <see cref="IWorkflowDesignerHost"/>: предмет теста —
/// порядок действий сервиса и его ответы, а не загрузка документа WF (это проверяет
/// <c>DesignerOpenRealDesignerTests</c> на настоящем дизайнере).
/// </para>
/// </summary>
[TestFixture]
public class DesignerOpenServiceTests
{
    private readonly List<string> _temporaryDirectories = new List<string>();

    [TearDown]
    public void CleanupTemporaryDirectories()
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

    // --- Открытие и активация вкладки. ---

    [Test]
    public void OpenOrActivate_OpensFileAndActivatesTab()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();

        DesignerOpenResult result = CreateService(host).OpenOrActivate(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(host.OpenCount, Is.EqualTo(1), "файл, которого нет среди вкладок, открывается один раз");
            Assert.That(result.FilePath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(result.AlreadyOpen, Is.False);
            Assert.That(result.Activated, Is.True, "после открытия вкладка обязана стать активной");
            Assert.That(host.ActiveTab, Is.Not.Null, "активной должна быть вкладка открытого файла");
            Assert.That(host.ActiveTab.FilePath, Is.EqualTo(Path.GetFullPath(path)));
        });
    }

    [Test]
    public void OpenOrActivate_ReusesAlreadyOpenTabInsteadOfSecondDesigner()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        DesignerOpenService service = CreateService(host);

        service.OpenOrActivate(path);
        DesignerOpenResult second = service.OpenOrActivate(path);

        Assert.Multiple(() =>
        {
            Assert.That(second.Ok, Is.True, second.Describe());
            Assert.That(second.AlreadyOpen, Is.True, "повторный вызов обязан сообщать, что вкладка уже была");
            Assert.That(host.OpenCount, Is.EqualTo(1), "два конструктора на один файл открывать нельзя");
            Assert.That(host.ActivateCount, Is.EqualTo(2), "повторный вызов активирует существующую вкладку");
            Assert.That(second.Activated, Is.True);
            Assert.That(host.GetOpenTabs(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void OpenOrActivate_RecognisesSameFileWrittenWithDifferentCase()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        DesignerOpenService service = CreateService(host);

        service.OpenOrActivate(path);
        DesignerOpenResult second = service.OpenOrActivate(path.ToUpperInvariant());

        Assert.Multiple(() =>
        {
            Assert.That(second.Ok, Is.True, second.Describe());
            Assert.That(second.AlreadyOpen, Is.True, "путь сравнивается без учёта регистра: это тот же файл");
            Assert.That(host.OpenCount, Is.EqualTo(1), "второй конструктор на тот же файл не создаётся");
        });
    }

    /// <summary>
    /// Содержимое только что открытой вкладки совпадает с файлом на диске: «изменён» выставляться не
    /// должен, иначе кнопка «Сохранить» активна сразу после открытия (фантомное dirty-состояние).
    /// </summary>
    [Test]
    public void OpenOrActivate_LeavesFreshTabClean()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();

        CreateService(host).OpenOrActivate(path);

        DesignerTab tab = host.GetOpenTabs().Single();

        Assert.Multiple(() =>
        {
            Assert.That(tab.IsEdited, Is.False, "после открытия вкладка не считается изменённой");
            Assert.That(host.MarkCleanCount, Is.EqualTo(1), "сервис обязан сбросить «изменён» у свежей вкладки");
        });
    }

    [Test]
    public void OpenOrActivate_KeepsUnsavedChangesOfAlreadyOpenTab()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        var tab = new DesignerTab(path, isEdited: true);
        host.AddOpenTab(tab);

        DesignerOpenResult result = CreateService(host).OpenOrActivate(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(result.AlreadyOpen, Is.True);
            Assert.That(host.OpenCount, Is.Zero);

            // Правки пользователя нельзя терять: активация существующей вкладки их не сбрасывает.
            Assert.That(tab.IsEdited, Is.True, "активация уже открытой вкладки не сбрасывает признак изменений");
            Assert.That(host.MarkCleanCount, Is.Zero, "«изменён» сбрасывается только у свежезагруженной вкладки");
        });
    }

    [Test]
    public void OpenOrActivate_ReturnsWorkflowIdFromXaml()
    {
        string path = WriteWorkflow();

        DesignerOpenResult result = CreateService(new FakeDesignerHost()).OpenOrActivate(path);

        Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Main"), "workflowId — это x:Class открытого процесса");
    }

    [Test]
    public void OpenOrActivate_RejectsMissingFile()
    {
        string missing = Path.Combine(NewDirectory(), "Main.xaml");
        var host = new FakeDesignerHost();

        DesignerOpenResult result = CreateService(host).OpenOrActivate(missing);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("не найден"));
            Assert.That(host.OpenCount, Is.Zero, "конструктор для отсутствующего файла не создаётся");
        });
    }

    [Test]
    public void OpenOrActivate_RejectsNonXamlFile()
    {
        string directory = NewDirectory();
        string path = Path.Combine(directory, "Main.txt");
        File.WriteAllText(path, "не процесс");
        var host = new FakeDesignerHost();

        DesignerOpenResult result = CreateService(host).OpenOrActivate(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain(".xaml"));
            Assert.That(host.OpenCount, Is.Zero);
        });
    }

    /// <summary>
    /// Сбой загрузчика (файл занят, битый XAML) — это ответ инструмента, а не исключение наружу:
    /// MCP-вызов не должен ронять студию.
    /// </summary>
    [Test]
    public void OpenOrActivate_ReturnsErrorInsteadOfThrowingWhenDesignerFails()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost { OpenError = new IOException("файл занят другим процессом") };

        DesignerOpenResult result = null;
        Assert.DoesNotThrow(() => result = CreateService(host).OpenOrActivate(path));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("файл занят другим процессом"));
            Assert.That(result.Errors.Single(), Does.Contain("не удалось открыть"), "в ошибке есть контекст");
            Assert.That(result.Hints, Is.Not.Empty, "к ошибке прикладывается подсказка, что делать");
        });
    }

    /// <summary>MCP-вызов приходит из фонового потока: конструктор создаётся только на UI-потоке.</summary>
    [Test]
    public void OpenOrActivate_RunsOnUiThread()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        DesignerTestHost bench = DesignerTestHost.Shared;
        var service = new DesignerOpenService(host, new DispatcherDesignerUiThread(bench.Dispatcher));

        DesignerOpenResult result = service.OpenOrActivate(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(Thread.CurrentThread.ManagedThreadId, Is.Not.EqualTo(bench.ThreadId),
                "тест должен вызывать сервис из чужого потока — иначе маршалинг не проверяется");
            Assert.That(host.LastOpenThreadId, Is.EqualTo(bench.ThreadId),
                "конструктор обязан создаваться на UI-потоке");
        });
    }

    // --- Перезапись файла, открытого в конструкторе. ---

    [Test]
    public void CheckWrite_AllowsFileThatIsNotOpen()
    {
        string path = WriteWorkflow();

        DesignerWriteCheck check = CreateService(new FakeDesignerHost()).CheckWrite(path);

        Assert.Multiple(() =>
        {
            Assert.That(check.CanWrite, Is.True, check.Error);
            Assert.That(check.IsOpenInDesigner, Is.False);
        });
    }

    [Test]
    public void CheckWrite_AllowsNewFile()
    {
        string path = Path.Combine(NewDirectory(), "NewProcess.xaml");

        DesignerWriteCheck check = CreateService(new FakeDesignerHost()).CheckWrite(path);

        Assert.That(check.CanWrite, Is.True, "новый файл не занят никем и может быть создан");
    }

    [Test]
    public void CheckWrite_BlocksFileOpenInDesigner()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        host.AddOpenTab(new DesignerTab(path, isEdited: false));

        DesignerWriteCheck check = CreateService(host).CheckWrite(path);

        Assert.Multiple(() =>
        {
            Assert.That(check.CanWrite, Is.False, "студия держит открытый файл с FileShare.Read");
            Assert.That(check.IsOpenInDesigner, Is.True);
            Assert.That(check.Error, Does.Contain("открыт в конструкторе"));
            Assert.That(string.Join(" ", check.Hints), Does.Contain("Reload"),
                "подсказка обязана называть сценарий: закрыть вкладку, перезагрузить её или писать в новый файл");
        });
    }

    [Test]
    public void Close_RefusesTabWithUnsavedChanges()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        host.AddOpenTab(new DesignerTab(path, isEdited: true));

        DesignerOpenResult result = CreateService(host).Close(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "закрывать вкладку с правками без сохранения нельзя");
            Assert.That(result.Errors.Single(), Does.Contain("несохранённые правки"));
            Assert.That(host.CloseCount, Is.Zero);
        });
    }

    [Test]
    public void Close_FreesOpenFileForOverwrite()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        host.AddOpenTab(new DesignerTab(path, isEdited: false));
        DesignerOpenService service = CreateService(host);

        DesignerOpenResult result = service.Close(path);
        DesignerWriteCheck check = service.CheckWrite(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(host.CloseCount, Is.EqualTo(1));
            Assert.That(check.CanWrite, Is.True, "после закрытия вкладки файл снова доступен для перезаписи");
        });
    }

    [Test]
    public void Close_IsOkWhenFileIsNotOpen()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();

        DesignerOpenResult result = CreateService(host).Close(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, "закрывать нечего — это не ошибка");
            Assert.That(host.CloseCount, Is.Zero);
            Assert.That(result.Hints, Is.Not.Empty, "сервис сообщает, что файл и не был открыт");
        });
    }

    [Test]
    public void Reload_ClosesAndReopensTab()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        host.AddOpenTab(new DesignerTab(path, isEdited: false));
        DesignerOpenService service = CreateService(host);

        DesignerOpenResult result = service.Reload(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(result.Reloaded, Is.True);
            Assert.That(result.AlreadyOpen, Is.False, "вкладка пересоздана — это не «уже открыт»");
            Assert.That(host.CloseCount, Is.EqualTo(1));
            Assert.That(host.OpenCount, Is.EqualTo(1));
            Assert.That(result.Activated, Is.True);
            Assert.That(host.GetOpenTabs(), Has.Count.EqualTo(1), "после перезагрузки остаётся ровно одна вкладка");
        });
    }

    [Test]
    public void Reload_RefusesTabWithUnsavedChanges()
    {
        string path = WriteWorkflow();
        var host = new FakeDesignerHost();
        host.AddOpenTab(new DesignerTab(path, isEdited: true));

        DesignerOpenResult result = CreateService(host).Reload(path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("несохранённые правки"));
            Assert.That(host.CloseCount, Is.Zero);
            Assert.That(host.OpenCount, Is.Zero);
        });
    }

    // --- Связка с конвейером генерации (этап 2). ---

    /// <summary>
    /// Генерация в файл, открытый в конструкторе, обязана вернуть понятную ошибку до записи:
    /// иначе <c>File.Replace</c> падает с <see cref="IOException"/> после успешной валидации.
    /// </summary>
    [Test]
    public void BuildService_RejectsGenerationIntoFileOpenInDesigner()
    {
        string path = Path.Combine(NewDirectory(), "Main.xaml");
        WorkflowBuildResult created = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), path);
        Assert.That(created.Ok, Is.True, created.Describe());
        string before = File.ReadAllText(path);

        var host = new FakeDesignerHost();
        host.AddOpenTab(new DesignerTab(path, isEdited: false));
        DesignerOpenService service = CreateService(host);
        var buildService = new WorkflowBuildService(WorkflowTestSamples.Options(), gate: null, writeCheck: service);

        WorkflowBuildResult result = buildService.Save(WorkflowTestSamples.NestedWorkflow(), path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" ", result.Errors), Does.Contain("открыт в конструкторе"));
            Assert.That(File.ReadAllText(path), Is.EqualTo(before), "открытый файл не перезаписывается");
        });
    }

    [Test]
    public void BuildService_SavesAfterTabClosed()
    {
        string path = Path.Combine(NewDirectory(), "Main.xaml");
        WorkflowBuildResult created = WorkflowTestSamples.BuildService().Save(WorkflowTestSamples.SimpleWorkflow(), path);
        Assert.That(created.Ok, Is.True, created.Describe());

        var host = new FakeDesignerHost();
        host.AddOpenTab(new DesignerTab(path, isEdited: false));
        DesignerOpenService service = CreateService(host);
        var buildService = new WorkflowBuildService(WorkflowTestSamples.Options(), gate: null, writeCheck: service);

        Assert.That(service.Close(path).Ok, Is.True, "сценарий: закрыть вкладку");
        WorkflowBuildResult result = buildService.Save(WorkflowTestSamples.NestedWorkflow(), path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.True, result.Describe());
            Assert.That(File.ReadAllText(path), Does.Contain("Если"), "файл перезаписан новой генерацией");
        });
    }

    // --- Вспомогательные методы и поддельная среда конструктора. ---

    private static DesignerOpenService CreateService(FakeDesignerHost host)
    {
        return new DesignerOpenService(host, new ImmediateUiThread());
    }

    private string NewDirectory()
    {
        string directory = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(directory);

        return directory;
    }

    private string WriteWorkflow(string xaml = null)
    {
        string path = Path.Combine(NewDirectory(), "Main.xaml");
        File.WriteAllText(path, xaml ?? WorkflowTestSamples.CanonicalXaml(), new UTF8Encoding(false));

        return path;
    }

    /// <summary>UI-поток теста: вызов выполняется сразу, «мы на UI-потоке» — да (маршалинг не предмет проверки).</summary>
    private sealed class ImmediateUiThread : IDesignerUiThread
    {
        public bool IsUiThread => true;

        public T Invoke<T>(Func<T> func)
        {
            return func();
        }
    }

    /// <summary>Среда конструктора без WPF: запоминает вызовы и умеет «падать» по команде.</summary>
    private sealed class FakeDesignerHost : IWorkflowDesignerHost
    {
        private readonly List<DesignerTab> _tabs = new List<DesignerTab>();

        public DesignerTab ActiveTab { get; private set; }

        internal Exception OpenError { get; set; }

        internal int OpenCount { get; private set; }

        internal int ActivateCount { get; private set; }

        internal int CloseCount { get; private set; }

        internal int MarkCleanCount { get; private set; }

        internal int LastOpenThreadId { get; private set; }

        internal void AddOpenTab(DesignerTab tab)
        {
            _tabs.Add(tab);
        }

        public IReadOnlyList<DesignerTab> GetOpenTabs()
        {
            return _tabs.ToArray();
        }

        public DesignerTab Open(string filePath)
        {
            OpenCount++;
            LastOpenThreadId = Thread.CurrentThread.ManagedThreadId;

            if (OpenError != null)
            {
                throw OpenError;
            }

            // Свежая вкладка «изменена»: так ведёт себя дизайнер, который поднял ModelChanged при загрузке.
            var tab = new DesignerTab(filePath, isEdited: true);
            _tabs.Add(tab);

            return tab;
        }

        public void Activate(DesignerTab tab)
        {
            ActivateCount++;
            ActiveTab = tab;
        }

        public void Close(DesignerTab tab)
        {
            CloseCount++;
            _tabs.Remove(tab);

            if (ReferenceEquals(ActiveTab, tab))
            {
                ActiveTab = null;
            }
        }

        public void MarkClean(DesignerTab tab)
        {
            MarkCleanCount++;
            tab.IsEdited = false;
        }
    }
}
