using System;
using System.Activities;
using System.Activities.Statements;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.CSharp.Activities;
using RPA.Core.Generation;

namespace AiRpaStudio.Tests.Generation;

/// <summary>
/// Нагрузочный процесс для шагов 0 и 5 плана <c>AiRpaStudio\docs\UI02_Background_Validation_Plan.md</c>:
/// документ на 100+ активностей, в котором ошибки выражений лежат <b>внутри</b> тел и ветвей —
/// ровно там, куда прежний обход (<c>else { break; }</c>) не спускался.
/// <para>
/// Раскладка корневой <c>Sequence</c> фиксирована константами: адреса вида
/// <c>root/Activities[2]/Body/Handler</c> — часть контракта теста, поэтому индексы не вычисляются,
/// а объявлены рядом с построением дерева.
/// </para>
/// </summary>
internal static class ValidationWorkloadSamples
{
    /// <summary>Сколько простых шагов добавляется к документу (итого активностей — больше 100).</summary>
    internal const int StepCount = 100;

    /// <summary>Индекс <c>ForEach</c> с корректным выражением в теле (ложных ошибок быть не должно).</summary>
    internal const int ValidForEachIndex = 1;

    /// <summary>Индекс <c>ForEach</c> с ошибкой в теле (<c>ActivityAction</c>).</summary>
    internal const int ForEachWithErrorIndex = 2;

    /// <summary>Индекс <c>Pick</c> с ошибкой в ветке (<c>PickBranch</c>).</summary>
    internal const int PickWithErrorIndex = 3;

    /// <summary>Индекс <c>If</c> с ошибкой в <c>Then</c>.</summary>
    internal const int IfWithErrorIndex = 4;

    /// <summary>Адрес корректного тела <c>ForEach</c>: иконки ошибки здесь быть не должно.</summary>
    internal const string ValidForEachAddress = "root/Activities[1]/Body/Handler";

    /// <summary>Адрес тела <c>ForEach</c> с ошибкой выражения.</summary>
    internal const string ForEachErrorAddress = "root/Activities[2]/Body/Handler";

    /// <summary>Адрес действия ветки <c>Pick</c> с ошибкой выражения.</summary>
    internal const string PickErrorAddress = "root/Activities[3]/Branches[0]/Action";

    /// <summary>Адрес активности внутри <c>Then</c> с ошибкой выражения.</summary>
    internal const string IfErrorAddress = "root/Activities[4]/Then/Activities[0]";

    /// <summary>Выражение, которого нет в области видимости: компилятор Roslyn обязан его отвергнуть.</summary>
    internal const string BrokenExpression = "missingVariableInsideBody";

