using System;
using System.Collections.Generic;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт live-открытия loose-XAML: файл открылся через <see cref="RPA.Core.Workflow.LooseXamlDesignerLoader"/>,
/// <see cref="System.Activities.Presentation.WorkflowFileItem"/> выставлен вручную, вложенные ссылки
/// превратились в фасады <see cref="System.Activities.DynamicActivity"/>. Только данные — тесты
/// утверждают по ним, а не по живым объектам WPF.
/// </summary>
[Serializable]
public sealed class LooseOpenReport
{
    public bool LoadSucceeded { get; set; }

    public string Error { get; set; }

    public string WorkflowFileItem { get; set; }

    public List<string> FacadeNames { get; set; } = new List<string>();

    public string Description { get; set; } = string.Empty;
}
