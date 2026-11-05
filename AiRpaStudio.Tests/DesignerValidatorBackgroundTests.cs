using NUnit.Framework;

namespace AiRpaStudio.Tests;

/// <summary>
/// Стражи связки фоновой валидации в <c>DesignerValidator</c>: правила, которые нельзя откатить
/// незаметно.
/// <para>
/// Проверки по исходнику, а не по поведению, потому что весь проход привязан к <c>ModelItem</c>
/// и живой поверхности конструктора (STA-поток, шаблоны WF). Поведенческие проверки живут в
/// <see cref="DesignerValidationScopeTests"/>, <see cref="ExpressionErrorEvaluatorTests"/> и
/// <see cref="DesignerValidationPassTests"/> — там логика без UI.
/// </para>
/// </summary>
[TestFixture]
public class DesignerValidatorBackgroundTests
{
    private const string ValidatorPath = @"AiRpaStudio\CSharpExpressionEditor\DesignerValidator.cs";

    [Test]
    public void Validator_UsesFullScopeTraversal()
    {
        string source = ValidationSourceReader.Read(ValidatorPath);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("DesignerValidationScope.Collect"),
                "обход модели обязан идти через общий обход с адресами: тела ForEach, ветки Pick и кастомные контейнеры");
            Assert.That(source, Does.Not.Contain("GetModelItemChilds"),
                "прежний обход с else { break; } обрывал коллекцию и не видел тела циклов");
        });
    }

    [Test]
    public void Validator_RunsExpressionChecksInBackground()
    {
        string source = ValidationSourceReader.Read(ValidatorPath);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("Task.Run("),
                "компиляция выражений должна уходить с UI-потока: иначе она блокирует дизайнер");
            Assert.That(source, Does.Contain("ExpressionErrorEvaluator.Evaluate"),
                "проверка выражений выполняется по снимкам, без чтения модели в фоне");
            Assert.That(source, Does.Not.Contain("RoslynExpressionValidator.Instance.CheckExpression"),
                "прямой вызов компиляции из прохода возвращает блокировку UI-потока");
        });
    }

    [Test]
    public void Validator_AppliesResultOnlyForCurrentGeneration()
    {
        string source = ValidationSourceReader.Read(ValidatorPath);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("_validationPass.TryComplete(generation)"),
                "результат устаревшего прохода обязан быть отброшен");
            Assert.That(source, Does.Contain("_validationPass.IsCurrent(generation)"),
                "между постановкой в очередь диспетчера и применением документ мог измениться");
        });
    }

    [Test]
    public void Validator_CancelsBackgroundPassOnDispose()
    {
        string source = ValidationSourceReader.Read(ValidatorPath);

        Assert.That(source, Does.Contain("_validationPass.Cancel()"),
            "при закрытии вкладки фоновый проход обязан отменяться: иначе он работает с освобождённым дизайнером");
    }

    [Test]
    public void Validator_KeepsWorkflowValidationOnUiThread()
    {
        string source = ValidationSourceReader.Read(ValidatorPath);

        Assert.That(source, Does.Contain("ValidateWorkflow()"),
            "ValidateWorkflow наполняет ModelItem.ValidationState, по которому рисует иконки сам WF: " +
            "переносить его в фон нельзя, пока не измерена цена (шаг 0 плана UI-02)");
    }

    [Test]
    public void Validator_DoesNotTreadOnIconsOfStalePass()
    {
        string source = ValidationSourceReader.Read(ValidatorPath);

        Assert.That(source, Does.Contain("SetValivationErrorsToModelItem"),
            "применение результата обязано идти через единственную точку установки иконки");
    }
}
