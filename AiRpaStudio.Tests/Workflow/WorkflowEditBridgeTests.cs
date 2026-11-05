using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AiRpaStudio.Tests.Design;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-02 на живом конструкторе: пачка правок открытого процесса, <c>complete</c>, <c>undo</c>/<c>redo</c>,
/// откат пачки при ошибке, удаление и перенос, структура несохранённого документа.
/// <para>
/// Проверяется только на настоящем <c>WorkflowDesigner</c>: <c>ModelEditingScope</c>, история отмены
/// и <c>ModelService</c> существуют лишь внутри загруженной вкладки, и «мост, который работает на
/// фейковой модели» ничего не доказывает. Сценарий целиком выполняется в домене стенда
/// (<see cref="DesignerBenchProxy"/>): конструктор нельзя выносить за пределы его STA-потока,
/// а статические кэши WF нельзя смешивать с кэшами тестов темы.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowEditBridgeTests
{
    private static readonly object ReportGate = new object();

    private static DesignerBenchEditReport _report;
    private static string _workDirectory;

    [OneTimeSetUp]
    public void RunEditScenarioOnLiveDesigner()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "AiRpaStudio-EditBridge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);

        string openPath = WriteWorkflow("Main.xaml");
        string unopenedPath = WriteWorkflow("NotOpened.xaml");

        _report = DesignerBenchProxy.Shared.WorkflowEditScenario(openPath, unopenedPath);
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

    /// <summary>Пачка из трёх вставок видна в живой модели до сохранения и закрывается одной правкой.</summary>
    [Test]
    public void Batch_InsertsActivities_AndIsOneUndoUnit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.Errors, Is.Empty, _report.Description);
            Assert.That(_report.OpenInDesigner, Is.True, "файл обязан быть открыт вкладкой конструктора");
            Assert.That(_report.SessionActive, Is.True, _report.Description);
            Assert.That(_report.InsertStartOk, Is.True, "вставка в начало Sequence: " + _report.Description);
            Assert.That(_report.InsertMiddleOk, Is.True, "вставка в середину Sequence: " + _report.Description);
            Assert.That(_report.InsertEndOk, Is.True, "вставка в конец Sequence (index не задан): " + _report.Description);
            Assert.That(_report.ActivityCountDuringBatch, Is.EqualTo(_report.ActivityCountBefore + 3),
                "вставленные активности обязаны быть в модели сразу: " + _report.Description);
            Assert.That(_report.CompleteOk, Is.True, _report.Description);
            Assert.That(_report.SessionActiveAfterComplete, Is.False, "после complete пачка закрыта");
            Assert.That(_report.CanUndoAfterComplete, Is.True,
                "после complete в конструкторе есть что отменить: " + _report.Description);
            Assert.That(_report.ActivityCountAfterComplete, Is.EqualTo(_report.ActivityCountBefore + 3));
        });
    }

    /// <summary>Адреса новых элементов приходят из ответа и попадают в структуру документа.</summary>
    [Test]
    public void Insert_ReturnsAddressesThatAppearInOutline()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.InsertStartId, Is.Not.Null.And.Not.Empty,
                "вставка обязана вернуть адрес новой активности: " + _report.Description);
            Assert.That(_report.InsertStartId, Is.EqualTo("root/Activities[0]"),
                "вставка в начало Sequence даёт первый адрес: " + _report.Description);
            Assert.That(_report.InsertMiddleId, Is.EqualTo("root/Activities[1]"),
                "вставка в середину даёт второй адрес: " + _report.Description);
            Assert.That(_report.InsertEndId, Is.EqualTo("root/Activities[4]"),
                "вставка без индекса — в конец коллекции: " + _report.Description);
            Assert.That(_report.OrderDuringBatch, Does.Contain("WriteLine"),
                "в структуре открытого документа видна вставленная активность: " + _report.OrderDuringBatch);
        });
    }

    /// <summary>Отмена снимает одну правку (одну запись истории), возврат — повторяет её.</summary>
    [Test]
    public void UndoRedo_UndoesOneEditAndReturnsIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.LiveOutlineBeforeSave, Is.EqualTo(_report.ActivityCountBefore + 3),
                "структура несохранённого документа обязана включать правки: " + _report.Description);
            Assert.That(_report.UndoOk, Is.True, _report.Description);
            Assert.That(_report.ActivityCountAfterUndo, Is.EqualTo(_report.ActivityCountBefore + 2),
                "Ctrl+Z снимает одну правку — одну запись истории: " + _report.Description);
            Assert.That(_report.CanRedoAfterUndo, Is.True, _report.Description);
            Assert.That(_report.RedoOk, Is.True, _report.Description);
            Assert.That(_report.ActivityCountAfterRedo, Is.EqualTo(_report.ActivityCountBefore + 3),
                "Ctrl+Y возвращает отменённую правку: " + _report.Description);
        });
    }

    /// <summary>Пачка умеет отменить себя целиком: две правки — два шага истории, модель как до BeginEdit.</summary>
    [Test]
    public void Revert_CancelsWholeBatch()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.RevertInsertOk, Is.True, "обе вставки пачки применились: " + _report.Description);
            Assert.That(_report.ActivityCountDuringRevertBatch, Is.EqualTo(_report.ActivityCountBefore + 5),
                "правки пачки видны в модели до отката: " + _report.Description);
            Assert.That(_report.RevertOk, Is.True, _report.Description);
            Assert.That(_report.RevertSessionActive, Is.False, "после revert пачка закрыта");
            Assert.That(_report.ActivityCountAfterRevert, Is.EqualTo(_report.ActivityCountBefore + 3),
                "откат пачки вернул модель к состоянию до неё: " + _report.Description);
        });
    }

    /// <summary>Отказ правки отменяет только её: уже применённые правки пачки остаются, причина видна.</summary>
    [Test]
    public void FailedEdit_RevertsOnlyItself()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.FailedInsertOk, Is.False, "вставка в несуществующего родителя обязана отклоняться");
            Assert.That(_report.FailedInsertErrors, Is.Not.Empty, "причина отказа приходит текстом");
            Assert.That(_report.SessionActiveAfterFailure, Is.True,
                "после отказа пачка остаётся открытой: агент исправляет адрес и повторяет правку");
            Assert.That(_report.ActivityCountAfterFailure, Is.EqualTo(_report.ActivityCountBeforeFailure),
                "модель вернулась к состоянию до пачки: " + _report.Description);
        });
    }

    /// <summary>Удаление и перенос — те же адреса; несуществующий адрес — отказ, а не исключение.</summary>
    [Test]
    public void DeleteAndMove_UseOutlineAddresses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.DeleteOk, Is.True, "удаление по адресу из outline: " + _report.Description);
            Assert.That(_report.ActivityCountAfterDelete, Is.EqualTo(_report.ActivityCountAfterRedo - 1),
                "после удаления активностей на одну меньше: " + _report.Description);
            Assert.That(_report.MoveOk, Is.True, "перенос по адресам из outline: " + _report.Description);
            Assert.That(_report.MoveNewId, Is.Not.Null.And.Not.Empty, "перенос обязан вернуть новый адрес элемента");
            Assert.That(_report.OrderBeforeMove, Is.Not.EqualTo(_report.OrderAfterMove),
                "перенос в начало меняет порядок детей корня: " + _report.Description);
            Assert.That(_report.DeleteMissingOk, Is.False, "несуществующий адрес — отказ");
            Assert.That(_report.SecondBatchCompleteOk, Is.True, _report.Description);
        });
    }

    /// <summary>Файл, не открытый вкладкой, — структурированный отказ с причиной.</summary>
    [Test]
    public void NotOpenFile_IsRefusedWithReason()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.OpenBeforeAnyTab, Is.False, "до открытия вкладки файл считается неоткрытым");
            Assert.That(_report.NotOpenSessionActive, Is.False);
            Assert.That(_report.NotOpenReason, Does.Contain("не открыт"),
                "агент обязан увидеть причину: " + _report.NotOpenReason);
            Assert.That(_report.NotOpenInsertOk, Is.False);
            Assert.That(_report.NotOpenInsertErrors, Is.Not.Empty);
        });
    }

    /// <summary>
    /// AG-06: валидация читает живую модель конструктора (вместе с несохранёнными правками) и привязывает
    /// ошибку к адресу новой активности — тому же, что отдаёт <c>activity_insert</c>. Файл на диске при этом
    /// не менялся, поэтому «источник: файл» здесь был бы ложью.
    /// </summary>
    [Test]
    public void Validate_ReportsUnsavedIssueWithAddress()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.ValidateInsertOk, Is.True, _report.Description);
            Assert.That(_report.ValidateAvailable, Is.True, "валидация обязана быть выполненной: " + _report.Description);
            Assert.That(_report.ValidateSource, Is.EqualTo("конструктор"),
                "источник — живая модель, а не файл: " + _report.Description);
            Assert.That(_report.ValidateErrors, Is.GreaterThan(0),
                "Assign без To невалиден: " + _report.ValidateText);
            Assert.That(_report.ValidateAddresses, Does.Contain(_report.ValidateInsertId),
                "ошибка привязана к адресу новой активности: " + _report.ValidateText);
            Assert.That(_report.ValidateText, Does.Contain("workflow_validate"),
                "ответ обязан подсказывать цикл «правка → валидация → правка»");
        });
    }

    /// <summary>AG-06: валидация — чтение. Она не должна оставлять в конструкторе ни одной записи истории отмены.</summary>
    [Test]
    public void Validate_DoesNotGrowUndoHistory()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.ValidateUndoDepthBefore, Is.GreaterThanOrEqualTo(0),
                "история отмены обязана быть доступна: " + _report.Description);
            Assert.That(_report.ValidateUndoDepthAfter, Is.EqualTo(_report.ValidateUndoDepthBefore),
                "валидация не меняет модель: " + _report.Description);
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
