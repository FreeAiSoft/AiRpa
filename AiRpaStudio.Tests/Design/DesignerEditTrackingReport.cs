using System;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт live-проверки отслеживания правок в loose-XAML конструкторе: после открытия вкладка чистая,
/// а реальная правка модели (зафиксированный <see cref="System.Activities.Presentation.Model.ModelEditingScope"/>)
/// помечает её «изменённой», даже когда <c>WorkflowDesigner.ModelChanged</c> не поднимается из-за
/// опубликованного <c>IDocumentPersistenceService</c>. Только данные — тесты утверждают по ним.
/// </summary>
[Serializable]
public sealed class DesignerEditTrackingReport
{
    public bool LoadSucceeded { get; set; }

    public string Error { get; set; }

    public bool CleanAfterLoad { get; set; }

    public bool EditedAfterModelChange { get; set; }

    public bool EditedAfterEmptyScope { get; set; }

    public string Description { get; set; } = string.Empty;
}