    /// <summary>Процесс-нагрузка: 100 шагов, корректное тело цикла, две ветки «внутри» и ошибка в <c>Then</c>.</summary>
    internal static Activity Workload()
    {
        var sequence = new Sequence { DisplayName = "Корень" };
        sequence.Variables.Add(new Variable<string>("output"));

        sequence.Activities.Add(new Assign
        {
            DisplayName = "Присваивание",
            To = new OutArgument<string>(new CSharpReference<string> { ExpressionText = "output" }),
            Value = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"start\"" })
        });

        // Корректное тело цикла: проверка «ложных ошибок внутри ActivityAction нет».
        sequence.Activities.Add(new ForEach<string>
        {
            DisplayName = "Цикл корректный",
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
        });

        // Тело ForEach с ошибкой: прежний обход сюда не спускался вообще.
        sequence.Activities.Add(new ForEach<string>
        {
            DisplayName = "Цикл с ошибкой",
            Values = new InArgument<IEnumerable<string>>(
                new CSharpValue<IEnumerable<string>> { ExpressionText = "new string[0]" }),
            Body = new ActivityAction<string>
            {
                Argument = new DelegateInArgument<string>("item"),
                Handler = new WriteLine
                {
                    DisplayName = "Цикл: ошибка в теле",
                    Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = BrokenExpression })
                }
            }
        });

        // Ветка Pick с ошибкой.
        sequence.Activities.Add(new Pick
        {
            DisplayName = "Ветвление",
            Branches =
            {
                new PickBranch
                {
                    DisplayName = "Ветка с ошибкой",
                    Trigger = new WriteLine
                    {
                        DisplayName = "Триггер 1",
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"trigger\"" })
                    },
                    Action = new WriteLine
                    {
                        DisplayName = "Действие с ошибкой",
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = BrokenExpression })
                    }
                },
                new PickBranch
                {
                    DisplayName = "Ветка корректная",
                    Trigger = new WriteLine
                    {
                        DisplayName = "Триггер 2",
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"trigger-2\"" })
                    },
                    Action = new WriteLine
                    {
                        DisplayName = "Действие корректное",
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = "\"ok\"" })
                    }
                }
            }
        });

        // Ветка If с ошибкой.
        sequence.Activities.Add(new If
        {
            DisplayName = "Если",
            Condition = new InArgument<bool>(new CSharpValue<bool> { ExpressionText = "true" }),
            Then = new Sequence
            {
                DisplayName = "Тогда",
                Activities =
                {
                    new WriteLine
                    {
                        DisplayName = "Тогда: ошибка",
                        Text = new InArgument<string>(new CSharpValue<string> { ExpressionText = BrokenExpression })
                    }
                }
            }
        });

        for (int index = 0; index < StepCount; index++)
        {
            sequence.Activities.Add(new WriteLine
            {
                DisplayName = "Шаг " + index.ToString(CultureInfo.InvariantCulture),
                Text = new InArgument<string>(new CSharpValue<string>
                {
                    ExpressionText = "output + \" " + index.ToString(CultureInfo.InvariantCulture) + "\""
                })
            });
        }

        return sequence;
    }

    /// <summary>
    /// XAML нагрузки во временном файле (UTF-8 без BOM, как пишет студия). Собирается тем же
    /// канонизатором, что и обычная генерация: файл обязан открываться конструктором.
    /// </summary>
    internal static string WriteWorkloadXaml(out string activityCountInfo)
    {
        Activity workload = Workload();
        string xaml = CanonicalXaml(workload);

        string directory = WorkflowTestSamples.CreateTempDirectory();
        string path = Path.Combine(directory, "Main.xaml");
        File.WriteAllText(path, xaml, new UTF8Encoding(false));

        activityCountInfo = "активностей в дереве образца: "
                            + System.Activities.WorkflowInspectionServices.GetActivities(workload).Count()
                                .ToString(CultureInfo.InvariantCulture);

        return path;
    }

    /// <summary>
    /// Канонизация без приговора гейта: документ намеренно содержит ошибки выражений (это предмет
    /// проверки), поэтому важна только форма XAML, а не вердикт валидации.
    /// </summary>
    internal static string CanonicalXaml(Activity implementation)
    {
        var service = new WorkflowBuildService(WorkflowTestSamples.Options(), gate: new PermissiveGate());
        WorkflowBuildResult result = service.Build(implementation);

        if (!result.Ok || string.IsNullOrEmpty(result.Xaml))
        {
            throw new InvalidOperationException(
                "Нагрузочный XAML не собран: " + Environment.NewLine + result.Describe());
        }

        return result.Xaml;
    }

    /// <summary>
    /// Гейт-заглушка: пропускает документ с ошибками <b>выражений</b> (их ищет валидатор дизайнера),
    /// но не отменяет канонизацию — форму XAML по-прежнему определяет <see cref="WorkflowBuildService"/>.
    /// </summary>
    private sealed class PermissiveGate : IWorkflowValidationGate
    {
        public WorkflowValidationReport Validate(string xamlPath)
        {
            return new WorkflowValidationReport();
        }
    }
}
