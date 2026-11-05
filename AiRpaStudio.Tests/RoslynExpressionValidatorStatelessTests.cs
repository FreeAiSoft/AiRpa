using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RPA.CSharpExpressionEditor;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Компиляция выражения по снимку окружения (<c>RoslynExpressionValidator.CheckExpression(text, environment)</c>).
/// <para>
/// Это вход, который использует фоновая валидация дизайнера: окружение снимается на UI-потоке,
/// компиляция идёт в фоновой задаче. Поэтому у метода нет изменяемого состояния — раньше блок
/// <c>using</c> и <see cref="Microsoft.CodeAnalysis.CSharp.CSharpCompilation"/> были полями синглтона,
/// общими для дизайнера и редактора выражения.
/// </para>
/// </summary>
[TestFixture]
public class RoslynExpressionValidatorStatelessTests
{
    [Test]
    public void CheckExpression_UsesPassedEnvironment()
    {
        (Type intType, List<string> intErrors) = RoslynExpressionValidator.CheckExpression("value", Env(("value", typeof(int))));
        (Type stringType, List<string> stringErrors) = RoslynExpressionValidator.CheckExpression("value", Env(("value", typeof(string))));

        Assert.Multiple(() =>
        {
            Assert.That(intErrors, Is.Null.Or.Empty);
            Assert.That(stringErrors, Is.Null.Or.Empty);
            Assert.That(intType, Is.EqualTo(typeof(int)));
            Assert.That(stringType, Is.EqualTo(typeof(string)),
                "тип выводится из переданного окружения, а не из состояния синглтона");
        });
    }

    [Test]
    public void CheckExpression_UnknownName_ReportsCompilationError()
    {
        (Type type, List<string> errors) = RoslynExpressionValidator.CheckExpression("ghost", ExpressionEnvironment.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(type, Is.Null);
            Assert.That(errors, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void CheckExpression_UsingNamespacesFromEnvironment()
    {
        // Без блока using обобщённый тип не разрешается — это и есть проверка того,
        // что окружение (а не поле синглтона) управляет компиляцией.
        (Type withoutUsing, List<string> withoutErrors) =
            RoslynExpressionValidator.CheckExpression("new List<int>()", ExpressionEnvironment.Empty);

        (Type withUsing, List<string> withErrors) = RoslynExpressionValidator.CheckExpression(
            "new List<int>()",
            new ExpressionEnvironment(null, "using System.Collections.Generic;" + Environment.NewLine));

        Assert.Multiple(() =>
        {
            Assert.That(withoutErrors, Is.Not.Null.And.Not.Empty, "без using тип List недоступен");
            Assert.That(withUsing, Is.EqualTo(typeof(List<int>)));
            Assert.That(withErrors, Is.Null.Or.Empty);
            Assert.That(withoutUsing, Is.Null);
        });
    }

    [Test]
    public void CheckExpression_GenericVariableInEnvironment_IsCompilable()
    {
        // Прежде имя типа подставлялось как Type.Name («List`1»), и выражение с переменной
        // обобщённого типа давало ложную ошибку компиляции.
        (Type type, List<string> errors) =
            RoslynExpressionValidator.CheckExpression("items", Env(("items", typeof(List<string>))));

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Null.Or.Empty, string.Join("; ", errors ?? new List<string>()));
            Assert.That(type, Is.EqualTo(typeof(List<string>)));
        });
    }

    [Test]
    public void CheckExpression_ParallelCalls_AreIndependent()
    {
        // Общий экземпляр компиляции в поле синглтона делал этот тест недетерминированным.
        Type[] results = new Type[16];

        Parallel.For(0, results.Length, index =>
        {
            Type expected = index % 2 == 0 ? typeof(int) : typeof(string);
            (Type type, _) = RoslynExpressionValidator.CheckExpression("value", Env(("value", expected)));
            results[index] = type;
        });

        for (int index = 0; index < results.Length; index++)
        {
            Type expected = index % 2 == 0 ? typeof(int) : typeof(string);
            Assert.That(results[index], Is.EqualTo(expected),
                "параллельные вызовы не должны влиять друг на друга");
        }
    }

    [Test]
    public void CSharpTypeName_FormatsGenericsAndArrays()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RoslynExpressionValidator.CSharpTypeName(typeof(int)), Is.EqualTo("System.Int32"));
            Assert.That(
                RoslynExpressionValidator.CSharpTypeName(typeof(List<string>)),
                Is.EqualTo("System.Collections.Generic.List<System.String>"));
            Assert.That(RoslynExpressionValidator.CSharpTypeName(typeof(int[])), Is.EqualTo("System.Int32[]"));
            Assert.That(RoslynExpressionValidator.CSharpTypeName(null), Is.EqualTo("object"));
        });
    }

    // --- Страж: изменяемое состояние не должно вернуться в синглтон. ---

    [Test]
    public void Validator_HasNoSharedMutableCompilation()
    {
        string source = ValidationSourceReader.Read(@"AiRpaStudio\CSharpExpressionEditor\RoslynExpressionValidator.cs");

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Not.Contain("private readonly CSharpCompilation _compilation;"),
                "общая компиляция в поле синглтона — это гонка между дизайнером и редактором выражения");
            Assert.That(source, Does.Not.Contain("RemoveAllSyntaxTrees"),
                "RemoveAllSyntaxTrees на общей компиляции снимает деревья чужого вызова");
            Assert.That(source, Does.Contain("private static CSharpCompilation CreateCompilation()"),
                "компиляция должна создаваться на вызов");
        });
    }

    private static ExpressionEnvironment Env(params (string Name, Type Type)[] names)
    {
        return new ExpressionEnvironment(
            names.Select(pair => new KeyValuePair<string, Type>(pair.Name, pair.Type)),
            string.Empty);
    }
}
