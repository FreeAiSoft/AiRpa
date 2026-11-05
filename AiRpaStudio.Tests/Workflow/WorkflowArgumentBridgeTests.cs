using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AiRpaStudio.Tests.Design;

namespace AiRpaStudio.Tests.Workflow;

/// <summary>
/// AG-05 на живом конструкторе: типы аргументов, литерал и выражение, переменные.
/// <para>
/// Проверяется только на настоящем <c>WorkflowDesigner</c>: дескриптор аргумента с реальным типом,
/// <c>InArgument&lt;T&gt;</c> с <c>CSharpValue&lt;T&gt;</c> и коллекция <c>Variables</c> существуют лишь
/// внутри загруженной вкладки. Сценарий целиком выполняется в домене стенда
/// (<see cref="DesignerBenchProxy"/>): конструктор нельзя выносить за пределы его STA-потока,
/// а статические кэши WF нельзя смешивать с кэшами тестов темы.
/// </para>
/// </summary>
[TestFixture]
public class WorkflowArgumentBridgeTests
{
    private static DesignerBenchArgumentReport _report;
    private static string _workDirectory;

    [OneTimeSetUp]
    public void RunArgumentScenarioOnLiveDesigner()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "AiRpaStudio-Arguments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);

        string path = Path.Combine(_workDirectory, "Main.xaml");

        string source = Path.Combine(
            RepositoryRoot(),
            "AiRpaStudio.Tests",
            "Fixtures",
            "Workflow",
            "sequence.xaml");

        File.WriteAllText(path, File.ReadAllText(source, Encoding.UTF8), new UTF8Encoding(false));

        _report = DesignerBenchProxy.Shared.WorkflowArgumentScenario(path);
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

    /// <summary>Сценарий целиком: все пункты контракта AG-05 выполнены.</summary>
    [Test]
    public void Scenario_SetsArgumentsAndManagesVariables()
    {
        Assert.That(_report.Ok, Is.True, _report.Description);
        Assert.That(_report.Errors, Is.Empty, _report.Description);
    }

    /// <summary>Литерал задаёт аргумент по реальному типу, и значение видно в живой структуре.</summary>
    [Test]
    public void Literal_IsAppliedToLiveModel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.OpenInDesigner, Is.True, "файл обязан быть открыт вкладкой конструктора");
            Assert.That(_report.SessionActive, Is.True, _report.Description);
            Assert.That(_report.LiteralOk, Is.True, "литерал в WriteLine.Text: " + _report.Description);
            Assert.That(_report.LiteralMessage, Does.Contain("Text"), "в ответе видно, какой аргумент задан: " + _report.LiteralMessage);
            Assert.That(_report.TextAfterLiteral, Does.Contain("hello from the agent"),
                "значение обязано попасть в структуру процесса: " + _report.TextAfterLiteral);
        });
    }

    /// <summary>Выражение подставляется как CSharpValue&lt;T&gt; — конструктор его принимает.</summary>
    [Test]
    public void Expression_IsAppliedAsCSharpValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.ExpressionOk, Is.True, "выражение в WriteLine.Text: " + _report.Description);
            Assert.That(_report.ExpressionMessage, Does.Contain("csharp"),
                "в ответе виден язык выражения: " + _report.ExpressionMessage);
            Assert.That(_report.TextAfterExpression, Is.Not.Null.And.Not.Empty,
                "выражение обязано попасть в структуру процесса");
        });
    }

    /// <summary>Отказы приходят до правки модели и объясняют причину.</summary>
    [Test]
    public void InvalidRequests_AreRejectedWithReason()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.UnknownArgumentOk, Is.False, "неизвестный аргумент обязан отклоняться");
            Assert.That(_report.UnknownArgumentErrors, Is.Not.Empty);
            Assert.That(string.Join(" ", _report.UnknownArgumentErrors), Does.Contain("NoSuchArgument"),
                "в отказе видно, какого аргумента нет");

            Assert.That(_report.BothOk, Is.False, "value и expression одновременно — ошибка запроса");
            Assert.That(string.Join(" ", _report.BothErrors), Does.Contain("expression"));

            Assert.That(_report.BrokenExpressionOk, Is.False, "битое выражение обязано отклоняться");
            Assert.That(string.Join(" ", _report.BrokenExpressionErrors), Does.Not.Empty);
        });
    }

    /// <summary>Переменная объявляется в активности с коллекцией <c>Variables</c> и сразу видна.</summary>
    [Test]
    public void Variable_IsDeclaredAndRemoved()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.VariableAddOk, Is.True, "переменная в корне процесса: " + _report.Description);
            Assert.That(_report.VariableAddMessage, Does.Contain("counter"));
            Assert.That(_report.VariablesAfterAdd, Does.Contain("counter"),
                "переменная обязана появиться в структуре процесса");
            Assert.That(_report.VariableRemoveOk, Is.True, _report.Description);
            Assert.That(_report.VariablesAfterRemove, Does.Not.Contain("counter"),
                "после удаления переменной в структуре её нет");
        });
    }

    /// <summary>Негативы переменных: дубль, неизвестный тип, неверное значение, активность без Variables.</summary>
    [Test]
    public void VariableNegatives_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.DuplicateVariableOk, Is.False, "повторное имя обязано отклоняться");
            Assert.That(string.Join(" ", _report.DuplicateVariableErrors), Does.Contain("уже объявлена"));

            Assert.That(_report.UnknownTypeOk, Is.False, "тип вне списка студии обязан отклоняться");
            Assert.That(string.Join(" ", _report.UnknownTypeErrors), Does.Contain("Money"),
                "в отказе виден неизвестный тип");

            Assert.That(_report.BadValueOk, Is.False, "значение не того типа обязано отклоняться");
            Assert.That(string.Join(" ", _report.BadValueErrors), Does.Not.Empty);

            Assert.That(_report.NoVariablesOk, Is.False, "у активности без Variables объявлять нечего");
            Assert.That(string.Join(" ", _report.NoVariablesErrors), Does.Contain("Variables"));

            Assert.That(_report.RemoveMissingOk, Is.False, "повторное удаление переменной — отказ");
        });
    }

    /// <summary>Правки закрываются одной пачкой, отмена штатная.</summary>
    [Test]
    public void Edits_AreCommittedAndUndone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_report.CompleteOk, Is.True, _report.Description);
            Assert.That(_report.UndoOk, Is.True, "после пачки в конструкторе есть что отменить: " + _report.Description);
        });
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
