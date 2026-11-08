using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Xml.Linq;
using Microsoft.CSharp.Activities;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// `UI-09` (доска `RPA - доска`): «импорт параметра `new List<string>` теряет угловую скобку».
/// <para>
/// Здесь проверяются две гипотезы плана <c>dev-dev-docs\UI02_Background_Validation_Plan.md</c> §4,
/// которые можно проверить кодом без интерактивной студии:
/// </para>
/// <list type="number">
/// <item><b>XML-экранирование</b> при записи выражения: <c>&lt;</c>/<c>&gt;</c> обязаны попасть в файл как
/// <c>&amp;lt;</c>/<c>&amp;gt;</c>, иначе разметка рвётся и «теряется» хвост выражения;</item>
/// <item><b>обрезка текста</b> при чтении: разобранное значение обязано совпадать с исходным выражением
/// символ в символ — уже с угловыми скобками.</item>
/// </list>
/// <para>
/// Если обе проверки зелёные, путь «генерация/сериализация» чист, и баг живёт в интерактивном
/// редактировании значения аргумента (третья гипотеза §4 — пере-сборка типа через <c>Type.Name</c>,
/// из-за которой <c>List&lt;string&gt;</c> превращается в <c>List`1</c>).
/// </para>
/// </summary>
[TestFixture]
public class ArgumentExpressionEscapingTests
{
    private const string Expression = "new List<string>()";

    /// <summary>Выражение с обобщённым типом записывается с экранированием, а не как «сырая» разметка.</summary>
    [Test]
    public void GenericTypeExpression_IsEscapedInXaml()
    {
        string xaml = WorkflowTestSamples.CanonicalXaml(WorkflowWithGenericExpression());

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ui09-generic.xaml"), xaml);

        Assert.That(xaml, Does.Contain("new List&lt;string&gt;()"),
            "выражение с обобщённым типом записано без экранирования угловых скобок:"
            + Environment.NewLine + DescribeExpressionLines(xaml));
    }

    /// <summary>Чтение возвращает выражение целиком: угловая скобка не обрезается.</summary>
    [Test]
    public void GenericTypeExpression_SurvivesReadBack()
    {
        string xaml = WorkflowTestSamples.CanonicalXaml(WorkflowWithGenericExpression());

        XDocument document = XDocument.Parse(xaml);

        var values = new List<string>();

        // Выражение WF хранится смешанным содержимым: текст идёт ПОСЛЕ дочернего
        // <mca:CSharpValue.Result>, поэтому значение элемента его не содержит — нужны текстовые узлы.
        foreach (XText text in document.DescendantNodes().OfType<XText>())
        {
            values.Add(text.Value);
        }
        foreach (XElement element in document.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                values.Add(attribute.Value);
            }

            if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
            {
                values.Add(element.Value);
            }
        }

        Assert.That(values.Exists(value => value.Trim() == Expression), Is.True,
            "после разбора XAML выражения «" + Expression + "» нет — значит при записи/чтении теряется хвост:"
            + Environment.NewLine + DescribeExpressionLines(xaml));
    }

    /// <summary>Документ по-прежнему проходит гейт: экранирование не ломает форму XAML.</summary>
    [Test]
    public void DocumentWithGenericTypeExpression_PassesGate()
    {
        WorkflowBuildResult result = WorkflowTestSamples.BuildService().Build(WorkflowWithGenericExpression());

        Assert.That(result.Ok, Is.True,
            "документ с выражением обобщённого типа не проходит гейт: " + result.Describe());
    }

    /// <summary>Процесс с выражением обобщённого типа: переменная <c>List&lt;string&gt;</c> и её использование.</summary>
    private static Activity WorkflowWithGenericExpression()
    {
        var sequence = new Sequence { DisplayName = "Корень" };
        sequence.Variables.Add(new Variable<List<string>>("list"));

        sequence.Activities.Add(new Assign
        {
            DisplayName = "Список",
            To = new OutArgument<List<string>>(new CSharpReference<List<string>> { ExpressionText = "list" }),
            Value = new InArgument<List<string>>(new CSharpValue<List<string>> { ExpressionText = Expression })
        });

        sequence.Activities.Add(new WriteLine
        {
            DisplayName = "Вывод размера",
            Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "list.Count.ToString()" })
        });

        return sequence;
    }

    /// <summary>Строки XAML, где встречается выражение (для сообщения об ошибке).</summary>
    private static string DescribeExpressionLines(string xaml)
    {
        var lines = new List<string>();
        foreach (string line in xaml.Split('\n'))
        {
            if (line.Contains("List") || line.Contains("list") || line.Contains("new "))
            {
                lines.Add("  " + line.Trim());
            }
        }

        return lines.Count == 0 ? "  (выражение в XAML не найдено)" : string.Join(Environment.NewLine, lines);
    }
}
