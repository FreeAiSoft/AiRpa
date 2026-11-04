using AiRpaStudio.Tests.Design;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 5 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c> на настоящем конструкторе:
/// процесс, собранный из кода агента (ветвление + цикл + вложенность), открывается в дизайнере
/// без ошибок и без модальных окон.
/// <para>
/// Это и есть п. 1.3 плана: «валидный XAML» проверяется прогоном через настоящий
/// <c>WorkflowDesigner</c> на стенде этапа 1, а не разбором текста.
/// </para>
/// </summary>
[TestFixture]
public class CodeFirstDesignerTests
{
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

        sequence.Activities.Add(new Assign
        {
            DisplayName = ""Обнулить"",
            To = new OutArgument<int>(new CSharpReference<int> { ExpressionText = ""counter"" }),
            Value = new InArgument<int>(new CSharpValue<int> { ExpressionText = ""0"" })
        });

        sequence.Activities.Add(new ForEach<string>
        {
            DisplayName = ""Обход"",
            Values = new InArgument<IEnumerable<string>>(
                new CSharpValue<IEnumerable<string>> { ExpressionText = ""new string[0]"" }),
            Body = new ActivityAction<string>
            {
                Argument = new DelegateInArgument<string>(""item""),
                Handler = new WriteLine
                {
                    DisplayName = ""Вывод в цикле"",
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = ""item"" })
                }
            }
        });

        sequence.Activities.Add(new If
        {
            DisplayName = ""Проверка"",
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

        return sequence;
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
    public void CodeFirstWorkflow_RendersInRealDesigner()
    {
        string root = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(root);

        string directory = Path.Combine(root, "RPA-1");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "Main.xaml");

        CodeFirstResult created = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = NestedCode,
            FilePath = path
        });

        Assert.That(created.Ok, Is.True, "код не прошёл конвейер генерации." + Environment.NewLine + created.Describe());

        DesignerBenchReport report = DesignerBenchProxy.Shared.Render(path);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.True, "сгенерированный Code-First процесс не отрисовался.\n" + report.Description);
            Assert.That(report.RenderedActivities, Is.Not.Empty, "в дереве конструктора есть активности");
            Assert.That(report.UnrecognizedActivities, Is.Empty, "нераспознанных активностей быть не должно");
            Assert.That(report.ValidationErrors, Is.Empty, "валидация WF обязана быть без ошибок");
            Assert.That(report.ModalWindows, Is.Empty, "модальные окна конструктора блокируют студию");
        });
    }
}
