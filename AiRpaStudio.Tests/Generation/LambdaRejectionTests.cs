using System.Activities;
using System.Activities.Expressions;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 5 конвейера генерации XAML: правило «нет лямбд в аргументах»
/// (<c>XamlExpressionPolicy</c>).
/// <para>
/// Лямбда в аргументе (<c>new InArgument&lt;int&gt;(ctx =&gt; 5)</c>) становится
/// <c>LambdaValue&lt;T&gt;</c>/<c>LambdaReference&lt;T&gt;</c> и не переживает сериализацию: в XAML
/// остаётся невалидный узел, а процесс перестаёт открываться в конструкторе. Поэтому проверка
/// выполняется до сохранения и обязана вернуть подсказку «используйте CSharpValue&lt;T&gt;».
/// </para>
/// </summary>
[TestFixture]
public class LambdaRejectionTests
{
    private const string LambdaCode = @"
using System.Activities;
using System.Activities.Statements;

public static class Workflow
{
    public static Activity Build()
    {
        return new If
        {
            DisplayName = ""Лямбда в коде"",
            Condition = new InArgument<bool>(context => true),
            Then = new Sequence()
        };
    }
}
";

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

    [Test]
    public void Check_LambdaValueInArgument_IsReported()
    {
        var activity = new If
        {
            DisplayName = "Если с лямбдой",
            Condition = new InArgument<bool>(context => true),
            Then = new WriteLine()
        };

        XamlExpressionPolicyReport report = XamlExpressionPolicy.Default.Check(activity);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.False, "лямбда в условии обязана быть отклонена");
            Assert.That(string.Join(" | ", report.Violations), Does.Contain("Condition"));
            Assert.That(string.Join(" | ", report.Violations), Does.Contain("LambdaValue"));
            Assert.That(string.Join(" | ", report.Hints), Does.Contain("CSharpValue"),
                "агент получает замену, а не только запрет");
        });
    }

    [Test]
    public void Check_LambdaReferenceInArgument_IsReported()
    {
        var assign = new Assign
        {
            DisplayName = "Присваивание с лямбдой",
            To = new OutArgument<string>(new LambdaReference<string>(context => "значение")),
            Value = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"x\"" })
        };

        XamlExpressionPolicyReport report = XamlExpressionPolicy.Default.Check(assign);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.False, "LambdaReference тоже не переживает сериализацию");
            Assert.That(string.Join(" | ", report.Violations), Does.Contain("To"));
        });
    }

    [Test]
    public void Check_CSharpExpressions_AreAllowed()
    {
        XamlExpressionPolicyReport report = XamlExpressionPolicy.Default.Check(WorkflowTestSamples.NestedWorkflow());

        Assert.That(report.Ok, Is.True, report.Describe());
    }

    [Test]
    public void Check_LambdaInNestedActivity_ReportsActivityAndArgument()
    {
        var forEach = new ForEach<string>
        {
            DisplayName = "Цикл с лямбдой",
            Values = new InArgument<IEnumerable<string>>(
                new CSharpValue<IEnumerable<string>> { ExpressionText = "new string[0]" }),
            Body = new ActivityAction<string>
            {
                Argument = new DelegateInArgument<string>("item"),
                Handler = new WriteLine
                {
                    DisplayName = "Вывод в цикле",
                    Text = new InArgument<string>(context => "item")
                }
            }
        };

        XamlExpressionPolicyReport report = XamlExpressionPolicy.Default.Check(forEach);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.False, "лямбда во вложенной активности тоже недопустима");
            Assert.That(string.Join(" | ", report.Violations), Does.Contain("Вывод в цикле"));
            Assert.That(string.Join(" | ", report.Violations), Does.Contain("Text"));
        });
    }

    [Test]
    public void Build_LambdaInArgument_IsRejectedBeforeSerialization()
    {
        var activity = new If
        {
            DisplayName = "Если с лямбдой",
            Condition = new InArgument<bool>(context => true),
            Then = new WriteLine()
        };

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Build(activity);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "гейт не должен пропускать лямбду");
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("не сериализуются"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("CSharpValue<T>"));
        });
    }

    [Test]
    public void Save_LambdaInArgument_DoesNotCreateFile()
    {
        string directory = Path.Combine(WorkflowTestSamples.CreateTempDirectory(), "RPA-1");
        _temporaryDirectories.Add(Path.GetDirectoryName(directory));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "Main.xaml");

        var activity = new If
        {
            DisplayName = "Если с лямбдой",
            Condition = new InArgument<bool>(context => true),
            Then = new WriteLine()
        };

        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Save(activity, path);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(File.Exists(path), Is.False, "невалидный процесс не остаётся в проекте");
        });
    }

    [Test]
    public void CodeFirst_LambdaInCode_IsRejectedWithReplacementHint()
    {
        CodeFirstResult result = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = LambdaCode,
            ProjectName = WorkflowTestSamples.ProjectName,
            NamespaceName = WorkflowTestSamples.WorkflowNamespace,
            ClassName = WorkflowTestSamples.WorkflowClass
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "Code-First обязан отклонять лямбды так же, как палитра");
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("лямбду"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("CSharpValue"));
        });
    }
}
