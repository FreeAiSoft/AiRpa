using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CSharp.Activities;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 4 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: сборка активности по описанию
/// палитры (<c>ActivityPaletteFactory</c>).
/// <para>
/// Проверяется главное требование п. 1.2: значения аргументов становятся
/// <see cref="CSharpValue{T}"/>/<see cref="CSharpReference{T}"/>, вложенные активности попадают
/// в реальные контейнеры, а собранная модель проходит гейт валидации (то есть открывается в конструкторе).
/// </para>
/// </summary>
[TestFixture]
public class ActivityPaletteFactoryTests
{
    private ActivityPaletteFactory _factory;

    /// <summary>Спецификация цикла <c>ForEach&lt;T&gt;</c> с обработчиком в <c>Body</c>.</summary>
    private static PaletteActivitySpec ForEachSpec(string typeName)
    {
        var spec = new PaletteActivitySpec("ForEach")
            .WithArgument("Values", PaletteValue.FromExpression("new string[0]"))
            .WithChild("Body", new PaletteActivitySpec("WriteLine")
                .WithArgument("Text", PaletteValue.FromExpression("item")));

        spec.TypeArguments.Add(typeName);
        return spec;
    }

    [SetUp]
    public void CreateFactory()
    {
        _factory = new ActivityPaletteFactory();
    }

    [Test]
    public void Build_WriteLineWithExpression_UsesCSharpValue()
    {
        PaletteActivitySpec spec = new PaletteActivitySpec("WriteLine")
            .WithArgument("Text", PaletteValue.FromExpression("\"Готово\""));

        PaletteBuildResult result = _factory.Build(spec);

        Assert.That(result.Ok, Is.True, result.Describe());

        var writeLine = (WriteLine)result.Activity;
        var expression = writeLine.Text?.Expression as CSharpValue<string>;

        Assert.Multiple(() =>
        {
            Assert.That(expression, Is.Not.Null, "In-аргумент обязан быть CSharpValue<T>, а не лямбдой");
            Assert.That(expression.ExpressionText, Is.EqualTo("\"Готово\""));
        });
    }

    [Test]
    public void Build_WriteLineWithLiteral_UsesPlainValue()
    {
        PaletteActivitySpec spec = new PaletteActivitySpec("WriteLine")
            .WithArgument("Text", PaletteValue.FromLiteral("привет"));

        PaletteBuildResult result = _factory.Build(spec);

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.That(((WriteLine)result.Activity).Text, Is.Not.Null);
    }

    [Test]
    public void Build_AssignWithExpressions_UsesCSharpReferenceForOut()
    {
        PaletteActivitySpec spec = new PaletteActivitySpec("Assign")
            .WithArgument("To", PaletteValue.FromExpression("output", "String"))
            .WithArgument("Value", PaletteValue.FromExpression("\"значение\"", "String"));

        PaletteBuildResult result = _factory.Build(spec);

        Assert.That(result.Ok, Is.True, result.Describe());

        var assign = (Assign)result.Activity;
        var reference = assign.To?.Expression as CSharpReference<string>;

        Assert.Multiple(() =>
        {
            Assert.That(reference, Is.Not.Null, "To — ссылка (CSharpReference<T>)");
            Assert.That(reference.ExpressionText, Is.EqualTo("output"));
            Assert.That(assign.Value?.Expression as CSharpValue<string>, Is.Not.Null);
        });
    }

    [Test]
    public void Build_IfWithThenElse_PutsActivitiesIntoContainers()
    {
        PaletteActivitySpec spec = new PaletteActivitySpec("If")
            .WithArgument("Condition", PaletteValue.FromExpression("true"))
            .WithChild("Then", new PaletteActivitySpec("WriteLine")
                .WithArgument("Text", PaletteValue.FromExpression("\"then\"")))
            .WithChild("Else", new PaletteActivitySpec("WriteLine")
                .WithArgument("Text", PaletteValue.FromExpression("\"else\"")));

        PaletteBuildResult result = _factory.Build(spec);

        Assert.That(result.Ok, Is.True, result.Describe());

        var branch = (If)result.Activity;

        Assert.Multiple(() =>
        {
            Assert.That(branch.Then, Is.TypeOf<WriteLine>(), "Then — свойство-активность, а не обработчик");
            Assert.That(branch.Else, Is.TypeOf<WriteLine>());
        });
    }

