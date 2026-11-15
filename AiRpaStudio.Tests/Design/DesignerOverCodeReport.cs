using System;

namespace AiRpaStudio.Tests.Design;

/// <summary>
/// Отчёт live-проверки «редактор кода → дизайнер» на живом <see cref="RPA.Core.WorkflowDesignerEx"/>:
/// дизайнер грузится из переданного текста буфера (а не с диска), а признак «изменён» переносится
/// из редактора в дизайнер. Только данные — тесты утверждают по ним.
/// </summary>
[Serializable]
public sealed class DesignerOverCodeReport
{
    public string Error { get; set; }

    /// <summary>Текст буфера отличается от файла на диске (предусловие: иначе проверка бессмысленна).</summary>
    public bool BufferDiffersFromDisk { get; set; }

    /// <summary>Файл открыт в конструктор из текста буфера.</summary>
    public bool DesignerCreated { get; set; }

    /// <summary>После <c>Flush()</c> текст дизайнера содержит маркер из буфера, а не из файла на диске.</summary>
    public bool LoadedFromBuffer { get; set; }

    /// <summary>Признак «изменён» выставлен у дизайнера (перенос из редактора кода).</summary>
    public bool EditedCarriedOver { get; set; }

    public string FlushedText { get; set; }

    public string Description { get; set; } = string.Empty;
}
