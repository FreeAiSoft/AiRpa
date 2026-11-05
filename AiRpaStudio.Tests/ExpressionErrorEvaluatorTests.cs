using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Правила проверки выражений (<see cref="ExpressionErrorEvaluator"/>) — логика, вынесенная из
/// <c>DesignerValidator.GetArgumentErrors</c>.
/// <para>
/// Причина выноса: там чтение модели и компиляция Roslyn были одним куском на UI-потоке, поэтому
/// «фоновая валидация» была невозможна. Теперь модель читается на UI-потоке
/// (<see cref="ActivityExpressionCheck"/>), а проверка идёт в фоне по снимкам.
/// </para>
/// <para>
/// Тесты держат два обязательства: (1) замечание всегда приходит <b>с адресом</b> активности —
/// иначе иконка встанет не туда; (2) сообщения остаются прежними, потому что на них смотрят
/// пользователь и существующие проверки.
/// </para>
/// </summary>
[TestFixture]
public class ExpressionErrorEvaluatorTests
{
    [Test]
    public void Evaluate_OutArgument_KnownName_HasNoErrors()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.OutArgument, "result");
        check.Environment = Env(("result", typeof(string)));

        Assert.That(ExpressionErrorEvaluator.Evaluate(new[] { check }, new FakeCompiler()), Is.Empty);
    }

    [Test]
    public void Evaluate_OutArgument_UnknownName_KeepsAddress()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.OutArgument, "missing");
        check.Address = "root/Activities[3]/Catches[0]/Action/Handler";

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, new FakeCompiler());

        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Address, Is.EqualTo(check.Address),
                "без адреса результат фоновой проверки не сопоставить с активностью");
            Assert.That(errors[0].Message, Is.EqualTo("Value: missing не существует в текущем контексте."));
        });
    }

    [Test]
    public void Evaluate_Expression_TypeMismatch_ReportsInArgument()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.Expression, "text");
        check.ArgumentType = typeof(int);
        check.CompileText = "text";

        FakeCompiler compiler = new FakeCompiler { Type = typeof(string) };

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler);

        Assert.That(errors.Select(e => e.Message), Is.EqualTo(new[] { "Value: System.String невозможно присвоить типу System.Int32." }));
    }

    [Test]
    public void Evaluate_Expression_AssignableType_HasNoErrors()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.Expression, "text");
        check.ArgumentType = typeof(object);

        FakeCompiler compiler = new FakeCompiler { Type = typeof(string) };

        Assert.That(ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler), Is.Empty);
    }

    [Test]
    public void Evaluate_CompilationErrors_ArePrefixedWithArgumentName()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.Expression, "1 +");

        FakeCompiler compiler = new FakeCompiler { Type = null, Errors = new List<string> { "; expected" } };

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler);

        Assert.That(errors.Select(e => e.Message), Is.EqualTo(new[] { "Value: ; expected" }));
    }

    [Test]
    public void Evaluate_Literal_WrongType_ReportsAgainstArgumentType()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.Literal, "\"abc\"");
        check.ArgumentType = typeof(int);
        check.LiteralType = typeof(string);
        check.CompileText = "\"abc\"";

        FakeCompiler compiler = new FakeCompiler { Type = typeof(string) };

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler);

        Assert.That(errors.Select(e => e.Message), Is.EqualTo(new[]
        {
            "Value: тип литерала System.String не соответствует типу аргумента System.Int32",
            "Value: тип литерала System.String (выведенный компилятором) не соответствует типу аргумента System.Int32"
        }));
    }

    [Test]
    public void Evaluate_Literal_MatchesArgumentType_HasNoErrors()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.Literal, "\"abc\"");
        check.ArgumentType = typeof(string);
        check.LiteralType = typeof(string);

        FakeCompiler compiler = new FakeCompiler { Type = typeof(string) };

        Assert.That(ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler), Is.Empty);
    }

    [Test]
    public void Evaluate_AssignValue_KnownName_ChecksLeftSideType()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.AssignValue, "text");
        check.ExpressionItemType = typeof(int);
        check.TargetType = typeof(int);
        check.CompileText = "text";
        check.Environment = Env(("text", typeof(string)));

        FakeCompiler compiler = new FakeCompiler { Type = typeof(string) };

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler);

        Assert.That(errors.Select(e => e.Message), Is.EqualTo(new[]
        {
            "Value: не возможно присвоить тип System.String переменной типа System.Int32."
        }));
    }

    [Test]
    public void Evaluate_AssignValue_UnknownName_ReportsContext()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.AssignValue, "ghost");
        check.ExpressionItemType = typeof(string);
        check.TargetType = typeof(string);
        check.CompileText = "ghost";

        FakeCompiler compiler = new FakeCompiler { Type = typeof(string) };

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler);

        Assert.That(errors.Select(e => e.Message), Is.EqualTo(new[] { "Value: ghost не существует в текущем контексте." }));
    }

    [Test]
    public void Evaluate_CompilerThrows_TurnsIntoNoteNotCrash()
    {
        ActivityExpressionCheck check = Check(ExpressionCheckKind.Expression, "text");

        FakeCompiler compiler = new FakeCompiler { Throw = new InvalidOperationException("нет ссылки на сборку") };

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, compiler);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Message, Does.Contain("не удалось проверить выражение"));
            Assert.That(errors[0].Address, Is.EqualTo(check.Address));
        });
    }

    [Test]
    public void Evaluate_Cancelled_StopsBeforeNextExpression()
    {
        ActivityExpressionCheck first = Check(ExpressionCheckKind.Expression, "a");
        ActivityExpressionCheck second = Check(ExpressionCheckKind.Expression, "b");

        FakeCompiler compiler = new FakeCompiler { Type = typeof(int) };
        using CancellationTokenSource source = new CancellationTokenSource();
        source.Cancel();

        Assert.That(ExpressionErrorEvaluator.Evaluate(new[] { first, second }, compiler, source.Token), Is.Empty,
            "устаревший проход обязан остановиться, а не досчитывать весь документ");
        Assert.That(compiler.Calls, Is.EqualTo(0));
    }

    [Test]
    public void Evaluate_DifferentActivities_KeepOwnAddresses()
    {
        ActivityExpressionCheck first = Check(ExpressionCheckKind.Expression, "a");
        first.Address = "root/Activities[0]";
        first.ArgumentType = typeof(int);

        ActivityExpressionCheck second = Check(ExpressionCheckKind.Expression, "b");
        second.Address = "root/Activities[1]/Body/Handler";
        second.ArgumentType = typeof(int);

        FakeCompiler compiler = new FakeCompiler { Type = typeof(string), Errors = new List<string> { "ошибка" } };

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { first, second }, compiler);

        Assert.That(errors.Select(e => e.Address).Distinct(),
            Is.EquivalentTo(new[] { "root/Activities[0]", "root/Activities[1]/Body/Handler" }));
    }

    [Test]
    public void Evaluate_NullCompiler_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ExpressionErrorEvaluator.Evaluate(new[] { Check(ExpressionCheckKind.Expression, "a") }, null));
    }

    // --- Вспомогательное. ---

    private static ActivityExpressionCheck Check(ExpressionCheckKind kind, string text)
    {
        return new ActivityExpressionCheck
        {
            Address = "root",
            ActivityTypeName = "WriteLine",
            ArgumentName = "Value",
            Kind = kind,
            ExpressionText = text,
            CompileText = text,
            Environment = ExpressionEnvironment.Empty
        };
    }

    private static ExpressionEnvironment Env(params (string Name, Type Type)[] names)
    {
        return new ExpressionEnvironment(
            names.Select(pair => new KeyValuePair<string, Type>(pair.Name, pair.Type)),
            string.Empty);
    }

    /// <summary>Компилятор-подделка: правила проверяются без Roslyn и без WF.</summary>
    private sealed class FakeCompiler : IExpressionCompiler
    {
        internal Type Type { get; set; } = typeof(int);

        internal List<string> Errors { get; set; }

        internal Exception Throw { get; set; }

        internal int Calls { get; private set; }

        public (Type Type, List<string> Errors) Check(string expressionText, ExpressionEnvironment environment)
        {
            Calls++;
            if (Throw != null)
            {
                throw Throw;
            }

            return (Type, Errors);
        }
    }
}