    [Test]
    public void Build_ForEachWithBody_WrapsHandlerInActivityAction()
    {
        PaletteActivitySpec spec = new PaletteActivitySpec("ForEach")
            .WithArgument("Values", PaletteValue.FromExpression("new string[0]"))
            .WithChild("Body", new PaletteActivitySpec("WriteLine")
                .WithArgument("Text", PaletteValue.FromExpression("item")));

        spec.TypeArguments.Add("String");

        PaletteBuildResult result = _factory.Build(spec);

        Assert.That(result.Ok, Is.True, result.Describe());

        var loop = (ForEach<string>)result.Activity;

        Assert.Multiple(() =>
        {
            Assert.That(loop.Body, Is.Not.Null, "Body — ActivityAction<T>, без обёртки активность не вставить");
            Assert.That(loop.Body.Handler, Is.TypeOf<WriteLine>());
            Assert.That(loop.Body.Argument?.Name, Is.EqualTo("item"));
            Assert.That(loop.Values?.Expression, Is.TypeOf<CSharpValue<IEnumerable<string>>>());
        });
    }

    [Test]
    public void Build_NestedProcess_PassesValidationGate()
    {
        PaletteActivitySpec root = new PaletteActivitySpec("Sequence")
            .WithChild("Activities", new PaletteActivitySpec("If")
                .WithArgument("Condition", PaletteValue.FromExpression("true"))
                .WithChild("Then", new PaletteActivitySpec("WriteLine")
                    .WithArgument("Text", PaletteValue.FromExpression("\"then\""))))
            .WithChild("Activities", ForEachSpec("String"));

        PaletteBuildResult build = _factory.Build(root);
        Assert.That(build.Ok, Is.True, build.Describe());

        WorkflowBuildResult generated = WorkflowTestSamples.BuildService().Build(build.Activity);

        Assert.Multiple(() =>
        {
            Assert.That(generated.Ok, Is.True,
                "собранный палитрой процесс обязан проходить гейт валидации: " + generated.Describe());
            Assert.That(generated.Xaml, Does.Contain("ExpressionActivityEditor=\"C#\""));
            Assert.That(generated.Xaml, Does.Contain("mca:CSharpValue"));
            Assert.That(new[] { "If", "ForEach", "WriteLine" }
                .All(name => generated.Xaml.IndexOf("<" + name, StringComparison.Ordinal) >= 0), Is.True,
                "все вложенные активности должны попасть в XAML");
        });
    }

    [Test]
    public void Build_UnknownActivity_ReturnsErrorWithKnownNames()
    {
        PaletteBuildResult result = _factory.Build(new PaletteActivitySpec("НетТакойАктивности"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("не найдена в палитре"));
            Assert.That(result.Errors.Single(), Does.Contain("WriteLine"), "в ошибке перечислены известные активности");
        });
    }

    [Test]
    public void Build_UnknownArgument_ListsRealArguments()
    {
        PaletteBuildResult result = _factory.Build(new PaletteActivitySpec("WriteLine")
            .WithArgument("Текст", PaletteValue.FromExpression("\"x\"")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("нет аргумента «Текст»"));
            Assert.That(result.Errors.Single(), Does.Contain("Text"), "виноватый аргумент ищется по подсказке");
        });
    }

    [Test]
    public void Build_GenericWithoutTypeArguments_ReturnsErrorWithHint()
    {
        PaletteBuildResult result = _factory.Build(new PaletteActivitySpec("ForEach")
            .WithArgument("Values", PaletteValue.FromExpression("new string[0]")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("обобщённая"));
            Assert.That(result.Hints.Single(), Does.Contain("String"), "подсказка перечисляет допустимые типы");
        });
    }

    [Test]
    public void Build_OutArgumentWithLiteral_ReturnsError()
    {
        PaletteBuildResult result = _factory.Build(new PaletteActivitySpec("Assign")
            .WithArgument("To", PaletteValue.FromLiteral("output"))
            .WithArgument("Value", PaletteValue.FromExpression("\"x\"")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("ссылка"));
        });
    }

    [Test]
    public void Build_UnsupportedContainer_ReturnsErrorAndHint()
    {
        PaletteBuildResult result = _factory.Build(new PaletteActivitySpec("Flowchart")
            .WithChild("Nodes", new PaletteActivitySpec("WriteLine")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("не поддерживается"));
            Assert.That(result.Hints, Is.Not.Empty);
        });
    }

    [Test]
    public void Build_SingleSlotWithSeveralChildren_AsksForSequence()
    {
        PaletteBuildResult result = _factory.Build(new PaletteActivitySpec("If")
            .WithArgument("Condition", PaletteValue.FromExpression("true"))
            .WithChild("Then", new PaletteActivitySpec("WriteLine"))
            .WithChild("Then", new PaletteActivitySpec("WriteLine")));

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Errors.Single(), Does.Contain("Sequence"));
        });
    }
}
