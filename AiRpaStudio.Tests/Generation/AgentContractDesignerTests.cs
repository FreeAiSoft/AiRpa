using System;
using System.Collections.Generic;
using System.IO;
using AiRpaStudio.Tests.Design;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Этап 7 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c> на настоящем конструкторе:
/// каждый few-shot пример контракта обязан не только собраться, но и **отрисоваться** — активности
/// в дереве, валидация WF без ошибок, нераспознанных активностей и модальных окон нет.
/// <para>
/// Именно это делает пример «эталоном» для агента: если пример из документации ломает конструктор,
/// тест падает раньше, чем агент повторит его в реальном проекте.
/// </para>
/// </summary>
[TestFixture]
public class AgentContractDesignerTests
{
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

    /// <summary>Имена примеров контракта: отдельный тест на каждый сценарий.</summary>
    private static IEnumerable<string> ExampleNames()
    {
        foreach (WorkflowAgentExample example in WorkflowAgentContract.Examples)
        {
            yield return example.Name;
        }
    }

    [TestCaseSource(nameof(ExampleNames))]
    public void ContractExample_RendersInRealDesigner(string exampleName)
    {
        WorkflowAgentExample example = WorkflowAgentContract.FindExample(exampleName);
        Assert.That(example, Is.Not.Null, "пример «" + exampleName + "» отсутствует в контракте");

        string path = BuildExample(example);
        DesignerBenchReport report = DesignerBenchProxy.Shared.Render(path);

        Assert.Multiple(() =>
        {
            Assert.That(report.Ok, Is.True,
                "пример контракта «" + example.Name + "» не отрисовался.\n" + report.Description);
            Assert.That(report.RenderedActivities, Is.Not.Empty, example.Name + ": в дереве конструктора есть активности");
            Assert.That(report.UnrecognizedActivities, Is.Empty, example.Name + ": нераспознанных активностей быть не должно");
            Assert.That(report.ValidationErrors, Is.Empty, example.Name + ": валидация WF обязана быть без ошибок");
            Assert.That(report.ModalWindows, Is.Empty, example.Name + ": модальные окна конструктора блокируют студию");
        });
    }

    /// <summary>Собирает пример тем инструментом, который указан в контракте, и возвращает путь к файлу.</summary>
    private string BuildExample(WorkflowAgentExample example)
    {
        string root = WorkflowTestSamples.CreateTempDirectory();
        _temporaryDirectories.Add(root);

        string directory = Path.Combine(root, WorkflowTestSamples.ProjectName);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, WorkflowTestSamples.WorkflowClass + ".xaml");

        if (example.Format == WorkflowAgentExampleFormat.Json)
        {
            JsonWorkflowResult result = new JsonWorkflowService().Create(new JsonWorkflowRequest
            {
                Json = example.Payload,
                FilePath = path,
                ProjectName = WorkflowTestSamples.ProjectName,
                NamespaceName = WorkflowTestSamples.WorkflowNamespace,
                ClassName = WorkflowTestSamples.WorkflowClass
            });

            Assert.That(result.Ok, Is.True,
                "пример «" + example.Name + "» не прошёл конвейер." + Environment.NewLine + result.Describe());

            return path;
        }

        CodeFirstResult codeFirst = new CodeFirstWorkflowService().Create(new CodeFirstWorkflowRequest
        {
            Code = example.Payload,
            FilePath = path,
            ProjectName = WorkflowTestSamples.ProjectName,
            NamespaceName = WorkflowTestSamples.WorkflowNamespace,
            ClassName = WorkflowTestSamples.WorkflowClass
        });

        Assert.That(codeFirst.Ok, Is.True,
            "пример «" + example.Name + "» не прошёл конвейер." + Environment.NewLine + codeFirst.Describe());

        return path;
    }
}
