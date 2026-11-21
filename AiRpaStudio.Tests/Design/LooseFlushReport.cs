using System;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт live-сохранения loose-XAML: файл открывается в настоящем
/// <see cref="RPA.Core.WorkflowDesignerEx"/>, поверхность дизайнера материализуется (чтобы
/// <c>VirtualizedContainerService</c> записал HintSize), затем <c>Flush()</c> возвращает сериализованный
/// текст. Только данные — тесты утверждают по ним, а не по живым объектам WPF.
/// </summary>
[Serializable]
public sealed class LooseFlushReport
{
    public string Error { get; set; }

    public string FlushedText { get; set; }

    public string Description { get; set; } = string.Empty;
}
