using System.Activities;
using System.Activities.XamlIntegration;
using System.Text;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 5 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>: Code-First через Roslyn
/// (<c>CodeFirstWorkflowService</c>).
/// <para>
/// Проверяется главное: код агента компилируется в песочнице, из сборки достаётся дерево активностей,
/// и дальше работает тот же конвейер, что у палитры, — канонизация, сериализатор WF, гейт валидации.
/// Условия, циклы и вложенность обязаны давать валидный XAML, а не «текст, похожий на XAML».
/// </para>
/// </summary>
[TestFixture]
public class CodeFirstWorkflowTests
{
    private const string SimpleCode = @"
using System;
using System.Activities;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;

public static class Workflow
{
    public static Activity Build()
    {
        var output = new Variable<string>(""output"");
        var sequence = new Sequence();
        sequence.Variables.Add(output);

        sequence.Activities.Add(new Assign
        {
            DisplayName = ""Присваивание"",
            To = new OutArgument<string>(new CSharpReference<string> { ExpressionText = ""output"" }),
            Value = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""System.DateTime.Now.ToString()"" })
        });

        sequence.Activities.Add(new WriteLine
        {
            DisplayName = ""Вывод"",
            Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""output"" })
        });

        return sequence;
    }
}
";

    private const string NestedCode = @"
using System;
using System.Collections.Generic;
using System.Activities;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;

public static class Workflow
{
    public static Activity Build()
    {
        var counter = new Variable<int>(""counter"", 0);
        var sequence = new Sequence();
        sequence.Variables.Add(counter);

        sequence.Activities.Add(new While
        {
            DisplayName = ""Цикл while"",
            Condition = new CSharpValue<bool> { ExpressionText = ""counter < 3"" },
            Body = new Sequence
            {
                Activities =
                {
                    new Assign
                    {
                        DisplayName = ""Прибавить"",
                        To = new OutArgument<int>(new CSharpReference<int> { ExpressionText = ""counter"" }),
                        Value = new InArgument<int>(new CSharpValue<int> { ExpressionText = ""counter + 1"" })
                    }
                }
            }
        });

        sequence.Activities.Add(new ForEach<string>
        {
            DisplayName = ""Цикл ForEach"",
            Values = new InArgument<IEnumerable<string>>(
                new CSharpValue<IEnumerable<string>> { ExpressionText = ""new string[0]"" }),
            Body = new ActivityAction<string>
            {
                Argument = new DelegateInArgument<string>(""item""),
                Handler = new WriteLine
                {
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""item"" })
                }
            }
        });

        sequence.Activities.Add(new If
        {
            DisplayName = ""Ветвление"",
            Condition = new InArgument<bool>(new CSharpValue<bool> { ExpressionText = ""counter > 0"" }),
            Then = new WriteLine
            {
                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""counter.ToString()"" })
            },
            Else = new WriteLine
            {
                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""(counter - 1).ToString()"" })
            }
        });

        sequence.Activities.Add(new TryCatch
        {
            DisplayName = ""Обработка ошибок"",
            Try = new Sequence
            {
                Activities =
                {
                    new WriteLine
                    {
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""counter.ToString()"" })
                    }
                }
            },
            Catches =
            {
                new Catch<Exception>
                {
                    Action = new ActivityAction<Exception>
                    {
                        Argument = new DelegateInArgument<Exception>(""ex""),
                        Handler = new WriteLine
                        {
                            Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""ex.Message"" })
                        }
                    }
                }
            }
        });

        return sequence;
    }
}
";

    private const string ActivityClassCode = @"
using System.Activities;
using System.Activities.Statements;
using Microsoft.CSharp.Activities;

