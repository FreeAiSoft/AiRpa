using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CSharp.Activities;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Общие образцы для тестов этапа 2 плана <c>AiRpaStudio\docs\Valid_Xaml_Designer_Plan.md</c>:
/// один и тот же процесс собирается канонизатором/сервисом в разных тестах, чтобы проверки
/// («форма каноническая», «гейт зелёный», «конструктор открывает») говорили об одном и том же XAML.
/// <para>
/// Выражения — только <see cref="CSharpValue{T}"/> и <see cref="CSharpReference{T}"/>: лямбды в аргументах
/// запрещены (п. 1.2 плана), потому что не переживают сериализацию.
/// </para>
/// </summary>
internal static class WorkflowTestSamples
{
    /// <summary>Имя сборки проекта-эталона (<c>RPA-1\Main.xaml</c>).</summary>
    internal const string ProjectName = "RPA-1";

    /// <summary>Пространство имён класса процесса (как <c>RPA_1.Main</c> в эталоне).</summary>
    internal const string WorkflowNamespace = "RPA_1";

    /// <summary>Имя класса процесса.</summary>
    internal const string WorkflowClass = "Main";

    /// <summary>Параметры канонизации эталонного процесса.</summary>
    internal static WorkflowCanonicalizationOptions Options(string projectAssemblyName = ProjectName)
    {
        return new WorkflowCanonicalizationOptions(WorkflowNamespace, WorkflowClass, projectAssemblyName);
    }

    /// <summary>Сервис сборки эталонного процесса (гейт по умолчанию, настоящий).</summary>
    internal static WorkflowBuildService BuildService(string projectAssemblyName = ProjectName)
    {
        return new WorkflowBuildService(Options(projectAssemblyName));
    }

    /// <summary>Гейт валидации с требованием ссылки на сборку проекта.</summary>
    internal static WorkflowValidationGate Gate(string projectAssemblyName = ProjectName)
    {
        return new WorkflowValidationGate(new WorkflowValidationOptions
        {
            ProjectAssemblyName = projectAssemblyName
        });
    }

    /// <summary>
    /// Простой процесс «3–5 шагов RPA»: переменная, присваивание значения выражением и вывод.
    /// Ровно тот минимум, который агент генерирует чаще всего.
    /// </summary>
    internal static Activity SimpleWorkflow()
    {
        var variable = new Variable<string>("output");

        var sequence = new Sequence();
        sequence.Variables.Add(variable);

        sequence.Activities.Add(new Assign
        {
            DisplayName = "Присваивание",
            To = new OutArgument<string>(new CSharpReference<string> { ExpressionText = "output" }),
            Value = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"Hello from RPA-1\"" })
        });

        sequence.Activities.Add(new WriteLine
        {
            DisplayName = "Вывод",
            Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "output" })
        });

        return sequence;
    }

    /// <summary>
    /// Процесс с ветвлением, циклом и вложенностью: проверяет, что IdRef назначается и вложенным
    /// активностям, а имя обобщённой активности не превращается в <c>ForEach`1_1</c>.
    /// </summary>
    internal static Activity NestedWorkflow()
    {
        var thenSequence = new Sequence
        {
            DisplayName = "Тогда",
            Activities =
            {
                new WriteLine
                {
                    DisplayName = "Тогда: вывод",
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"then\"" })
                }
            }
        };

        var forEach = new ForEach<string>
        {
            DisplayName = "Цикл",
            Values = new InArgument<IEnumerable<string>>(
                new CSharpValue<IEnumerable<string>> { ExpressionText = "new string[0]" }),
            Body = new ActivityAction<string>
            {
                Argument = new DelegateInArgument<string>("item"),
                Handler = new WriteLine
                {
                    DisplayName = "Цикл: вывод",
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "item" })
                }
            }
        };

        var sequence = new Sequence { DisplayName = "Корень" };

        sequence.Activities.Add(new If
        {
            DisplayName = "Если",
            Condition = new InArgument<bool>(new CSharpValue<bool> { ExpressionText = "true" }),
            Then = thenSequence,
            Else = new WriteLine
            {
                DisplayName = "Иначе: вывод",
                Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"else\"" })
            }
        });

        sequence.Activities.Add(forEach);

        return sequence;
    }

    /// <summary>Канонический XAML эталонного процесса (собранный через конвейер этапа 2).</summary>
    internal static string CanonicalXaml(Activity implementation = null)
    {
        WorkflowBuildResult result = BuildService().Build(implementation ?? SimpleWorkflow());
        if (!result.Ok)
        {
            throw new InvalidOperationException(
                "Эталонный процесс не прошёл гейт валидации: " + Environment.NewLine + result.Describe());
        }

        return result.Xaml;
    }

    /// <summary>Папка для временных файлов прогона (удаляется вызывающим).</summary>
    internal static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "AiRpaStudio-Generation",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Пишет XAML во временный файл и возвращает путь (UTF-8 без BOM, как студия).</summary>
    internal static string WriteTempXaml(string xaml)
    {
        string path = Path.Combine(CreateTempDirectory(), "Main.xaml");
        File.WriteAllText(path, xaml, new UTF8Encoding(false));

        return path;
    }
}
