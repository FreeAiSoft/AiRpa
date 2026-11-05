using System;
using System.IO;
using System.Text;
using AiRpaStudio.Tests.Design;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-04 на живом конструкторе: обёртка активности (<c>activity_wrap</c>) через модель конструктора.
/// <para>
/// Проверяется только на настоящем <c>WorkflowDesigner</c>: перестановка узлов идёт через
/// <c>ModelItem</c>, и «обёртка внутри обёртки» ломается именно там, а не на фейковой модели.
/// Сценарий целиком выполняется в домене стенда (<see cref="DesignerBenchProxy"/>): конструктор
/// нельзя выносить за пределы его STA-потока.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowWrapBridgeTests
{
    private static DesignerBenchWrapReport _report;
    private static string _workDirectory;

    [OneTimeSetUp]
    public void RunWrapScenarioOnLiveDesigner()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "AiRpaStudio-Wrap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);

        string openPath = WriteWorkflow("Main.xaml");
        _report = DesignerBenchProxy.Shared.WorkflowWrapScenario(openPath);
    }

    [OneTimeTearDown]
    public void RemoveWorkDirectory()
    {
        try
        {
            if (_workDirectory != null && Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, true);
            }
        }
        catch (Exception)
        {
            // Каталог мог остаться заблокированным конструктором — на результат прогона это не влияет.
        }
    }

    /// <summary>Обёртка занимает место элемента, элемент становится её первым ребёнком.</summary>
    [Test]
    public void Wrap_TakesElementPlace_AndKeepsElementInside()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.Errors, Is.Empty, _report.Description);
            Assert.That(_report.OpenInDesigner, Is.True, "файл обязан быть открыт вкладкой конструктора");
            Assert.That(_report.WrapInSequenceOk, Is.True, "обёртка в Sequence: " + _report.Description);
            Assert.That(_report.OrderAfterSequenceWrap, Does.StartWith("Sequence:guarded"),
                "обёртка встала на место первого элемента: " + _report.OrderAfterSequenceWrap);
            Assert.That(_report.FirstChildOfWrapper, Is.EqualTo("WriteLine:Log first step"),
                "обёрнутый шаг обязан оказаться внутри обёртки: " + _report.FirstChildOfWrapper);
            Assert.That(_report.ActivityCountAfterSequenceWrap, Is.EqualTo(_report.ActivityCountBefore + 1),
                "обёртка добавляет одну активность: " + _report.Description);
        });
    }

    /// <summary>Адрес обёртки — адрес, который был у элемента; по нему агент правит дальше.</summary>
    [Test]
    public void Wrap_ReturnsAddressesFromOutline()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.WrapInSequenceWrapperAddress, Is.EqualTo("root/Activities[0]"),
                "обёртка заняла адрес обёрнутого элемента: " + _report.WrapInSequenceWrapperAddress);
            Assert.That(_report.WrapInSequenceWrapperId, Is.EqualTo("root/Activities[0]/Activities[0]"),
                "ответ называет новый адрес обёрнутой активности: " + _report.WrapInSequenceWrapperId);
            Assert.That(_report.WrapInIfWrapperAddress, Is.EqualTo("root/Activities[1]"),
                "вторая обёртка — по адресу второго элемента: " + _report.WrapInIfWrapperAddress);
            Assert.That(_report.WrapInIfWrapperId, Is.EqualTo("root/Activities[1]/Then"),
                "обёрнутая активность лежит в Then: " + _report.WrapInIfWrapperId);
        });
    }

    /// <summary>Обёртка с несколькими контейнерами (<c>If</c>) требует явного <c>container</c>.</summary>
    [Test]
    public void Wrap_AmbiguousContainer_IsRefusedWithList()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.WrapWithoutContainerOk, Is.False, "выбор контейнера не угадывается");
            Assert.That(_report.WrapWithoutContainerErrors, Is.Not.Empty, "причина отказа приходит текстом");
            Assert.That(
                string.Join(" ", _report.WrapWithoutContainerErrors),
                Does.Contain("container"),
                "ответ обязан подсказать аргумент container");
        });
    }

    /// <summary>Явный контейнер (<c>Then</c>) кладёт элемент именно туда.</summary>
    [Test]
    public void Wrap_ExplicitContainer_PutsElementThere()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.WrapInIfThenOk, Is.True, "обёртка в If.Then: " + _report.Description);
            Assert.That(_report.ThenChild, Is.EqualTo("Sequence:Nested sequence"),
                "обёрнутый элемент лежит в Then: " + _report.ThenChild);
            Assert.That(_report.OrderAfterIfWrap, Does.Contain("If:"),
                "порядок детей корня: " + _report.OrderAfterIfWrap);
        });
    }

    /// <summary>Неизвестный тип обёртки — отказ, а не исключение; модель не изменилась.</summary>
    [Test]
    public void Wrap_UnknownType_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.WrapUnknownTypeOk, Is.False, "обёртки NoSuchActivity не существует");
            Assert.That(
                string.Join(" ", _report.WrapUnknownTypeErrors),
                Does.Contain("NoSuchActivity"),
                "причина обязана называть тип: " + string.Join(" ", _report.WrapUnknownTypeErrors));
            Assert.That(_report.CompleteOk, Is.True, _report.Description);
            Assert.That(_report.ActivityCountAfterComplete, Is.EqualTo(_report.ActivityCountBefore + 2),
                "в модели остались ровно две обёртки: " + _report.Description);
        });
    }

    /// <summary>Обёртка — одна запись истории отмены: <c>undo</c> снимает её целиком.</summary>
    [Test]
    public void Wrap_IsOneUndoUnit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.UndoOk, Is.True, _report.Description);
            Assert.That(_report.ActivityCountAfterUndo, Is.EqualTo(_report.ActivityCountBefore + 1),
                "отменилась одна обёртка: " + _report.Description);
            Assert.That(_report.OrderAfterUndo, Is.EqualTo("Sequence:guarded | Sequence:Nested sequence"),
                "структура вернулась к состоянию после первой обёртки: " + _report.OrderAfterUndo);
        });
    }

    private static string WriteWorkflow(string fileName)
    {
        string source = Path.Combine(
            RepositoryRoot(),
            "AiRpaStudio.Tests",
            "Fixtures",
            "Workflow",
            "sequence.xaml");

        string target = Path.Combine(_workDirectory, fileName);
        File.WriteAllText(target, File.ReadAllText(source, Encoding.UTF8), new UTF8Encoding(false));

        return target;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException("не найден корень репозитория (AiRpa.sln)");
        }

        return directory.FullName;
    }
}