public sealed class Workflow : Activity
{
    public Workflow()
    {
        Implementation = () => new Sequence
        {
            DisplayName = ""Корень класса"",
            Activities =
            {
                new WriteLine
                {
                    DisplayName = ""Вывод класса"",
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""System.DateTime.Now.Year.ToString()"" })
                }
            }
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
    public void Create_SimpleCode_ProducesCanonicalXaml()
    {
        CodeFirstResult result = Build(SimpleCode);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.EntryPoint, Is.EqualTo("Workflow.Build"), "вход процесса виден агенту");
            Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Main"));
            Assert.That(result.ActivityCount, Is.GreaterThan(2), "в дереве есть Sequence, Assign и WriteLine");
            Assert.That(result.Xaml, Does.Contain("x:Class=\"RPA_1.Main\""));
            Assert.That(result.Xaml, Does.Contain("sap2010:ExpressionActivityEditor.ExpressionActivityEditor=\"C#\""),
                "без редактора выражений конструктор покажет текст вместо выражений");
            Assert.That(result.Xaml, Does.Contain("xmlns:mca=\""), "пространство имён C#-выражений обязательно");
            Assert.That(result.Xaml, Does.Contain("TextExpression.ReferencesForImplementation"));
            Assert.That(result.Xaml, Does.Contain("mca:CSharpValue"),
                "выражения записаны как CSharpValue/CSharpReference");
        });
    }

    [Test]
    public void Create_NestedCode_ContainsConditionLoopAndTryCatch()
    {
        CodeFirstResult result = Build(NestedCode);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.Xaml, Does.Contain("<If"), "ветвление обязано попасть в XAML");
            Assert.That(result.Xaml, Does.Contain("ForEach"), "цикл ForEach обязателен");
            Assert.That(result.Xaml, Does.Contain("<While"), "цикл While обязателен");
            Assert.That(result.Xaml, Does.Contain("TryCatch"), "обработка ошибок обязательна");
            Assert.That(result.Warnings, Is.Empty, "валидация WF не должна давать предупреждений: " +
                                                   string.Join(" | ", result.Warnings));
        });
    }

    [Test]
    public void Create_UsingsFromCode_GoToXamlNamespaces()
    {
        const string code = @"
using System;
using System.Activities;
using System.Activities.Statements;
using System.Text.RegularExpressions;
using Microsoft.CSharp.Activities;

public static class Workflow
{
    public static Activity Build()
    {
        var sequence = new Sequence();
        sequence.Activities.Add(new WriteLine
        {
            Text = new InArgument<string>(
                new CSharpValue<string> { ExpressionText = ""Regex.IsMatch(string.Empty, string.Empty).ToString()"" })
        });

        return sequence;
    }
}
";

        CodeFirstResult result = Build(code);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.Namespaces, Does.Contain("System.Text.RegularExpressions"),
                "using кода обязан попасть в NamespacesForImplementation");
            Assert.That(result.Xaml, Does.Contain("System.Text.RegularExpressions"),
                "иначе конструктор покажет нерезолвленное выражение");
            Assert.That(result.References, Does.Contain("System"),
                "ссылка на сборку берётся из реально использованного пространства имён");
        });
    }

    [Test]
    public void Create_ActivityClassEntryPoint_UsesImplementation()
    {
        CodeFirstResult result = Build(ActivityClassCode);

        Assert.That(result.Ok, Is.True, result.Describe());

        Assert.Multiple(() =>
        {
            Assert.That(result.EntryPoint, Is.EqualTo("Workflow"), "входом стал класс-активность");
            Assert.That(result.Xaml, Does.Contain("WriteLine"));
            Assert.That(result.Xaml, Does.Contain("Корень класса"),
                "в XAML попал Implementation класса, а не сам класс агента");
        });
    }

    [Test]
    public void Create_NoEntryPoint_FailsWithContractHint()
    {
        const string code = @"
public static class Nothing
{
    public static int Value()
    {
        return 5;
    }
}
";

        CodeFirstResult result = Build(code);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("code.entry"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("public static Activity"),
                "агент получает контракт кода, а не «не удалось»");
        });
    }

    [Test]
    public void Create_CustomActivityInTree_IsRejected()
    {
        const string code = @"
using System.Activities;
using System.Activities.Statements;

public sealed class MyStep : CodeActivity
{
    protected override void Execute(CodeActivityContext context)
    {
    }
}

public static class Workflow
{
    public static Activity Build()
    {
        var sequence = new Sequence();
        sequence.Activities.Add(new MyStep());
        return sequence;
    }
}
";

        CodeFirstResult result = Build(code);

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False, "активность из сгенерированной сборки не сериализуется");
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("code.serialization"));
        });
    }

    [Test]
    public void Create_CompileError_ReturnsDiagnosticsAndDoesNotWriteFile()
    {
        const string code = @"
using System.Activities;
using System.Activities.Statements;

public static class Workflow
{
    public static Activity Build()
    {
        return new Sequence(
    }
}
";

        string directory = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(directory);
        string path = Path.Combine(directory, "Main.xaml");

        CodeFirstResult result = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = code,
            FilePath = path,
            ProjectName = WorkflowTestSamples.ProjectName
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Diagnostics, Is.Not.Empty, "диагностика компиляции возвращается агенту");
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Line > 0), Is.True,
                "позиция ошибки обязательна: без неё агент не исправит код");
            Assert.That(File.Exists(path), Is.False, "невалидная генерация не оставляет файл в проекте");
        });
    }

    [Test]
    public void Create_Save_WritesFileThatLoadsBackAsActivity()
    {
        string directory = Path.Combine(WorkflowTestSamples.CreateTempDirectory(), "RPA-1");
        _temporaryDirectories.Add(Path.GetDirectoryName(directory));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "Main.xaml");

        CodeFirstResult result = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = SimpleCode,
            FilePath = path
        });

        Assert.That(result.Ok, Is.True, result.Describe());
        Assert.That(File.Exists(path), Is.True, "файл процесса записан");

        // Повторная загрузка тем же API, что использует студия: доказательство, что XAML рабочий.
        Activity reloaded = ActivityXamlServices.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(result.WorkflowId, Is.EqualTo("RPA_1.Main"), "проект и класс выведены из пути");
            Assert.That(result.Opened, Is.False, "без среды конструктора вкладка не открывается");
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("вкладка не открыта"));
        });
    }

    [Test]
    public void Create_WithoutProjectAndFilePath_Fails()
    {
        CodeFirstResult result = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = SimpleCode
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("xaml.project"));
            Assert.That(string.Join(" | ", result.Hints), Does.Contain("project"), "подсказка объясняет, что передать");
        });
    }

    [Test]
    public void Create_NotXamlPath_Fails()
    {
        CodeFirstResult result = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = SimpleCode,
            FilePath = Path.Combine(Path.GetTempPath(), "Main.cs")
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(string.Join(" | ", result.Errors), Does.Contain("xaml.path"));
        });
    }

    /// <summary>Сборка процесса из кода без записи файла: параметры класса задаются явно.</summary>
    private static CodeFirstResult Build(string code)
    {
        return new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = code,
            ProjectName = WorkflowTestSamples.ProjectName,
            NamespaceName = WorkflowTestSamples.WorkflowNamespace,
            ClassName = WorkflowTestSamples.WorkflowClass
        });
    }
}
