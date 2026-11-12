using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RPA.Core.Generation;
using RPA.Core.Services;

namespace AiRpaStudio.Tests;

/// <summary>
/// Регресс валидатора выражений по эталонным XAML-файлам: правая часть <c>Assign</c> — это любое
/// C#-выражение, а не только имя переменной. Эталонные файлы лежат в
/// <c>AiRpaStudio.Tests\Fixtures\Workflow</c> и повторяют сценарий <c>RPA-1\Main.xaml</c>
/// (<c>Assign</c> с литералом <c>"Hello from RPA-1"</c>).
/// <para>
/// Проверка идёт на настоящем компиляторе Roslyn (<see cref="RoslynExpressionCompiler"/>), без подделок:
/// снимок <see cref="ActivityExpressionCheck"/> строится из загруженного эталона, а результат оценивает
/// <see cref="ExpressionErrorEvaluator"/>.
/// </para>
/// </summary>
[TestFixture]
public class ExpressionValidatorRegressionTests
{
    [Test]
    public void Assign_StringLiteral_IsValid()
    {
        ActivityExpressionCheck check = BuildAssignValueCheck("assign-string-literal.xaml");

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, RoslynExpressionCompiler.Instance);

        Assert.That(errors, Is.Empty,
            "литерал в правой части Assign обязан быть валидным: "
            + string.Join(Environment.NewLine, errors.Select(error => error.Message)));
    }

    [Test]
    public void Assign_UnknownName_IsStillReported()
    {
        ActivityExpressionCheck check = BuildAssignValueCheck("assign-unknown-name.xaml");

        IReadOnlyList<ActivityValidationError> errors =
            ExpressionErrorEvaluator.Evaluate(new[] { check }, RoslynExpressionCompiler.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Not.Empty,
                "неизвестное имя в правой части Assign обязано оставаться ошибкой");
            Assert.That(
                string.Join(Environment.NewLine, errors.Select(error => error.Message)),
                Does.Contain("ghost"),
                "ошибка должна указывать на имя, которое не удалось разрешить");
        });
    }

    /// <summary>
    /// Снимок проверки правой части <c>Assign</c> из эталонного файла: тип левой части, тип аргумента и
    /// тип узла выражения берутся из загруженной модели — так же, как их снимает
    /// <c>DesignerValidator.CreateArgumentChecks</c> с живой модели.
    /// </summary>
    private static ActivityExpressionCheck BuildAssignValueCheck(string fileName)
    {
        WorkflowSnapshot snapshot = WorkflowModelReader.Read(FixturePath(fileName));
        Assert.That(snapshot.Ok, Is.True, string.Join(Environment.NewLine, snapshot.Errors));

        WorkflowActivitySnapshot assignNode = snapshot.All().First(node => node.Activity is Assign);
        var assign = (Assign)assignNode.Activity;

        string expressionText = WorkflowModelReader.ExpressionText(assign.Value);

        return new ActivityExpressionCheck
        {
            Address = assignNode.Id,
            ActivityTypeName = assign.GetType().Name,
            ArgumentName = "Value",
            Kind = ExpressionCheckKind.AssignValue,
            ExpressionText = expressionText,
            CompileText = expressionText,
            ArgumentType = assign.Value.ArgumentType,
            ExpressionItemType = assign.Value.Expression?.GetType(),
            TargetType = assign.To.ArgumentType,
            Environment = BuildEnvironment(snapshot.Root.Activity)
        };
    }

    /// <summary>Переменные в области видимости корневой активности эталона (для сценария RPA-1 — <c>output</c>).</summary>
    private static ExpressionEnvironment BuildEnvironment(Activity rootActivity)
    {
        var names = new Dictionary<string, Type>(StringComparer.Ordinal);

        PropertyInfo variablesProperty = rootActivity.GetType()
            .GetProperty("Variables", BindingFlags.Public | BindingFlags.Instance);

        if (variablesProperty?.GetValue(rootActivity) is IEnumerable<Variable> variables)
        {
            foreach (Variable variable in variables)
            {
                if (variable != null && !string.IsNullOrWhiteSpace(variable.Name))
                {
                    names[variable.Name] = variable.Type ?? typeof(object);
                }
            }
        }

        return new ExpressionEnvironment(names, string.Empty);
    }

    private static string FixturePath(string fileName)
    {
        return Path.Combine(RepositoryRoot(), "AiRpaStudio.Tests", "Fixtures", "Workflow", fileName);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AiRpa.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "не найден корень репозитория (AiRpa.sln)");
        return directory.FullName;
    }
}
