using System;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт live-проверки специфических фич контекстного меню WF-дизайнера: включены ли аннотации и
/// опубликованы ли сервисы, от которых зависит видимость пунктов «Точка останова»/«Аннотации».
/// Только данные — тесты утверждают по ним.
/// </summary>
[Serializable]
public sealed class DesignerFeaturesReport
{
    public bool LoadSucceeded { get; set; }

    public string Error { get; set; }

    public bool AnnotationEnabled { get; set; }

    public bool DebugViewAvailable { get; set; }

    public bool CommandServiceAvailable { get; set; }

    public bool BreakpointServicesEnabled { get; set; }

    public string Description { get; set; } = string.Empty;
}